import { create } from "zustand"
import type { Chat, Message, ChatMember, ReactionGroup } from "@/types"

interface ChatState {
  chats: Chat[]
  activeChat: Chat | null
  messages: Message[]
  setChats: (chats: Chat[]) => void
  setActiveChat: (chat: Chat | null) => void
  setMessages: (messages: Message[]) => void
  addMessage: (message: Message) => void
  prependMessages: (messages: Message[]) => void
  addChat: (chat: Chat) => void
  removeChat: (chatId: string) => void
  addMember: (chatId: string, member: ChatMember) => void
  removeMember: (chatId: string, memberId: string) => void
  updateReactions: (messageId: string, reactions: ReactionGroup[]) => void
}

export const useChatStore = create<ChatState>((set) => ({
  chats: [],
  activeChat: null,
  messages: [],

  setChats: (chats) => set({ chats }),
  setActiveChat: (chat) => set({ activeChat: chat }),
  setMessages: (messages) => set({ messages }),
  addMessage: (message) => set((s) => ({ messages: [...s.messages, message] })),
  prependMessages: (messages) => set((s) => ({ messages: [...messages, ...s.messages] })),

  addChat: (chat) => set((s) => ({ chats: [...s.chats, chat] })),
  removeChat: (chatId) => set((s) => ({
    chats: s.chats.filter((c) => c.id !== chatId),
    activeChat: s.activeChat?.id === chatId ? null : s.activeChat,
  })),

  addMember: (chatId, member) => set((s) => {
    // Idempotent — skip if member already exists
    const exists = (members: ChatMember[]) => members.some((m) => m.id === member.id)
    return {
      chats: s.chats.map((c) =>
        c.id === chatId && !exists(c.members) ? { ...c, members: [...c.members, member] } : c
      ),
      activeChat: s.activeChat?.id === chatId && !exists(s.activeChat.members)
        ? { ...s.activeChat, members: [...s.activeChat.members, member] }
        : s.activeChat,
    }
  }),

  removeMember: (chatId, memberId) => set((s) => ({
    chats: s.chats.map((c) =>
      c.id === chatId ? { ...c, members: c.members.filter((m) => m.id !== memberId) } : c
    ),
    activeChat: s.activeChat?.id === chatId
      ? { ...s.activeChat, members: s.activeChat.members.filter((m) => m.id !== memberId) }
      : s.activeChat,
  })),

  updateReactions: (messageId, reactions) => set((s) => ({
    messages: s.messages.map((m) =>
      m.id === messageId ? { ...m, reactions } : m
    ),
  })),
}))
