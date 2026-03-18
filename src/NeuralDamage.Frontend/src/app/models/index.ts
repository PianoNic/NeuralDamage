export interface User {
  id: string;
  email: string;
  displayName: string;
  avatarUrl: string | null;
  createdAt: string;
}

export interface Bot {
  id: string;
  name: string;
  avatarUrl: string | null;
  modelId: string;
  systemPrompt: string;
  personality: string | null;
  temperature: number;
  createdBy: string;
  createdAt: string;
  isActive: boolean;
}

export interface ChatMember {
  id: string;
  chatId: string;
  userId: string | null;
  botId: string | null;
  role: string;
  joinedAt: string;
  displayName: string | null;
  avatarUrl: string | null;
  memberType: 'user' | 'bot';
}

export interface Chat {
  id: string;
  name: string;
  createdBy: string;
  createdAt: string;
  members: ChatMember[];
}

export interface ReactionGroup {
  emoji: string;
  count: number;
  userIds: string[];
  botIds: string[];
  names: string[];
}

export interface ReplyInfo {
  id: string;
  senderName: string;
  senderType: 'user' | 'bot';
  content: string;
}

export interface Message {
  id: string;
  chatId: string;
  senderUserId: string | null;
  senderBotId: string | null;
  senderName: string;
  senderAvatar: string | null;
  senderType: 'user' | 'bot';
  content: string;
  mentions: string[];
  reactions: ReactionGroup[];
  replyTo: ReplyInfo | null;
  createdAt: string;
}

export interface OpenRouterModel {
  id: string;
  name: string;
  contextLength: number | null;
  pricing: Record<string, string> | null;
}
