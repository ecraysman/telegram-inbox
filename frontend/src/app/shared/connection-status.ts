import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ConnectionStatus } from '../core/models';

const LABELS: Record<ConnectionStatus, string> = {
  connected: 'En línea',
  connecting: 'Conectando…',
  reconnecting: 'Reconectando…',
};

@Component({
  selector: 'app-connection-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="status" [class.online]="status() === 'connected'" role="status">
      <span class="dot" aria-hidden="true"></span>{{ label() }}
    </span>
  `,
  styles: `
    .status {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      font-size: 0.75rem;
      color: var(--color-brand-10);
    }
    .dot {
      width: 0.5rem;
      height: 0.5rem;
      border-radius: var(--radius-full);
      background: var(--color-neutral-40);
    }
    .online .dot {
      background: var(--color-accent);
    }
  `,
})
export class ConnectionStatusIndicator {
  readonly status = input.required<ConnectionStatus>();
  protected readonly label = computed(() => LABELS[this.status()]);
}
