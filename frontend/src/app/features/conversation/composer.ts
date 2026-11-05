import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MAX_REPLY_LENGTH } from '../../core/models';

/** Reply box. Enter sends, Shift+Enter inserts a new line. The draft lives in the store. */
@Component({
  selector: 'app-composer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './composer.html',
  styleUrl: './composer.css',
})
export class Composer {
  readonly draft = input.required<string>();
  readonly sending = input(false);
  readonly error = input<string | null>(null);
  readonly draftChange = output<string>();
  readonly send = output<void>();

  protected readonly maxLength = MAX_REPLY_LENGTH;
  protected readonly length = computed(() => this.draft().length);
  protected readonly tooLong = computed(() => this.length() > MAX_REPLY_LENGTH);
  protected readonly showCounter = computed(() => this.length() > MAX_REPLY_LENGTH - 200);
  protected readonly canSend = computed(
    () => !this.sending() && !this.tooLong() && this.draft().trim().length > 0,
  );

  protected onInput(event: Event): void {
    this.draftChange.emit((event.target as HTMLTextAreaElement).value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  protected submit(): void {
    if (this.canSend()) {
      this.send.emit();
    }
  }
}
