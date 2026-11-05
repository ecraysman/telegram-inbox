import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { ContactList } from './features/contacts/contact-list';
import { ConversationPanel } from './features/conversation/conversation-panel';
import { InboxStore } from './state/inbox.store';

/** Shell and the only container component: wires the store to the presentational panels. */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ContactList, ConversationPanel],
  template: `
    <aside class="sidebar">
      <app-contact-list
        [contacts]="store.contacts()"
        [selectedId]="store.selectedContactId()"
        [loaded]="store.contactsLoaded()"
        [error]="store.contactsError()"
        [status]="store.connectionStatus()"
        (selectContact)="store.select($event)"
      />
    </aside>
    <main class="conversation">
      <app-conversation-panel
        [contact]="store.selectedContact()"
        [messages]="store.messages()"
        [loading]="store.conversationLoading()"
        [error]="store.conversationError()"
        [draft]="store.draft()"
        [sending]="store.sending()"
        [sendError]="store.sendError()"
        [retrying]="store.retrying()"
        (draftChange)="store.setDraft($event)"
        (send)="store.send()"
        (retry)="store.retry($event)"
      />
    </main>
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: 320px 1fr;
      height: 100vh;
    }
    .sidebar {
      min-height: 0;
      border-right: 1px solid var(--color-neutral-20);
      background: var(--color-white);
    }
    .conversation {
      min-height: 0;
      background: var(--color-neutral-5);
    }
  `,
})
export class App implements OnInit {
  protected readonly store = inject(InboxStore);

  ngOnInit(): void {
    void this.store.init();
  }
}
