/** "Joined today", "Joined Tuesday" within the week, then "Joined 3 Sep" (with the year when it differs). */
export function joinedLabel(joinedAt: string, now: Date = new Date()): string {
  const date = new Date(joinedAt);
  if (Number.isNaN(date.getTime())) return '';
  const startOf = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((startOf(now) - startOf(date)) / 86_400_000);
  if (days <= 0) return 'Joined today';
  if (days === 1) return 'Joined yesterday';
  if (days < 7) return `Joined ${date.toLocaleDateString(undefined, { weekday: 'long' })}`;
  return `Joined ${date.toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric',
  })}`;
}

/** "Public, made by You" / "Public, made by alice" / "Private to this chat". */
export function visibilityLabel(
  bot: { isPublic: boolean; createdById: string; createdBy: { displayName: string } },
  currentUserId: string | null,
): string {
  if (!bot.isPublic) return 'Private to this chat';
  const by = bot.createdById === currentUserId ? 'you' : bot.createdBy.displayName || 'someone';
  return `Public, made by ${by}`;
}
