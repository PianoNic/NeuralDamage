import { useEffect, useRef } from "react"
import { MessageBubble } from "./message-bubble"
import { TypingIndicator } from "./typing-indicator"
import { useAuthStore } from "@/stores/auth-store"
import type { Message } from "@/types"

interface Props {
  messages: Message[]
  onReaction?: (messageId: string, emoji: string) => void
  onReply?: (message: Message) => void
}

export function MessageList({ messages, onReaction, onReply }: Props) {
  const user = useAuthStore((s) => s.user)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const el = containerRef.current
    if (el) {
      el.scrollTop = el.scrollHeight
    }
  }, [messages.length])

  const scrollToMessage = (messageId: string) => {
    const el = containerRef.current?.querySelector(`[data-message-id="${messageId}"]`)
    if (el) {
      el.scrollIntoView({ behavior: "smooth", block: "center" })
      el.classList.add("bg-accent/50")
      setTimeout(() => el.classList.remove("bg-accent/50"), 1500)
    }
  }

  return (
    <div ref={containerRef} className="flex-1 overflow-y-auto">
      <div className="py-4">
        {messages.length === 0 && (
          <div className="text-center text-muted-foreground py-12">
            No messages yet. Start the conversation!
          </div>
        )}
        {messages.map((msg) =>
          (msg.sender_type as string) === "system" ? (
            <div key={msg.id} className="flex justify-center py-2 px-4">
              <span className="text-xs text-muted-foreground bg-muted/60 rounded-full px-3 py-1 whitespace-pre-wrap text-center">
                {msg.content}
              </span>
            </div>
          ) : (
            <MessageBubble
              key={msg.id}
              message={msg}
              isOwn={msg.sender_user_id === user?.id}
              currentUserId={user?.id}
              onReaction={onReaction}
              onReply={onReply}
              onReplyClick={scrollToMessage}
            />
          )
        )}
        <TypingIndicator />
      </div>
    </div>
  )
}
