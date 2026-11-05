// Pure state transitions used by InboxStore. Every upsert is by id, so duplicate events are harmless.

import { ContactSummaryDto, MessageDto } from '../core/models';

/** Where an incoming message version comes from; only status events may move a message back to pending. */
export type MessageSource = 'statusEvent' | 'other';

/** Orders messages by (occurredAt, seq) ascending; timestamps are parsed, never compared as strings. */
export function compareMessages(a: MessageDto, b: MessageDto): number {
  const byTime = Date.parse(a.occurredAt) - Date.parse(b.occurredAt);
  return byTime !== 0 ? byTime : a.seq - b.seq;
}

/**
 * Status precedence (contracts §7.10): a version with `pending` never replaces `sent` or `failed`,
 * unless it comes from a `messageStatusChanged` event (a retry).
 */
export function mergeMessage(existing: MessageDto, incoming: MessageDto, source: MessageSource): MessageDto {
  const isDowngrade =
    incoming.status === 'pending' && (existing.status === 'sent' || existing.status === 'failed');
  if (isDowngrade && source !== 'statusEvent') {
    return existing;
  }
  return incoming;
}

/** Inserts or replaces one message and keeps the list sorted. */
export function upsertMessage(list: readonly MessageDto[], incoming: MessageDto, source: MessageSource): MessageDto[] {
  return upsertMessages(list, [incoming], source);
}

/** Inserts or replaces several messages and keeps the list sorted. */
export function upsertMessages(
  list: readonly MessageDto[],
  incoming: readonly MessageDto[],
  source: MessageSource,
): MessageDto[] {
  const byId = new Map(list.map((m) => [m.id, m] as const));
  for (const message of incoming) {
    const existing = byId.get(message.id);
    byId.set(message.id, existing ? mergeMessage(existing, message, source) : message);
  }
  return [...byId.values()].sort(compareMessages);
}

/** Orders contacts by last activity (`lastMessage.occurredAt` desc); contacts without messages go last. */
export function sortContacts(contacts: readonly ContactSummaryDto[]): ContactSummaryDto[] {
  return [...contacts].sort((a, b) => {
    if (!a.lastMessage || !b.lastMessage) {
      return (a.lastMessage ? 0 : 1) - (b.lastMessage ? 0 : 1);
    }
    return compareMessages(b.lastMessage, a.lastMessage);
  });
}

/** Inserts or replaces a contact (applying status precedence to its last message) and re-sorts. */
export function upsertContact(
  contacts: readonly ContactSummaryDto[],
  incoming: ContactSummaryDto,
  source: MessageSource,
): ContactSummaryDto[] {
  const existing = contacts.find((c) => c.id === incoming.id);
  const merged = existing ? mergeContact(existing, incoming, source) : incoming;
  return sortContacts([...contacts.filter((c) => c.id !== incoming.id), merged]);
}

function mergeContact(
  existing: ContactSummaryDto,
  incoming: ContactSummaryDto,
  source: MessageSource,
): ContactSummaryDto {
  const previous = existing.lastMessage;
  const next = incoming.lastMessage;
  if (previous && next && previous.id === next.id) {
    return { ...incoming, lastMessage: mergeMessage(previous, next, source) };
  }
  return incoming;
}

/** Replaces the whole list from a REST fetch, keeping status precedence for known last messages. */
export function replaceContacts(
  current: readonly ContactSummaryDto[],
  fetched: readonly ContactSummaryDto[],
): ContactSummaryDto[] {
  const byId = new Map(current.map((c) => [c.id, c] as const));
  return sortContacts(
    fetched.map((contact) => {
      const existing = byId.get(contact.id);
      return existing ? mergeContact(existing, contact, 'other') : contact;
    }),
  );
}

/** Updates a contact's preview when a newer message of its conversation is known locally. */
export function applyLastMessage(contacts: readonly ContactSummaryDto[], message: MessageDto): ContactSummaryDto[] {
  const contact = contacts.find((c) => c.id === message.contactId);
  if (!contact) {
    return [...contacts];
  }
  const last = contact.lastMessage;
  let lastMessage: MessageDto;
  if (!last || compareMessages(last, message) < 0) {
    lastMessage = message;
  } else if (last.id === message.id) {
    lastMessage = mergeMessage(last, message, 'other');
  } else {
    return [...contacts];
  }
  return upsertContact(contacts, { ...contact, lastMessage }, 'other');
}

/** Clears a contact's unread counter (used after a successful mark-as-read). */
export function clearUnread(contacts: readonly ContactSummaryDto[], contactId: string): ContactSummaryDto[] {
  return contacts.map((c) => (c.id === contactId && c.unreadCount !== 0 ? { ...c, unreadCount: 0 } : c));
}

/** The latest inbound message without `readAt`, i.e. the `upToMessageId` for a mark-as-read call. */
export function latestUnreadInbound(messages: readonly MessageDto[]): MessageDto | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const message = messages[i];
    if (message.direction === 'inbound' && message.readAt === null) {
      return message;
    }
  }
  return null;
}
