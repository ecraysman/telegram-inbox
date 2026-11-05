import { ContactSummaryDto, MessageDto } from '../core/models';
import {
  applyLastMessage,
  latestUnreadInbound,
  mergeMessage,
  replaceContacts,
  sortContacts,
  upsertContact,
  upsertMessage,
} from './inbox.reducers';

function msg(overrides: Partial<MessageDto> & Pick<MessageDto, 'id'>): MessageDto {
  return {
    seq: 1,
    contactId: 'c1',
    direction: 'inbound',
    text: 'hola',
    occurredAt: '2026-11-05T14:00:00+00:00',
    readAt: null,
    status: 'received',
    failureReason: null,
    ...overrides,
  };
}

function contact(id: string, lastMessage: MessageDto | null, unreadCount = 0): ContactSummaryDto {
  return { id, displayName: id, username: null, unreadCount, lastMessage };
}

describe('mergeMessage (status precedence)', () => {
  it('never replaces sent with pending outside a status event', () => {
    const sent = msg({ id: 'm', direction: 'outbound', status: 'sent' });
    const pending = { ...sent, status: 'pending' as const };
    expect(mergeMessage(sent, pending, 'other')).toBe(sent);
  });

  it('never replaces failed with pending outside a status event', () => {
    const failed = msg({ id: 'm', direction: 'outbound', status: 'failed', failureReason: 'x' });
    expect(mergeMessage(failed, { ...failed, status: 'pending', failureReason: null }, 'other')).toBe(failed);
  });

  it('accepts failed to pending from a status event (retry)', () => {
    const failed = msg({ id: 'm', direction: 'outbound', status: 'failed' });
    const pending = { ...failed, status: 'pending' as const };
    expect(mergeMessage(failed, pending, 'statusEvent')).toBe(pending);
  });

  it('accepts forward transitions from any source', () => {
    const pending = msg({ id: 'm', direction: 'outbound', status: 'pending' });
    const sent = { ...pending, status: 'sent' as const };
    expect(mergeMessage(pending, sent, 'other')).toBe(sent);
  });
});

describe('upsertMessage', () => {
  it('is idempotent for duplicate events', () => {
    const m = msg({ id: 'a' });
    const once = upsertMessage([], m, 'other');
    const twice = upsertMessage(once, m, 'other');
    expect(twice).toEqual([m]);
  });

  it('orders by parsed occurredAt, then seq', () => {
    const late = msg({ id: 'late', seq: 1, occurredAt: '2026-11-05T15:00:00Z' });
    // Same instant written with an offset: string comparison would get this wrong.
    const early = msg({ id: 'early', seq: 9, occurredAt: '2026-11-05T16:00:00+05:00' });
    const tie = msg({ id: 'tie', seq: 2, occurredAt: '2026-11-05T14:30:00.000+00:00' });
    const result = [late, early, tie].reduce<MessageDto[]>((list, m) => upsertMessage(list, m, 'other'), []);
    expect(result.map((m) => m.id)).toEqual(['early', 'tie', 'late']);
  });
});

describe('contacts', () => {
  const older = msg({ id: 'o', contactId: 'a', occurredAt: '2026-11-05T10:00:00Z' });
  const newer = msg({ id: 'n', contactId: 'b', occurredAt: '2026-11-05T12:00:00Z' });

  it('sorts by last activity, newest first, empty contacts last', () => {
    const sorted = sortContacts([contact('empty', null), contact('a', older), contact('b', newer)]);
    expect(sorted.map((c) => c.id)).toEqual(['b', 'a', 'empty']);
  });

  it('upserts by id and re-sorts', () => {
    const list = sortContacts([contact('a', older), contact('b', newer)]);
    const latest = msg({ id: 'x', contactId: 'a', occurredAt: '2026-11-05T13:00:00Z' });
    const result = upsertContact(list, contact('a', latest, 1), 'other');
    expect(result.map((c) => c.id)).toEqual(['a', 'b']);
    expect(result[0].unreadCount).toBe(1);
    expect(upsertContact(result, contact('a', latest, 1), 'other')).toEqual(result);
  });

  it('keeps the precedence of the last message status', () => {
    const sent = msg({ id: 's', contactId: 'a', direction: 'outbound', status: 'sent' });
    const list = [contact('a', sent)];
    const stale = contact('a', { ...sent, status: 'pending' });
    expect(upsertContact(list, stale, 'other')[0].lastMessage?.status).toBe('sent');
    expect(replaceContacts(list, [stale])[0].lastMessage?.status).toBe('sent');
  });

  it('updates the preview only with a newer message', () => {
    const list = [contact('a', older)];
    const reply = msg({ id: 'r', contactId: 'a', direction: 'outbound', status: 'pending', occurredAt: '2026-11-05T11:00:00Z' });
    expect(applyLastMessage(list, reply)[0].lastMessage?.id).toBe('r');
    const ancient = msg({ id: 'z', contactId: 'a', occurredAt: '2026-11-04T11:00:00Z' });
    expect(applyLastMessage(list, ancient)[0].lastMessage?.id).toBe('o');
  });
});

describe('latestUnreadInbound', () => {
  it('returns the newest inbound message without readAt', () => {
    const list = [
      msg({ id: '1', seq: 1 }),
      msg({ id: '2', seq: 2 }),
      msg({ id: '3', seq: 3, direction: 'outbound', status: 'sent', readAt: '2026-11-05T14:00:00Z' }),
    ];
    expect(latestUnreadInbound(list)?.id).toBe('2');
    expect(latestUnreadInbound([msg({ id: '1', readAt: '2026-11-05T14:00:00Z' })])).toBeNull();
  });
});
