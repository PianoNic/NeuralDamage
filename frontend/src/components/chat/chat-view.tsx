import { useCallback, useEffect, useRef, useState } from "react"
import { useParams } from "react-router-dom"
import { Bot, Users } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { MessageList } from "./message-list"
import { MessageInput } from "./message-input"
import { BotManager } from "@/components/bots/bot-manager"
import { useChatStore } from "@/stores/chat-store"
import { useUIStore } from "@/stores/ui-store"
import { useWebSocket } from "@/hooks/use-websocket"
import { apiFetch } from "@/lib/api"
import type { Chat, Message } from "@/types"

const PAGE_SIZE = 50

export function ChatView() {
  const { chatId } = useParams<{ chatId: string }>()
  const { activeChat, setActiveChat, messages, setMessages, prependMessages } = useChatStore()
  const { setBotManagerOpen } = useUIStore()
  const { sendMessage, sendTyping, sendReaction } = useWebSocket(chatId ?? null)
  const [replyingTo, setReplyingTo] = useState<Message | null>(null)
  const [isLoadingMore, setIsLoadingMore] = useState(false)
  const [hasMore, setHasMore] = useState(true)
  const loadingRef = useRef(false)

  // Clear reply when switching chats
  useEffect(() => {
    setReplyingTo(null)
    setHasMore(true)
  }, [chatId])

  useEffect(() => {
    if (!chatId) return

    const load = async () => {
      const [chat, msgs] = await Promise.all([
        apiFetch<Chat>(`/api/chats/${chatId}`),
        apiFetch<Message[]>(`/api/chats/${chatId}/messages?limit=${PAGE_SIZE}`),
      ])
      setActiveChat(chat)
      setMessages(msgs)
      setHasMore(msgs.length >= PAGE_SIZE)
    }
    load().catch(console.error)
  }, [chatId, setActiveChat, setMessages])

  const loadMore = useCallback(async () => {
    if (!chatId || loadingRef.current || !hasMore || messages.length === 0) return
    loadingRef.current = true
    setIsLoadingMore(true)
    try {
      const oldest = messages[0]
      const older = await apiFetch<Message[]>(
        `/api/chats/${chatId}/messages?limit=${PAGE_SIZE}&before=${oldest.id}`
      )
      if (older.length < PAGE_SIZE) setHasMore(false)
      if (older.length > 0) prependMessages(older)
    } catch (e) {
      console.error("Failed to load older messages", e)
    } finally {
      setIsLoadingMore(false)
      loadingRef.current = false
    }
  }, [chatId, hasMore, messages, prependMessages])

  const handleSend = (content: string, mentions: string[], replyToId?: string) => {
    sendMessage(content, mentions, replyToId)
    setReplyingTo(null)
  }

  if (!activeChat) {
    return (
      <div className="flex-1 flex items-center justify-center text-muted-foreground">
        Select a chat to get started
      </div>
    )
  }

  const botMembers = activeChat.members.filter((m) => m.member_type === "bot")
  const userMembers = activeChat.members.filter((m) => m.member_type === "user")

  return (
    <div className="flex flex-col h-full min-h-0">
      {/* Header */}
      <div className="border-b px-4 py-2.5 flex items-center gap-3 shrink-0">
        <div className="flex-1 min-w-0">
          <h2 className="font-semibold text-sm sm:text-base truncate">{activeChat.name}</h2>
          <div className="flex items-center gap-1 text-[11px] text-muted-foreground">
            <Users className="h-3 w-3" />
            <span>{userMembers.length}</span>
            {botMembers.length > 0 && (
              <>
                <span className="mx-0.5">·</span>
                <Bot className="h-3 w-3" />
                <span>{botMembers.length}</span>
              </>
            )}
          </div>
        </div>

        {/* Bot avatars — hidden on very small screens */}
        <div className="hidden sm:flex items-center -space-x-2">
          {botMembers.slice(0, 3).map((m) => (
            <Avatar key={m.id} className="h-6 w-6 border-2 border-background">
              <AvatarImage src={m.avatar_url ?? undefined} />
              <AvatarFallback className="text-[9px] bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300">
                {m.display_name?.[0]?.toUpperCase()}
              </AvatarFallback>
            </Avatar>
          ))}
          {botMembers.length > 3 && (
            <Avatar className="h-6 w-6 border-2 border-background">
              <AvatarFallback className="text-[9px]">+{botMembers.length - 3}</AvatarFallback>
            </Avatar>
          )}
        </div>

        <Button variant="outline" size="sm" className="h-7 text-xs" onClick={() => setBotManagerOpen(true)}>
          <Bot className="h-3.5 w-3.5 mr-1" /> Bots
        </Button>
      </div>

      {/* Messages */}
      <MessageList
        messages={messages}
        onReaction={sendReaction}
        onReply={setReplyingTo}
        onLoadMore={loadMore}
        isLoadingMore={isLoadingMore}
        hasMore={hasMore}
      />

      {/* Input */}
      <MessageInput
        onSend={handleSend}
        onTyping={sendTyping}
        replyingTo={replyingTo}
        onCancelReply={() => setReplyingTo(null)}
      />

      {/* Bot Manager Sheet */}
      <BotManager />
    </div>
  )
}
