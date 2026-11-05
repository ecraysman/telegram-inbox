import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Initials of a display name ("Ana Pérez" → "AP", "@juanc" → "J"). */
export function initialsOf(name: string): string {
  const words = name.replace(/^@/, '').trim().split(/\s+/).filter((w) => w.length > 0);
  const letters = words.slice(0, 2).map((w) => [...w][0] ?? '');
  return letters.join('').toUpperCase() || '?';
}

@Component({
  selector: 'app-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="avatar" aria-hidden="true">{{ initials() }}</span>`,
  styles: `
    .avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2.5rem;
      height: 2.5rem;
      border-radius: var(--radius-full);
      background: var(--color-brand-10);
      color: var(--color-brand-deepest);
      font-weight: 700;
      font-size: 0.875rem;
      flex-shrink: 0;
    }
  `,
})
export class Avatar {
  readonly name = input.required<string>();
  protected readonly initials = computed(() => initialsOf(this.name()));
}
