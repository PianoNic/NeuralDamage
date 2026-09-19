/**
 * Wire shapes are generated from the API's OpenAPI document into
 * `src/app/api/model` (`bun run apigen`). They are re-exported here so call
 * sites have one import for both those and the view models below, and so
 * nothing hand-writes a second description of a payload — the versions that
 * used to live in this file had drifted from the API and the mismatch was
 * invisible, because the generated services returned `any` and every call
 * site cast the result.
 *
 * Do not add an interface here that describes something the API returns.
 * Give the endpoint a response type and regenerate instead.
 */
export type {
  BotDto,
  BotSummaryDto,
  ChatDetailDto,
  ChatDto,
  ChatMemberDto,
  MessageDto,
  OpenRouterModel,
  ReactionGroupDto,
  ReplyInfoDto,
  UserDto,
} from '@app/api';

import type { ReactionGroupDto, ReplyInfoDto } from '@app/api';

/**
 * A message as the templates render it. The wire nests sender identity under
 * `senderUser` / `senderBot` and has no notion of a sender "type"; this is
 * flat, always populated, and built by `toMessage` in `chat/message.mapper`.
 */
export interface Message {
  id: string;
  chatId: string;
  senderUserId: string | null;
  senderBotId: string | null;
  senderName: string;
  senderAvatar: string | null;
  /** Drives the model vendor's brand icon when a bot has no avatar of its own. */
  senderModelId: string | null;
  senderType: 'user' | 'bot';
  content: string;
  mentions: string[];
  reactions: ReactionGroupDto[];
  replyTo: ReplyInfoDto | null;
  createdAt: string;
}

/**
 * A chat member as the templates render it — same story as {@link Message}:
 * the wire nests identity under `user` / `bot` and has no `memberType`.
 */
export interface ChatMember {
  id: string;
  chatId: string;
  userId: string | null;
  botId: string | null;
  role: string;
  joinedAt: string;
  displayName: string;
  avatarUrl: string | null;
  memberType: 'user' | 'bot';
}
