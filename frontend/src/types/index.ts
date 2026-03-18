export interface User {
  id: string
  email: string
  display_name: string
  avatar_url: string | null
  created_at: string
}

export interface Bot {
  id: string
  name: string
  avatar_url: string | null
  model_id: string
  system_prompt: string
  personality: string | null
  temperature: number
  created_by: string
  created_at: string
  is_active: boolean
}

export interface ChatMember {
  id: string
  chat_id: string
  user_id: string | null
  bot_id: string | null
  role: string
  joined_at: string
  display_name: string | null
  avatar_url: string | null
  member_type: "user" | "bot"
}

export interface Chat {
  id: string
  name: string
  created_by: string
  created_at: string
  members: ChatMember[]
}

export interface ReactionGroup {
  emoji: string
  count: number
  user_ids: string[]
  bot_ids: string[]
  names: string[]
}

export interface ReplyInfo {
  id: string
  sender_name: string
  sender_type: "user" | "bot"
  content: string
}

export interface Message {
  id: string
  chat_id: string
  sender_user_id: string | null
  sender_bot_id: string | null
  sender_name: string
  sender_avatar: string | null
  sender_type: "user" | "bot"
  content: string
  mentions: string[]
  reactions: ReactionGroup[]
  reply_to: ReplyInfo | null
  created_at: string
}

export interface OpenRouterModel {
  id: string
  name: string
  context_length: number | null
  pricing: Record<string, string> | null
}
