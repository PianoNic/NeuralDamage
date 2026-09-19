import { ChatMember, Message, ReactionGroup, ReplyInfo } from '@app/models';

/**
 * The API returns a MessageDto — sender identity lives in nested `senderUser` /
 * `senderBot` objects, and there is no `senderType`. The view model this app
 * renders is flat. Callers used to cast the DTO straight to `Message`, which
 * type-checked but left `senderName`, `senderAvatar`, `senderType`, `reactions`
 * and `replyTo` undefined at runtime.
 */
interface MessageDto {
  id: string;
  chatId: string;
  senderUserId: string | null;
  senderBotId: string | null;
  content: string;
  mentions: string[] | null;
  replyToId: string | null;
  createdAt: string;
  senderUser?: { id: string; email?: string; displayName?: string | null; avatarUrl?: string | null } | null;
  senderBot?: { id: string; name?: string | null; avatarUrl?: string | null; modelId?: string | null } | null;
  reactions?: ReactionGroup[] | null;
  replyTo?: ReplyInfo | null;
}

export function toMessage(dto: MessageDto): Message {
  const isBot = dto.senderBotId !== null;

  return {
    id: dto.id,
    chatId: dto.chatId,
    senderUserId: dto.senderUserId,
    senderBotId: dto.senderBotId,
    senderName: isBot
      ? (dto.senderBot?.name ?? 'Bot')
      : (dto.senderUser?.displayName ?? dto.senderUser?.email ?? 'Unknown'),
    senderAvatar: (isBot ? dto.senderBot?.avatarUrl : dto.senderUser?.avatarUrl) ?? null,
    // Falls back to the model vendor's brand icon when a bot has no avatar.
    senderModelId: isBot ? (dto.senderBot?.modelId ?? null) : null,
    senderType: isBot ? 'bot' : 'user',
    content: dto.content,
    mentions: dto.mentions ?? [],
    reactions: dto.reactions ?? [],
    replyTo: dto.replyTo ?? null,
    createdAt: dto.createdAt,
  };
}

export function toMessages(dtos: readonly MessageDto[]): Message[] {
  return dtos.map(toMessage);
}

/**
 * Same story for members: the API nests identity under `user` / `bot` and has
 * no `memberType`, so the flat view model has to be derived.
 */
interface ChatMemberDto {
  id: string;
  chatId: string;
  userId: string | null;
  botId: string | null;
  role: string;
  joinedAt: string;
  user?: { id: string; email?: string; displayName?: string | null; avatarUrl?: string | null } | null;
  bot?: { id: string; name?: string | null; avatarUrl?: string | null } | null;
}

export function toChatMember(dto: ChatMemberDto): ChatMember {
  const isBot = dto.botId !== null;

  return {
    id: dto.id,
    chatId: dto.chatId,
    userId: dto.userId,
    botId: dto.botId,
    role: dto.role,
    joinedAt: dto.joinedAt,
    displayName: isBot
      ? (dto.bot?.name ?? 'Bot')
      : (dto.user?.displayName ?? dto.user?.email ?? 'Unknown'),
    avatarUrl: (isBot ? dto.bot?.avatarUrl : dto.user?.avatarUrl) ?? null,
    memberType: isBot ? 'bot' : 'user',
  };
}

export function toChatMembers(dtos: readonly ChatMemberDto[]): ChatMember[] {
  return dtos.map(toChatMember);
}
