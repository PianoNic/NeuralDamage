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

/** "30 Sep 2026", for the profile card; empty for an unreadable date. */
export function joinedDate(joinedAt: string): string {
  const date = new Date(joinedAt);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

/** A chat role as the People panel shows it. */
export function roleLabel(role: string): string {
  return role === 'Owner' ? 'Owner' : 'Member';
}

/**
 * Whether the viewer may remove this member, as the API allows it: the chat owner removes anyone but
 * themselves, everyone else only themselves. The owner can never be removed.
 */
export function canRemoveMember(
  member: { role: string; userId: string | null },
  members: readonly { role: string; userId: string | null }[],
  currentUserId: string | null,
): boolean {
  if (!currentUserId || member.role === 'Owner') return false;
  if (member.userId === currentUserId) return true;
  return members.some((m) => m.userId === currentUserId && m.role === 'Owner');
}
