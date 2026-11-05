import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApiService } from '../core/api.service';
import { ConnectionStatus, ContactSummaryDto, ConversationReadEvent, MessageDto, MessageEvent } from '../core/models';
import { RealtimeService } from '../core/realtime.service';
import { InboxStore } from './inbox.store';

function msg(overrides: Partial<MessageDto> & Pick<MessageDto, 'id'>): MessageDto {
  return {
    seq: 1,
    contactId: 'c1',
    direction: 'inbound',
    text: 'hola',
    occurredAt: '2026-11-05T14:00:00Z',
    readAt: null,
    status: 'received',
    failureReason: null,
    ...overrides,
  };
}

function contact(id: string, lastMessage: MessageDto | null, unreadCount = 0): ContactSummaryDto {
  return { id, displayName: `Contacto ${id}`, username: null, unreadCount, lastMessage };
}

class FakeRealtime {
  readonly calls: string[];
  readonly status = signal<ConnectionStatus>('connecting').asReadonly();
  readonly messageReceived$ = new Subject<MessageEvent>();
  readonly messageQueued$ = new Subject<MessageEvent>();
  readonly messageStatusChanged$ = new Subject<MessageEvent>();
  readonly conversationRead$ = new Subject<ConversationReadEvent>();
  readonly resync$ = new Subject<void>();

  constructor(calls: string[]) {
    this.calls = calls;
  }

  connect(): Promise<void> {
    this.calls.push('connect');
    return Promise.resolve();
  }
}

class FakeApi {
  readonly calls: string[];
  contacts: ContactSummaryDto[] = [];
  messages: Record<string, MessageDto[]> = {};
  sendResult: Observable<MessageDto> = throwError(() => new HttpErrorResponse({ status: 500 }));

  constructor(calls: string[]) {
    this.calls = calls;
  }

  getContacts(): Observable<ContactSummaryDto[]> {
    this.calls.push('getContacts');
    return of(this.contacts);
  }

  getMessages(contactId: string): Observable<MessageDto[]> {
    this.calls.push(`getMessages:${contactId}`);
    return of(this.messages[contactId] ?? []);
  }

  markRead(contactId: string, upToMessageId: string): Observable<void> {
    this.calls.push(`markRead:${contactId}:${upToMessageId}`);
    return of(undefined);
  }

  sendReply(contactId: string, text: string): Observable<MessageDto> {
    this.calls.push(`sendReply:${contactId}:${text}`);
    return this.sendResult;
  }

  retry(messageId: string): Observable<MessageDto> {
    this.calls.push(`retry:${messageId}`);
    return of(msg({ id: messageId, direction: 'outbound', status: 'pending' }));
  }
}

