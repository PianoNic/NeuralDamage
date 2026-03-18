import { create } from "zustand"

interface UIState {
  sidebarOpen: boolean
  botManagerOpen: boolean
  typingBots: Map<string, string>  // bot_id → bot_name
  typingUsers: Set<string>
  toggleSidebar: () => void
  setBotManagerOpen: (open: boolean) => void
  addTypingBot: (botId: string, botName: string) => void
  removeTypingBot: (botId: string) => void
  addTypingUser: (userId: string) => void
  removeTypingUser: (userId: string) => void
}

export const useUIStore = create<UIState>((set) => ({
  sidebarOpen: typeof window !== "undefined" ? window.innerWidth >= 768 : true,
  botManagerOpen: false,
  typingBots: new Map(),
  typingUsers: new Set(),

  toggleSidebar: () => set((s) => ({ sidebarOpen: !s.sidebarOpen })),
  setBotManagerOpen: (open) => set({ botManagerOpen: open }),

  addTypingBot: (botId, botName) => set((s) => {
    const next = new Map(s.typingBots)
    next.set(botId, botName)
    return { typingBots: next }
  }),
  removeTypingBot: (botId) => set((s) => {
    const next = new Map(s.typingBots)
    next.delete(botId)
    return { typingBots: next }
  }),

  addTypingUser: (userId) => set((s) => {
    const next = new Set(s.typingUsers)
    next.add(userId)
    return { typingUsers: next }
  }),
  removeTypingUser: (userId) => set((s) => {
    const next = new Set(s.typingUsers)
    next.delete(userId)
    return { typingUsers: next }
  }),
}))
