import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { describeSendError } from '../core/api-error';
import { ContactSummaryDto, MAX_REPLY_LENGTH, MessageDto, MessageEvent } from '../core/models';
import { RealtimeService } from '../core/realtime.service';
import {
  applyLastMessage,
  clearUnread,
  MessageSource,
  latestUnreadInbound,
  replaceContacts,
  upsertContact,
  upsertMessage,
  upsertMessages,
} from './inbox.reducers';

interface ConversationState {
  contactId: string;
  messages: MessageDto[];
}

/** Single source of UI state. Components read its signals and call its commands; they never touch I/O. */
@Injectable({ providedIn: 'root' })
export class InboxStore {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly contactsState = signal<ContactSummaryDto[]>([]);
  private readonly contactsLoadedState = signal(false);
  private readonly contactsErrorState = signal<string | null>(null);
  private readonly selectedIdState = signal<string | null>(null);
  private readonly conversationState = signal<ConversationState | null>(null);
  private readonly conversationLoadingState = signal(false);
  private readonly conversationErrorState = signal<string | null>(null);
  private readonly draftsState = signal<Readonly<Record<string, string>>>({});
  private readonly sendingState = signal(false);
  private readonly sendErrorState = signal<string | null>(null);
  private readonly retryingState = signal<ReadonlySet<string>>(new Set());
  private initialized = false;

  readonly connectionStatus = this.realtime.status;
  readonly contacts = this.contactsState.asReadonly();
  readonly contactsLoaded = this.contactsLoadedState.asReadonly();
  readonly contactsError = this.contactsErrorState.asReadonly();
  readonly selectedContactId = this.selectedIdState.asReadonly();
  readonly conversationLoading = this.conversationLoadingState.asReadonly();
  readonly conversationError = this.conversationErrorState.asReadonly();
  readonly sending = this.sendingState.asReadonly();
  readonly sendError = this.sendErrorState.asReadonly();
  readonly retrying = this.retryingState.asReadonly();

  readonly selectedContact = computed(() => {
    const id = this.selectedIdState();
    return id ? (this.contactsState().find((c) => c.id === id) ?? null) : null;
  });

  readonly messages = computed(() => {
    const conversation = this.conversationState();
    return conversation && conversation.contactId === this.selectedIdState() ? conversation.messages : [];
  });

  readonly draft = computed(() => {
    const id = this.selectedIdState();
    return id ? (this.draftsState()[id] ?? '') : '';
  });

  /** Connects the hub first (contracts §7.10), then loads the contact list. Idempotent. */
  async init(): Promise<void> {
    if (this.initialized) {
      return;
    }
    this.initialized = true;

    this.realtime.messageReceived$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((e) => this.onMessageReceived(e));
    this.realtime.messageQueued$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((e) => this.onMessageEvent(e, 'other'));
    this.realtime.messageStatusChanged$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((e) => this.onMessageEvent(e, 'statusEvent'));
    this.realtime.conversationRead$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((e) => this.contactsState.update((list) => upsertContact(list, e.contact, 'other')));
    this.realtime.resync$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => void this.resync());

