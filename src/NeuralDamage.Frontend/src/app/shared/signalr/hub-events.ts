import { ChatDetailDto, ChatDto, ChatMemberDto, MessageDto, ReactionGroupDto } from '@app/models';

/**
 * The hub payloads, which OpenAPI does not describe — it only covers HTTP, so
 * these are the one part of the wire the generator cannot reach. The DTOs
 * themselves are still generated; only the event names and argument order are
 * written out here.
 *
 * Each entry mirrors a method on the server's `IChatClient` / `IUserClient`,
 * so those interfaces are the things to check against when an event changes.
 * `MemberAdded` arriving as a `ChatMemberDto` and being read as though it were
 * flat is how this went wrong before.
 */
export interface ChatHubEvents {
  ChatUpdated: [chat: ChatDto];
  ChatDeleted: [chatId: string];
  ChatCleared: [chatId: string];
  MemberAdded: [member: ChatMemberDto];
  MemberRemoved: [chatId: string, memberId: string];
  MessageNew: [message: MessageDto];
  ReactionUpdated: [messageId: string, reactions: ReactionGroupDto[]];
  BotTyping: [chatId: string, botId: string, botName: string];
  BotResponseCancelled: [chatId: string];
}

/** Mirrors `IUserClient` on the server. */
export interface UserHubEvents {
  ChatCreated: [chat: ChatDto];
  ChatJoined: [chat: ChatDetailDto];
  ChatLeft: [chatId: string];
}
