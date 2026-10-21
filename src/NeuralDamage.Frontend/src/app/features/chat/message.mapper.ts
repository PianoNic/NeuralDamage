import { ChatMember, ChatMemberDto, Message, MessageDto } from '../../core/models';

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
    attachments: dto.attachments ?? [],
    sticker: dto.sticker ?? null,
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
    modelId: isBot ? (dto.bot?.modelId ?? null) : null,
    isMuted: dto.isMuted ?? false,
    modelStatus: dto.modelStatus ?? null,
    modelStatusReason: dto.modelStatusReason ?? null,
  };
}

export function toChatMembers(dtos: readonly ChatMemberDto[]): ChatMember[] {
  return dtos.map(toChatMember);
}

/**
 * The messages with one image's description filled in, from `AttachmentDescribed`. The same list
 * comes back when nothing matches, so nothing re-renders for an image this client has not loaded.
 */
export function withDescription(
  messages: Message[],
  messageId: string,
  attachmentId: string,
  description: string,
): Message[] {
  const index = messages.findIndex((m) => m.id === messageId);
  if (index < 0 || !messages[index].attachments.some((a) => a.id === attachmentId)) return messages;
  const message = messages[index];
  const next = [...messages];
  next[index] = {
    ...message,
    attachments: message.attachments.map((a) => (a.id === attachmentId ? { ...a, description } : a)),
  };
  return next;
}

/**
 * Folds messages fetched after a reconnect into the ones shown: a fetched copy replaces the shown one
 * (its reactions and descriptions are newer), the rest are added, and the result is oldest first.
 * Timestamps are compared as dates, since the API does not pad fractional seconds to one width.
 */
export function mergeMessages(current: Message[], fetched: readonly Message[]): Message[] {
  if (fetched.length === 0) return current;
  const byId = new Map(current.map((m) => [m.id, m]));
  for (const message of fetched) byId.set(message.id, message);
  // The sort is stable, so messages sharing a timestamp keep their order.
  return [...byId.values()].sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt));
}
