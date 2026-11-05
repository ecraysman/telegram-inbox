import { Pipe, PipeTransform } from '@angular/core';

const pad = (value: number): string => value.toString().padStart(2, '0');
const formatTime = (date: Date): string => `${pad(date.getHours())}:${pad(date.getMinutes())}`;
const formatDay = (date: Date): string => `${pad(date.getDate())}/${pad(date.getMonth() + 1)}`;

/** `list`: "14:03" today, "05/11" before. `bubble`: "14:03" today, "05/11 14:03" before. Browser time zone. */
export type MessageTimeMode = 'list' | 'bubble';

export function formatMessageTime(iso: string, mode: MessageTimeMode, now: Date = new Date()): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  const sameDay =
    date.getFullYear() === now.getFullYear() && date.getMonth() === now.getMonth() && date.getDate() === now.getDate();
  if (sameDay) {
    return formatTime(date);
  }
  return mode === 'list' ? formatDay(date) : `${formatDay(date)} ${formatTime(date)}`;
}

@Pipe({ name: 'messageTime' })
export class MessageTimePipe implements PipeTransform {
  transform(iso: string | null | undefined, mode: MessageTimeMode = 'list'): string {
    return iso ? formatMessageTime(iso, mode) : '';
  }
}