    await this.realtime.connect();
    await this.loadContacts();
  }

  /** Opens a conversation: loads its history and marks it as read. */
  async select(contactId: string): Promise<void> {
    if (this.selectedIdState() === contactId) {
      return;
    }
    this.selectedIdState.set(contactId);
    this.conversationState.set({ contactId, messages: [] });
    this.conversationErrorState.set(null);
    this.sendErrorState.set(null);
    await this.loadConversation(contactId);
  }

  setDraft(text: string): void {
    const id = this.selectedIdState();
    if (id) {
      this.draftsState.update((drafts) => ({ ...drafts, [id]: text }));
    }
  }

  /** Sends the current draft. On failure the draft is kept and the error is exposed. */
  async send(): Promise<void> {
    const contactId = this.selectedIdState();
    const text = this.draft();
    if (!contactId || this.sendingState() || text.trim().length === 0 || text.length > MAX_REPLY_LENGTH) {
      return;
    }
    this.sendingState.set(true);
    this.sendErrorState.set(null);
    try {
      const message = await firstValueFrom(this.api.sendReply(contactId, text));
      this.applyMessage(message, 'other');
      this.draftsState.update((drafts) => ({ ...drafts, [contactId]: '' }));
    } catch (error) {
      if (this.selectedIdState() === contactId) {
        this.sendErrorState.set(describeSendError(error));
      }
    } finally {
      this.sendingState.set(false);
    }
  }

  /**
   * Re-queues a failed reply. The status moves back to pending only through the
   * `messageStatusChanged` event (status precedence, contracts §7.10).
   */
  async retry(messageId: string): Promise<void> {
    if (this.retryingState().has(messageId)) {
      return;
    }
    this.retryingState.update((set) => new Set(set).add(messageId));
    try {
      const message = await firstValueFrom(this.api.retry(messageId));
      this.applyMessage(message, 'other');
    } catch {
      // A 409 means it is no longer failed; refetching shows the real state either way.
      const id = this.selectedIdState();
      if (id) {
        await this.loadConversation(id);
      }
    } finally {
      this.retryingState.update((set) => {
        const next = new Set(set);
        next.delete(messageId);
        return next;
      });
    }
  }

  private async loadContacts(): Promise<void> {
    try {
      const contacts = await firstValueFrom(this.api.getContacts());
      this.contactsState.update((current) => replaceContacts(current, contacts));
      this.contactsErrorState.set(null);
    } catch {
      this.contactsErrorState.set('No se pudo cargar la lista de conversaciones.');
    } finally {
      this.contactsLoadedState.set(true);
    }
  }

  private async loadConversation(contactId: string): Promise<void> {
    this.conversationLoadingState.set(true);
    try {
      const fetched = await firstValueFrom(this.api.getMessages(contactId));
      if (this.selectedIdState() !== contactId) {
        return;
      }
      this.conversationState.update((current) => ({
        contactId,
        messages: upsertMessages(current?.contactId === contactId ? current.messages : [], fetched, 'other'),
      }));
      this.conversationErrorState.set(null);
      const unread = latestUnreadInbound(this.messages());
      if (unread) {
        await this.markRead(contactId, unread.id);
      }
    } catch {
      if (this.selectedIdState() === contactId) {
        this.conversationErrorState.set('No se pudo cargar la conversación.');
      }
    } finally {
      if (this.selectedIdState() === contactId) {
        this.conversationLoadingState.set(false);
      }
    }
  }

  private async markRead(contactId: string, upToMessageId: string): Promise<void> {
    try {
      await firstValueFrom(this.api.markRead(contactId, upToMessageId));
      this.contactsState.update((list) => clearUnread(list, contactId));
    } catch {
      // The badge stays as the server reports it; the next open or incoming message retries the marking.
    }
  }

  private onMessageReceived(event: MessageEvent): void {
    this.onMessageEvent(event, 'other');
    const contactId = event.contact.id;
    if (this.selectedIdState() === contactId && event.contact.unreadCount > 0) {
      void this.markRead(contactId, event.message.id);
    }
  }

  private onMessageEvent(event: MessageEvent, source: MessageSource): void {
    this.contactsState.update((list) => upsertContact(list, event.contact, source));
    this.upsertIntoConversation(event.message, source);
  }

  /** Applies a message known from an HTTP response (no contact payload) to the conversation and the preview. */
  private applyMessage(message: MessageDto, source: MessageSource): void {
    this.upsertIntoConversation(message, source);
    this.contactsState.update((list) => applyLastMessage(list, message));
  }

  private upsertIntoConversation(message: MessageDto, source: MessageSource): void {
    this.conversationState.update((current) =>
      current && current.contactId === message.contactId
        ? { contactId: current.contactId, messages: upsertMessage(current.messages, message, source) }
        : current,
    );
  }

  /** After a reconnection: refetch the contact list and the open conversation (contracts §3). */
  private async resync(): Promise<void> {
    await this.loadContacts();
    const id = this.selectedIdState();
    if (id) {
      await this.loadConversation(id);
    }
  }
}
