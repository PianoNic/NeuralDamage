import { useState, useRef, useCallback, useEffect, useMemo } from "react"
import { Send, X, Reply, Terminal } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { useChatStore } from "@/stores/chat-store"
import { cn } from "@/lib/utils"
import type { ChatMember, Message } from "@/types"

const SLASH_COMMANDS = [
  { cmd: "/stop", desc: "Stop all bot responses immediately" },
  { cmd: "/clear", desc: "Clear all messages in this chat" },
  { cmd: "/mute", desc: "Mute all bots until /unmute" },
  { cmd: "/unmute", desc: "Unmute all bots" },
  { cmd: "/kick", desc: "Remove a bot — /kick BotName" },
  { cmd: "/rename", desc: "Rename the chat — /rename New Name" },
  { cmd: "/bots", desc: "List all bots in this chat" },
  { cmd: "/help", desc: "Show available commands" },
]

interface Props {
  onSend: (content: string, mentions: string[], replyToId?: string) => void
  onTyping?: () => void
  replyingTo?: Message | null
  onCancelReply?: () => void
}

export function MessageInput({ onSend, onTyping, replyingTo, onCancelReply }: Props) {
  const [value, setValue] = useState("")
  const [mentionQuery, setMentionQuery] = useState<string | null>(null)
  const [mentionIndex, setMentionIndex] = useState(0)
  const [mentionStartPos, setMentionStartPos] = useState(0)
  const [slashQuery, setSlashQuery] = useState<string | null>(null)
  const [slashIndex, setSlashIndex] = useState(0)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const lastTypingSent = useRef(0)
  const activeChat = useChatStore((s) => s.activeChat)

  const filteredSlash = useMemo(() => {
    if (slashQuery === null) return []
    return SLASH_COMMANDS.filter((c) =>
      c.cmd.toLowerCase().startsWith(`/${slashQuery.toLowerCase()}`)
    )
  }, [slashQuery])

  // Focus textarea when replying
  useEffect(() => {
    if (replyingTo) {
      textareaRef.current?.focus()
    }
  }, [replyingTo])

  // All mentionable members (bots + users)
  const members = activeChat?.members ?? []

  const filtered = mentionQuery !== null
    ? members.filter((m) =>
        m.display_name?.toLowerCase().includes(mentionQuery.toLowerCase())
      )
    : []

  // Clamp selection index
  useEffect(() => {
    if (mentionIndex >= filtered.length) {
      setMentionIndex(Math.max(0, filtered.length - 1))
    }
  }, [filtered.length, mentionIndex])

  useEffect(() => {
    if (slashIndex >= filteredSlash.length) {
      setSlashIndex(Math.max(0, filteredSlash.length - 1))
    }
  }, [filteredSlash.length, slashIndex])

  const insertMention = useCallback((member: ChatMember) => {
    const before = value.slice(0, mentionStartPos)
    const after = value.slice(textareaRef.current?.selectionStart ?? value.length)
    const name = member.display_name ?? "unknown"
    const newValue = `${before}@${name} ${after}`
    setValue(newValue)
    setMentionQuery(null)

    requestAnimationFrame(() => {
      const pos = before.length + name.length + 2
      textareaRef.current?.focus()
      textareaRef.current?.setSelectionRange(pos, pos)
    })
  }, [value, mentionStartPos])

  const handleChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    const newValue = e.target.value
    setValue(newValue)

    const now = Date.now()
    if (onTyping && now - lastTypingSent.current > 2000) {
      lastTypingSent.current = now
      onTyping()
    }

    const cursorPos = e.target.selectionStart ?? newValue.length
    const textBeforeCursor = newValue.slice(0, cursorPos)

    // Slash command detection — only at the very start of input
    if (textBeforeCursor.startsWith("/") && !textBeforeCursor.includes(" ")) {
      setSlashQuery(textBeforeCursor.slice(1))
      setSlashIndex(0)
      setMentionQuery(null)
      return
    }
    setSlashQuery(null)

    const lastAt = textBeforeCursor.lastIndexOf("@")
    if (lastAt !== -1) {
      const charBefore = lastAt > 0 ? textBeforeCursor[lastAt - 1] : " "
      if (charBefore === " " || charBefore === "\n" || lastAt === 0) {
        const query = textBeforeCursor.slice(lastAt + 1)
        if (!query.includes(" ")) {
          setMentionQuery(query)
          setMentionStartPos(lastAt)
          setMentionIndex(0)
          return
        }
      }
    }
    setMentionQuery(null)
  }

  const handleSend = () => {
    const content = value.trim()
    if (!content) return

    const mentions: string[] = []
    if (activeChat) {
      for (const member of activeChat.members) {
        if (member.display_name && content.toLowerCase().includes(`@${member.display_name.toLowerCase()}`)) {
          if (member.bot_id) mentions.push(member.bot_id)
        }
      }
    }

    onSend(content, mentions, replyingTo?.id)
    setValue("")
    setMentionQuery(null)
    setSlashQuery(null)
    textareaRef.current?.focus()
  }

  const insertSlashCommand = useCallback((cmd: string) => {
    // For commands that need args, insert command + space; otherwise just the command
    const needsArg = cmd === "/kick" || cmd === "/rename"
    setValue(needsArg ? `${cmd} ` : cmd)
    setSlashQuery(null)
    requestAnimationFrame(() => {
      const pos = cmd.length + (needsArg ? 1 : 0)
      textareaRef.current?.focus()
      textareaRef.current?.setSelectionRange(pos, pos)
    })
  }, [])

  const handleKeyDown = (e: React.KeyboardEvent) => {
    // Slash command autocomplete
    if (slashQuery !== null && filteredSlash.length > 0) {
      if (e.key === "ArrowDown") {
        e.preventDefault()
        setSlashIndex((i) => (i + 1) % filteredSlash.length)
        return
      }
      if (e.key === "ArrowUp") {
        e.preventDefault()
        setSlashIndex((i) => (i - 1 + filteredSlash.length) % filteredSlash.length)
        return
      }
      if (e.key === "Tab") {
        e.preventDefault()
        insertSlashCommand(filteredSlash[slashIndex].cmd)
        return
      }
      if (e.key === "Escape") {
        e.preventDefault()
        setSlashQuery(null)
        return
      }
    }

    if (mentionQuery !== null && filtered.length > 0) {
      if (e.key === "ArrowDown") {
        e.preventDefault()
        setMentionIndex((i) => (i + 1) % filtered.length)
        return
      }
      if (e.key === "ArrowUp") {
        e.preventDefault()
        setMentionIndex((i) => (i - 1 + filtered.length) % filtered.length)
        return
      }
      if (e.key === "Tab" || e.key === "Enter") {
        e.preventDefault()
        insertMention(filtered[mentionIndex])
        return
      }
      if (e.key === "Escape") {
        e.preventDefault()
        setMentionQuery(null)
        return
      }
    }

    // Escape cancels reply
    if (e.key === "Escape" && replyingTo) {
      e.preventDefault()
      onCancelReply?.()
      return
    }

    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  return (
    <div className="border-t">
      {/* Reply preview bar */}
      {replyingTo && (
        <div className="flex items-center gap-2 px-4 py-2 bg-muted/40 border-b">
          <Reply className="h-4 w-4 text-muted-foreground shrink-0" />
          <div className="flex-1 min-w-0">
            <span className={cn(
              "text-xs font-semibold",
              replyingTo.sender_type === "bot" ? "text-purple-600 dark:text-purple-400" : "text-primary"
            )}>
              {replyingTo.sender_name}
            </span>
            <p className="text-xs text-muted-foreground truncate">{replyingTo.content}</p>
          </div>
          <button
            onClick={onCancelReply}
            className="h-5 w-5 rounded-full hover:bg-muted flex items-center justify-center shrink-0 cursor-pointer"
          >
            <X className="h-3.5 w-3.5 text-muted-foreground" />
          </button>
        </div>
      )}

      <div className="px-4 py-3">
        <div className="relative">
          {/* Slash command autocomplete popup */}
          {slashQuery !== null && filteredSlash.length > 0 && (
            <div className="absolute bottom-full left-0 right-0 mb-1 bg-popover border rounded-lg shadow-lg overflow-hidden z-50 max-h-[200px] overflow-y-auto">
              {filteredSlash.map((item, i) => (
                <button
                  key={item.cmd}
                  onMouseDown={(e) => {
                    e.preventDefault()
                    insertSlashCommand(item.cmd)
                  }}
                  className={cn(
                    "flex items-center gap-2 w-full px-3 py-2 text-sm text-left transition-colors",
                    i === slashIndex ? "bg-accent" : "hover:bg-accent/50"
                  )}
                >
                  <Terminal className="h-4 w-4 text-muted-foreground shrink-0" />
                  <span className="font-mono font-medium">{item.cmd}</span>
                  <span className="text-muted-foreground text-xs ml-auto hidden sm:inline">{item.desc}</span>
                </button>
              ))}
            </div>
          )}

          {/* Mention autocomplete popup */}
          {mentionQuery !== null && filtered.length > 0 && (
            <div className="absolute bottom-full left-0 right-0 mb-1 bg-popover border rounded-lg shadow-lg overflow-hidden z-50 max-h-[200px] overflow-y-auto">
              {filtered.map((member, i) => (
                <button
                  key={member.id}
                  onMouseDown={(e) => {
                    e.preventDefault()
                    insertMention(member)
                  }}
                  className={cn(
                    "flex items-center gap-2 w-full px-3 py-2 text-sm text-left transition-colors",
                    i === mentionIndex ? "bg-accent" : "hover:bg-accent/50"
                  )}
                >
                  <Avatar className="h-6 w-6">
                    <AvatarImage src={member.avatar_url ?? undefined} />
                    <AvatarFallback className={cn(
                      "text-[10px]",
                      member.member_type === "bot" && "bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300"
                    )}>
                      {member.display_name?.[0]?.toUpperCase()}
                    </AvatarFallback>
                  </Avatar>
                  <span className="font-medium">{member.display_name}</span>
                  {member.member_type === "bot" && (
                    <Badge variant="secondary" className="text-[10px] px-1 py-0 ml-auto">BOT</Badge>
                  )}
                </button>
              ))}
            </div>
          )}

          <div className="flex gap-2 items-end">
            <Textarea
              ref={textareaRef}
              value={value}
              onChange={handleChange}
              onKeyDown={handleKeyDown}
              placeholder={replyingTo ? `Reply to ${replyingTo.sender_name}...` : "Type a message... (use @ to mention)"}
              className="min-h-[40px] max-h-[120px] resize-none"
              rows={1}
            />
            <Button onClick={handleSend} size="icon" disabled={!value.trim()}>
              <Send className="h-4 w-4" />
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}
