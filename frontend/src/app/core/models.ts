// Wire types, identical to contracts.md §2 and §3.

export type Direction = 'inbound' | 'outbound';
export type MessageStatus = 'received' | 'pending' | 'sent' | 'failed';

export interface MessageDto {
  id: string;
  seq: number;
  contactId: string;
  direction: Direction;
  text: string;
  occurredAt: string;
  readAt: string | null;
  status: MessageStatus;
  failureReason: string | null;
}

export interface ContactSummaryDto {
  id: string;
  displayName: string;
  username: string | null;
  unreadCount: number;
  lastMessage: MessageDto | null;
}

export interface MarkReadRequest {
  upToMessageId: string;
}

export interface SendReplyRequest {
  text: string;
}

/** Payload of `messageReceived`, `messageQueued` and `messageStatusChanged`. */
export interface MessageEvent {
  message: MessageDto;
  contact: ContactSummaryDto;
}

/** Payload of `conversationRead`. */
export interface ConversationReadEvent {
  contact: ContactSummaryDto;
}

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting';

/** Maximum reply length, counted in UTF-16 code units (contracts §7.6). */
export const MAX_REPLY_LENGTH = 4096;
