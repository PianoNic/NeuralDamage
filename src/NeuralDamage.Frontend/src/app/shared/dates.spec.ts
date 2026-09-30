import { formatDayLabel, formatFullDate, formatShortDate, formatTime, formatWeekday } from './dates';

describe('dates', () => {
  const now = new Date(2026, 8, 30, 12); // Wednesday 30 September 2026

  it('labels days in English whatever the browser language', () => {
    expect(formatDayLabel(new Date(2026, 8, 30, 8), now)).toBe('Today');
    expect(formatDayLabel(new Date(2026, 8, 29, 23), now)).toBe('Yesterday');
    expect(formatDayLabel(new Date(2026, 8, 26, 10), now)).toBe('Saturday 26 September');
    expect(formatDayLabel(new Date(2025, 11, 31, 10), now)).toBe('Wednesday, 31 December 2025');
  });

  it('formats times, weekdays and dates the same way everywhere', () => {
    expect(formatTime(new Date(2026, 8, 30, 14, 5))).toBe('14:05');
    expect(formatWeekday(new Date(2026, 8, 26))).toBe('Saturday');
    expect(formatShortDate(new Date(2026, 8, 3), now)).toBe('3 Sept');
    expect(formatShortDate(new Date(2025, 8, 3), now)).toBe('3 Sept 2025');
    expect(formatFullDate(new Date(2026, 8, 30))).toBe('30 Sept 2026');
  });
});
