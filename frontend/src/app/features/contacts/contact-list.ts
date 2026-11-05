import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ConnectionStatus, ContactSummaryDto } from '../../core/models';
import { Avatar } from '../../shared/avatar';
import { ConnectionStatusIndicator } from '../../shared/connection-status';
import { MessageTimePipe } from '../../shared/message-time.pipe';

/** Sidebar: brand header with the connection indicator and the contacts sorted by last activity. */
@Component({
  selector: 'app-contact-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, ConnectionStatusIndicator, MessageTimePipe],
  templateUrl: './contact-list.html',
  styleUrl: './contact-list.css',
})
export class ContactList {
  readonly contacts = input.required<readonly ContactSummaryDto[]>();
  readonly selectedId = input<string | null>(null);
  readonly loaded = input(false);
  readonly error = input<string | null>(null);
  readonly status = input.required<ConnectionStatus>();
  readonly selectContact = output<string>();
}
