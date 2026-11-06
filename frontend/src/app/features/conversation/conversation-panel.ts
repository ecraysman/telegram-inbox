import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  input,
  output,
  viewChild,
} from '@angular/core';
import { ContactSummaryDto, MessageDto } from '../../core/models';
import { Avatar } from '../../shared/avatar';
import { Composer } from './composer';
import { MessageBubble } from './message-bubble';

/** Distance from the bottom (px) within which the history counts as "at the bottom". */
export const STICK_THRESHOLD_PX = 80;

type ScrollMetrics = Pick<HTMLElement, 'scrollTop' | 'scrollHeight' | 'clientHeight'>;

export function isNearBottom(element: ScrollMetrics, threshold = STICK_THRESHOLD_PX): boolean {
  return element.scrollHeight - element.scrollTop - element.clientHeight <= threshold;
}

/** Center panel: header of the selected contact, its history and the reply box. */
@Component({
  selector: 'app-conversation-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, Composer, MessageBubble],
  templateUrl: './conversation-panel.html',
  styleUrl: './conversation-panel.css',
})
export class ConversationPanel {
  readonly contact = input<ContactSummaryDto | null>(null);
  readonly messages = input.required<readonly MessageDto[]>();
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly draft = input.required<string>();
  readonly sending = input(false);
  readonly sendError = input<string | null>(null);
  readonly retrying = input<ReadonlySet<string>>(new Set());
  readonly draftChange = output<string>();
  readonly send = output<void>();
  readonly retry = output<string>();

  private readonly history = viewChild<ElementRef<HTMLElement>>('history');
  /** Whether the next history change should scroll to the newest message. */
  private stickToBottom = true;
  private shownContactId: string | null = null;

  constructor() {
    // Follow new messages only when the conversation was just opened, the operator was already
    // near the bottom, or the operator just sent a reply; never pull them away from older history.
    afterRenderEffect(() => {
      const contactId = this.contact()?.id ?? null;
      this.messages();
      const element = this.history()?.nativeElement;
      if (!element) {
        return;
      }
      if (contactId !== this.shownContactId) {
        this.shownContactId = contactId;
        this.stickToBottom = true;
      }
      if (this.stickToBottom) {
        element.scrollTop = element.scrollHeight;
      }
    });
  }

  protected onHistoryScroll(element: HTMLElement): void {
    this.stickToBottom = isNearBottom(element);
  }

  protected onSend(): void {
    this.stickToBottom = true;
    this.send.emit();
  }
}
