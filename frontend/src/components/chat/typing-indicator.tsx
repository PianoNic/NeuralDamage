import { useUIStore } from "@/stores/ui-store"
import { useChatStore } from "@/stores/chat-store"

export function TypingIndicator() {
  const typingBots = useUIStore((s) => s.typingBots)
  const typingUsers = useUIStore((s) => s.typingUsers)
  const activeChat = useChatStore((s) => s.activeChat)

  if ((typingBots.size === 0 && typingUsers.size === 0) || !activeChat) return null

  const names: string[] = []

  // User names
  for (const userId of typingUsers) {
    const member = activeChat.members.find((m) => m.user_id === userId)
    if (member?.display_name) names.push(member.display_name)
  }

  // Bot names — read directly from the Map (name is sent via WS)
  for (const [, botName] of typingBots) {
    names.push(botName)
  }

  if (names.length === 0) return null

  const text = names.length === 1
    ? `${names[0]} is typing...`
    : `${names.join(", ")} are typing...`

  return (
    <div className="px-4 py-1.5 text-xs text-muted-foreground animate-pulse">
      {text}
    </div>
  )
}
