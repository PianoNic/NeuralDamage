/**
 * The app is in English, so every date it shows is too, whatever the browser's language. Left to
 * the browser's locale, "Yesterday" ended up next to "Samstag, 26. September".
 */
export const APP_LOCALE = 'en-GB';

const DAY_MS = 86_400_000;

/** Whole calendar days from `date` to `now` in local time: 0 today, 1 yesterday. */
export function daysAgo(date: Date, now: Date): number {
  const startOf = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  return Math.round((startOf(now) - startOf(date)) / DAY_MS);
}

/** "14:05". */
export function formatTime(date: Date): string {
  return date.toLocaleTimeString(APP_LOCALE, { hour: '2-digit', minute: '2-digit' });
}

/** "Saturday". */
export function formatWeekday(date: Date): string {
  return date.toLocaleDateString(APP_LOCALE, { weekday: 'long' });
}

/** "3 Sept", with the year when it is not this one: "3 Sept 2025". */
export function formatShortDate(date: Date, now: Date): string {
  return date.toLocaleDateString(APP_LOCALE, {
    day: 'numeric',
    month: 'short',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric',
  });
}

/** "30 Sept 2026". */
export function formatFullDate(date: Date): string {
  return date.toLocaleDateString(APP_LOCALE, { day: 'numeric', month: 'short', year: 'numeric' });
}

/** "Today", "Yesterday", then "Saturday 26 September" (with the year when it is not this one). */
export function formatDayLabel(date: Date, now: Date): string {
  const days = daysAgo(date, now);
  if (days === 0) return 'Today';
  if (days === 1) return 'Yesterday';
  return date.toLocaleDateString(APP_LOCALE, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric',
  });
}
