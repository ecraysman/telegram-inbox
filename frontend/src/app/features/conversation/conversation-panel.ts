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

  constructor() {
    // Keep the newest message in view whenever the history changes.
    afterRenderEffect(() => {
      this.messages();
      const element = this.history()?.nativeElement;
      if (element) {
        element.scrollTop = element.scrollHeight;
      }
    });
  }
}
