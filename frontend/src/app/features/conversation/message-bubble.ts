import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MessageDto } from '../../core/models';
import { MessageTimePipe } from '../../shared/message-time.pipe';

/** One message. Outbound messages show their delivery state; failed ones offer "Reintentar". */
@Component({
  selector: 'app-message-bubble',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MessageTimePipe],
  templateUrl: './message-bubble.html',
  styleUrl: './message-bubble.css',
  host: {
    '[class.outbound]': "message().direction === 'outbound'",
  },
})
export class MessageBubble {
  readonly message = input.required<MessageDto>();
  readonly retrying = input(false);
  readonly retry = output<string>();
}
