import { BotDto, ChatMember, CommentDto, PostDto } from '../../core/models';
import { nicknames } from '../chat/mentions';

/**
 * Who can be @mentioned on the feed, in the composer's terms: the public bots that are on the
 * feed, and the people in the thread at hand.
 */
export function feedMembers(bots: readonly BotDto[], posts: readonly PostDto[] = [], comments: readonly CommentDto[] = []): ChatMember[] {
  const members: ChatMember[] = bots
    .filter((b) => b.isActive && b.feedEnabled)
    .map((b) => ({
      id: b.id,
      chatId: '',
      userId: null,
      botId: b.id,
      role: 'Member',
      joinedAt: b.createdAt,
      displayName: b.name,
      avatarUrl: b.avatarUrl ?? null,
      memberType: 'bot' as const,
      modelId: b.modelId,
      isMuted: false,
      modelStatus: null,
      modelStatusReason: null,
    }));
  const people = new Map<string, { id: string; name: string; avatarUrl: string | null }>();
  for (const author of [...posts.map((p) => p.author), ...comments.map((c) => c.author)])
    if (!author.isBot) people.set(author.id, { id: author.id, name: author.name, avatarUrl: author.avatarUrl ?? null });
  for (const person of people.values())
    members.push({
      id: person.id,
      chatId: '',
      userId: person.id,
      botId: null,
      role: 'Member',
      joinedAt: '',
      displayName: person.name,
      avatarUrl: person.avatarUrl,
      memberType: 'user',
      modelId: null,
      isMuted: false,
      modelStatus: null,
      modelStatusReason: null,
    });
  return members;
}

/** Every name a mention may use: display names, and the bots' nicknames. */
export function mentionNames(members: readonly ChatMember[], bots: readonly BotDto[]): string[] {
  return [...members.map((m) => m.displayName), ...bots.flatMap((b) => nicknames(b.aliases))];
}
