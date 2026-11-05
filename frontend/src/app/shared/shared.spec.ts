import { retryDelayMs } from '../core/realtime.service';
import { initialsOf } from './avatar';
import { formatMessageTime } from './message-time.pipe';

describe('initialsOf', () => {
  it('uses up to two words and ignores a leading @', () => {
    expect(initialsOf('Ana Pérez')).toBe('AP');
    expect(initialsOf('@juanc')).toBe('J');
    expect(initialsOf('María José López')).toBe('MJ');
    expect(initialsOf('  ')).toBe('?');
  });
});

describe('formatMessageTime', () => {
  const now = new Date(2026, 10, 5, 18, 0);

  it('shows the time for today and the date for older messages', () => {
    const today = new Date(2026, 10, 5, 9, 7).toISOString();
    const older = new Date(2026, 10, 3, 9, 7).toISOString();
    expect(formatMessageTime(today, 'list', now)).toMatch(/09:07/);
    expect(formatMessageTime(older, 'list', now)).toMatch(/03\/11/);
    expect(formatMessageTime(older, 'bubble', now)).toMatch(/03\/11 09:07/);
    expect(formatMessageTime('not a date', 'list', now)).toBe('');
  });
});

describe('retryDelayMs', () => {
  it('follows 0, 2, 5, 10 s and then every 30 s forever', () => {
    expect([0, 1, 2, 3, 4, 50].map(retryDelayMs)).toEqual([0, 2_000, 5_000, 10_000, 30_000, 30_000]);
  });
});
