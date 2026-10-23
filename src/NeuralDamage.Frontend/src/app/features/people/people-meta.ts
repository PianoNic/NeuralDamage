import { daysAgo, formatFullDate, formatShortDate, formatWeekday } from '../../shared/dates';

/** "Joined today", "Joined Tuesday" within the week, then "Joined 3 Sept" (with the year when it differs). */
export function joinedLabel(joinedAt: string, now: Date = new Date()): string {
  const date = new Date(joinedAt);
  if (Number.isNaN(date.getTime())) return '';
  const days = daysAgo(date, now);
  if (days <= 0) return 'Joined today';
  if (days === 1) return 'Joined yesterday';
  if (days < 7) return `Joined ${formatWeekday(date)}`;
  return `Joined ${formatShortDate(date, now)}`;
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

/** "30 Sept 2026", for the profile card; empty for an unreadable date. */
export function joinedDate(joinedAt: string): string {
  const date = new Date(joinedAt);
  if (Number.isNaN(date.getTime())) return '';
  return formatFullDate(date);
}

/** A chat role as the People panel shows it. */
export function roleLabel(role: string): string {
  return role === 'Owner' ? 'Owner' : 'Member';
}

/**
 * What to ask before removing a member, or null when removing needs no confirmation. Only a private bot
 * asks: it belongs to this chat alone, so the server deletes it once it is removed.
 */
export function removalConfirmation(
  member: { displayName: string },
  bot: { isPublic: boolean } | null | undefined,
): { title: string; description: string; action: string } | null {
  if (!bot || bot.isPublic) return null;
  const name = member.displayName;
  return {
    title: `Remove ${name}?`,
    description: `${name} is private to this chat, so it will be deleted.`,
    action: 'Remove and delete',
  };
}

/**
 * Whether the viewer may remove this member, as the API allows it: the chat owner removes anyone but
 * themselves, everyone else only themselves; an admin anyone. The owner can never be removed.
 */
export function canRemoveMember(
  member: { role: string; userId: string | null },
  members: readonly { role: string; userId: string | null }[],
  currentUserId: string | null,
  isAdmin = false,
): boolean {
  if (!currentUserId || member.role === 'Owner') return false;
  if (member.userId === currentUserId || isAdmin) return true;
  return members.some((m) => m.userId === currentUserId && m.role === 'Owner');
}

/** The line under a member's name: a bot's short description, "You", or when a person joined. */
export function memberSubtitle(
  member: { memberType: string; userId: string | null; joinedAt: string },
  bot: { personality?: string | null } | null | undefined,
  currentUserId: string | null,
): string {
  if (member.memberType === 'bot') return bot?.personality?.split('\n')[0] || 'Bot';
  if (member.userId && member.userId === currentUserId) return 'You';
  return joinedLabel(member.joinedAt);
}