describe('InboxStore', () => {
  let calls: string[];
  let api: FakeApi;
  let realtime: FakeRealtime;
  let store: InboxStore;

  const first = msg({ id: 'm1', seq: 1, occurredAt: '2026-11-05T14:00:00Z' });

  beforeEach(() => {
    calls = [];
    api = new FakeApi(calls);
    realtime = new FakeRealtime(calls);
    api.contacts = [contact('c1', first, 1), contact('c2', msg({ id: 'm9', contactId: 'c2', occurredAt: '2026-11-05T13:00:00Z' }))];
    api.messages = { c1: [first] };
    TestBed.configureTestingModule({
      providers: [
        { provide: ApiService, useValue: api },
        { provide: RealtimeService, useValue: realtime },
      ],
    });
    store = TestBed.inject(InboxStore);
  });

  it('connects the hub before the first REST fetch', async () => {
    await store.init();
    expect(calls.slice(0, 2)).toEqual(['connect', 'getContacts']);
    expect(store.contacts().map((c) => c.id)).toEqual(['c1', 'c2']);
  });

  it('marks the conversation as read when opened and clears the badge', async () => {
    await store.init();
    await store.select('c1');
    expect(calls).toContain('markRead:c1:m1');
    expect(store.selectedContact()?.unreadCount).toBe(0);
    expect(store.messages().map((m) => m.id)).toEqual(['m1']);
  });

  it('does not call mark-as-read when nothing is unread', async () => {
    api.messages['c1'] = [{ ...first, readAt: '2026-11-05T14:01:00Z' }];
    await store.init();
    await store.select('c1');
    expect(calls.some((c) => c.startsWith('markRead'))).toBe(false);
  });

  it('applies an incoming message to the list and the open conversation, and marks it read', async () => {
    await store.init();
    await store.select('c1');
    const incoming = msg({ id: 'm2', seq: 2, occurredAt: '2026-11-05T14:05:00Z' });
    const event: MessageEvent = { message: incoming, contact: contact('c1', incoming, 1) };

    realtime.messageReceived$.next(event);
    realtime.messageReceived$.next(event);
    await Promise.resolve();

    expect(store.messages().map((m) => m.id)).toEqual(['m1', 'm2']);
    expect(store.contacts()[0].lastMessage?.id).toBe('m2');
    expect(calls.filter((c) => c === 'markRead:c1:m2').length).toBeGreaterThan(0);
  });

  it('re-sorts the list and counts unread for a conversation that is not open', async () => {
    await store.init();
    const incoming = msg({ id: 'm3', contactId: 'c2', seq: 3, occurredAt: '2026-11-05T15:00:00Z' });
    realtime.messageReceived$.next({ message: incoming, contact: contact('c2', incoming, 2) });

    expect(store.contacts().map((c) => c.id)).toEqual(['c2', 'c1']);
    expect(store.contacts()[0].unreadCount).toBe(2);
    expect(calls.some((c) => c.startsWith('markRead:c2'))).toBe(false);
  });

  it('keeps sent when a late messageQueued arrives, but accepts a retry status event', async () => {
    await store.init();
    await store.select('c1');
    const sent = msg({ id: 'r1', seq: 5, direction: 'outbound', status: 'sent', occurredAt: '2026-11-05T14:10:00Z' });
    realtime.messageStatusChanged$.next({ message: sent, contact: contact('c1', sent) });
    realtime.messageQueued$.next({ message: { ...sent, status: 'pending' }, contact: contact('c1', { ...sent, status: 'pending' }) });
    expect(store.messages().find((m) => m.id === 'r1')?.status).toBe('sent');

    const failed = { ...sent, status: 'failed' as const, failureReason: 'rejected: blocked' };
    realtime.messageStatusChanged$.next({ message: failed, contact: contact('c1', failed) });
    realtime.messageStatusChanged$.next({ message: { ...failed, status: 'pending', failureReason: null }, contact: contact('c1', null) });
    expect(store.messages().find((m) => m.id === 'r1')?.status).toBe('pending');
  });

  it('keeps the draft and exposes the error when sending fails', async () => {
    await store.init();
    await store.select('c1');
    store.setDraft('respuesta');
    await store.send();
    expect(store.draft()).toBe('respuesta');
    expect(store.sendError()).not.toBeNull();
    expect(store.sending()).toBe(false);
  });

  it('clears the draft and shows the reply after a successful send', async () => {
    const reply = msg({ id: 'r2', seq: 6, direction: 'outbound', status: 'pending', occurredAt: '2026-11-05T14:20:00Z' });
    api.sendResult = of(reply);
    await store.init();
    await store.select('c1');
    store.setDraft('respuesta');
    await store.send();
    expect(calls).toContain('sendReply:c1:respuesta');
    expect(store.draft()).toBe('');
    expect(store.messages().at(-1)?.id).toBe('r2');
    expect(store.contacts()[0].lastMessage?.id).toBe('r2');
  });

  it('does not send blank or too long drafts', async () => {
    await store.init();
    await store.select('c1');
    store.setDraft('   ');
    await store.send();
    store.setDraft('x'.repeat(4097));
    await store.send();
    expect(calls.some((c) => c.startsWith('sendReply'))).toBe(false);
  });

  it('refetches the contacts and the open conversation on resync', async () => {
    await store.init();
    await store.select('c1');
    calls.length = 0;
    realtime.resync$.next();
    await new Promise((resolve) => setTimeout(resolve));
    expect(calls).toContain('getContacts');
    expect(calls).toContain('getMessages:c1');
  });
});
