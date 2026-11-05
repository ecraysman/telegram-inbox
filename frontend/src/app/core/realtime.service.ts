import { Injectable, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  IRetryPolicy,
  LogLevel,
  RetryContext,
} from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { ConnectionStatus, ConversationReadEvent, MessageEvent } from './models';

/** Backoff of contracts §7.10: 0, 2, 5, 10 s, then every 30 s, forever. */
const RETRY_DELAYS_MS = [0, 2_000, 5_000, 10_000];
const STEADY_RETRY_MS = 30_000;

export function retryDelayMs(previousRetryCount: number): number {
  return RETRY_DELAYS_MS[previousRetryCount] ?? STEADY_RETRY_MS;
}

const neverEndingRetry: IRetryPolicy = {
  nextRetryDelayInMilliseconds: (context: RetryContext) => retryDelayMs(context.previousRetryCount),
};

/**
 * SignalR client for the server-to-client hub `/hubs/inbox` (contracts §3).
 * Exposes a connection-status signal and one stream per hub method.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly statusSignal = signal<ConnectionStatus>('connecting');
  private readonly messageReceivedSubject = new Subject<MessageEvent>();
  private readonly messageQueuedSubject = new Subject<MessageEvent>();
  private readonly messageStatusChangedSubject = new Subject<MessageEvent>();
  private readonly conversationReadSubject = new Subject<ConversationReadEvent>();
  private readonly resyncSubject = new Subject<void>();

  private connection: HubConnection | null = null;
  private startPromise: Promise<void> | null = null;

  readonly status = this.statusSignal.asReadonly();
  readonly messageReceived$: Observable<MessageEvent> = this.messageReceivedSubject.asObservable();
  readonly messageQueued$: Observable<MessageEvent> = this.messageQueuedSubject.asObservable();
  readonly messageStatusChanged$: Observable<MessageEvent> = this.messageStatusChangedSubject.asObservable();
  readonly conversationRead$: Observable<ConversationReadEvent> = this.conversationReadSubject.asObservable();
  /**
   * Emits whenever the connection comes back after the first attempt (automatic reconnect,
   * restart after `onclose`, or a late first connection). Consumers must refetch their state.
   */
  readonly resync$: Observable<void> = this.resyncSubject.asObservable();

  /**
   * Starts the connection. Resolves after the first attempt settles, successful or not, so the
   * caller can fetch REST state afterwards; failed attempts keep retrying in the background.
   */
  connect(): Promise<void> {
    if (this.startPromise) {
      return this.startPromise;
    }
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/inbox')
      .withAutomaticReconnect(neverEndingRetry)
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('messageReceived', (event: MessageEvent) => this.messageReceivedSubject.next(event));
    connection.on('messageQueued', (event: MessageEvent) => this.messageQueuedSubject.next(event));
    connection.on('messageStatusChanged', (event: MessageEvent) => this.messageStatusChangedSubject.next(event));
    connection.on('conversationRead', (event: ConversationReadEvent) => this.conversationReadSubject.next(event));

    connection.onreconnecting(() => this.statusSignal.set('reconnecting'));
    connection.onreconnected(() => {
      this.statusSignal.set('connected');
      this.resyncSubject.next();
    });
    connection.onclose(() => {
      this.statusSignal.set('reconnecting');
      this.scheduleStart(0, true);
    });

    this.connection = connection;
    this.startPromise = this.tryStart(0, false);
    return this.startPromise;
  }

  /** One start attempt; on failure schedules the next one without blocking the caller. */
  private async tryStart(attempt: number, isRecovery: boolean): Promise<void> {
    const connection = this.connection;
    if (!connection || connection.state !== HubConnectionState.Disconnected) {
      return;
    }
    try {
      await connection.start();
      this.statusSignal.set('connected');
      if (isRecovery) {
        this.resyncSubject.next();
      }
    } catch {
      this.statusSignal.set('reconnecting');
      this.scheduleStart(attempt + 1, true);
    }
  }

  private scheduleStart(attempt: number, isRecovery: boolean): void {
    setTimeout(() => void this.tryStart(attempt, isRecovery), retryDelayMs(attempt));
  }
}
