import { ChatMember, ChatMemberDto, Message, MessageDto } from '@app/models';

/**
 * The API returns a `MessageDto` — sender identity lives in nested
 * `senderUser` / `senderBot` objects and there is no `senderType`. The view
 * model this app renders is flat. Callers used to cast the DTO straight to
 * `Message`, which type-checked against a generated `any` but left
 * `senderName`, `senderAvatar`, `senderType`, `reactions` and `replyTo`
 * undefined at runtime.
 *
 * Both sides are now generated or derived from the API's OpenAPI document, so
 * a field that moves on the server fails this file at compile time.
 */
export function toMessage(dto: MessageDto): Message {
  // A bot message carries senderBotId; a person's carries senderUserId.
  const isBot = dto.senderBotId != null;

  return {
    id: dto.id,
    chatId: dto.chatId,
    senderUserId: dto.senderUserId ?? null,
    senderBotId: dto.senderBotId ?? null,
    senderName: isBot
      ? (dto.senderBot?.name ?? 'Bot')
      : (dto.senderUser?.displayName ?? dto.senderUser?.email ?? 'Unknown'),
    senderAvatar: (isBot ? dto.senderBot?.avatarUrl : dto.senderUser?.avatarUrl) ?? null,
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

/** Same story for members: identity is nested under `user` / `bot`. */
export function toChatMember(dto: ChatMemberDto): ChatMember {
  const isBot = dto.botId != null;

  return {
    id: dto.id,
    chatId: dto.chatId,
    userId: dto.userId ?? null,
    botId: dto.botId ?? null,
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
