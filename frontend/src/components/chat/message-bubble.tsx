import { useState } from "react"
import Markdown from "react-markdown"
import { SmilePlus, Reply } from "lucide-react"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { ReactionBar } from "./reaction-bar"
import type { Message } from "@/types"
import { cn } from "@/lib/utils"

const QUICK_EMOJIS = ["\ud83d\udc4d", "\u2764\ufe0f", "\ud83d\ude02", "\ud83d\ude2e", "\ud83d\ude22", "\ud83d\ude4f", "\ud83d\udd25", "\ud83d\udcaf"]

interface Props {
  message: Message
  isOwn: boolean
  currentUserId?: string
  onReaction?: (messageId: string, emoji: string) => void
  onReply?: (message: Message) => void
  onReplyClick?: (messageId: string) => void
}

export function MessageBubble({ message, isOwn, currentUserId, onReaction, onReply, onReplyClick }: Props) {
  const [pickerOpen, setPickerOpen] = useState(false)

  const time = new Date(message.created_at).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
  })

  const handleEmojiSelect = (emoji: string) => {
    onReaction?.(message.id, emoji)
    setPickerOpen(false)
  }

  return (
    <div
      data-message-id={message.id}
      className={cn("flex gap-2 sm:gap-3 px-3 sm:px-4 py-1.5 group transition-colors duration-500", isOwn && "flex-row-reverse")}
    >
      <Avatar className="h-7 w-7 shrink-0 mt-0.5">
        <AvatarImage src={message.sender_avatar ?? undefined} />
        <AvatarFallback className={cn(
          "text-[10px]",
          message.sender_type === "bot" && "bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300"
        )}>
          {message.sender_name[0]?.toUpperCase()}
        </AvatarFallback>
      </Avatar>
      <div className={cn("flex flex-col max-w-[85%] sm:max-w-[70%]", isOwn && "items-end")}>
        <div className="flex items-center gap-1.5 mb-0.5">
          <span className="text-xs font-medium text-foreground">{message.sender_name}</span>
          {message.sender_type === "bot" && (
            <Badge variant="secondary" className="text-[9px] px-1 py-0 leading-tight">BOT</Badge>
          )}
          <span className="text-[10px] text-muted-foreground">{time}</span>
        </div>

        {/* Reply quote */}
        {message.reply_to && (
          <button
            onClick={() => onReplyClick?.(message.reply_to!.id)}
            className={cn(
              "flex items-start gap-2 mb-1 px-2 py-1 rounded text-xs max-w-full cursor-pointer transition-colors",
              "bg-muted/60 hover:bg-muted border-l-2",
              message.reply_to.sender_type === "bot"
                ? "border-l-purple-400"
                : "border-l-primary"
            )}
          >
            <div className="min-w-0">
              <span className={cn(
                "font-semibold block text-[11px]",
                message.reply_to.sender_type === "bot" ? "text-purple-600 dark:text-purple-400" : "text-primary"
              )}>
                {message.reply_to.sender_name}
              </span>
              <span className="text-muted-foreground line-clamp-1 text-left text-[11px]">
                {message.reply_to.content}
              </span>
            </div>
          </button>
        )}

        <div className="relative">
          <div
            className={cn(
              "rounded-lg px-2.5 py-1.5 text-sm break-words",
              isOwn
                ? "bg-primary text-primary-foreground"
                : message.sender_type === "bot"
                  ? "bg-purple-50 text-foreground dark:bg-purple-950/30"
                  : "bg-muted text-foreground"
            )}
          >
            <Markdown
              components={{
                p: ({ children }) => <p className="mb-1.5 last:mb-0">{children}</p>,
                strong: ({ children }) => <strong className="font-semibold">{children}</strong>,
                em: ({ children }) => <em className="italic">{children}</em>,
                code: ({ children, className }) => {
                  const isBlock = className?.includes("language-")
                  if (isBlock) {
                    return (
                      <pre className="bg-black/10 dark:bg-white/10 rounded p-2 my-1.5 overflow-x-auto text-xs">
                        <code>{children}</code>
                      </pre>
                    )
                  }
                  return (
                    <code className="bg-black/10 dark:bg-white/10 rounded px-1 py-0.5 text-xs font-mono">
                      {children}
                    </code>
                  )
                },
                pre: ({ children }) => <>{children}</>,
                ul: ({ children }) => <ul className="list-disc pl-4 mb-1.5">{children}</ul>,
                ol: ({ children }) => <ol className="list-decimal pl-4 mb-1.5">{children}</ol>,
                li: ({ children }) => <li className="mb-0.5">{children}</li>,
                a: ({ href, children }) => (
                  <a href={href} target="_blank" rel="noopener noreferrer" className="underline text-blue-600 dark:text-blue-400">
                    {children}
                  </a>
                ),
                blockquote: ({ children }) => (
                  <blockquote className="border-l-2 border-muted-foreground/30 pl-2 italic my-1.5">
                    {children}
                  </blockquote>
                ),
                h1: ({ children }) => <h1 className="text-base font-bold mb-1">{children}</h1>,
                h2: ({ children }) => <h2 className="text-sm font-bold mb-1">{children}</h2>,
                h3: ({ children }) => <h3 className="text-sm font-bold mb-1">{children}</h3>,
              }}
            >
              {message.content}
            </Markdown>
          </div>

          {/* Action buttons — appear on hover, hidden for own messages */}
          {!isOwn && (
            <>
              <div className={cn(
                "absolute -bottom-2 right-0 flex gap-0.5 transition-opacity",
                pickerOpen ? "opacity-100" : "opacity-0 group-hover:opacity-100"
              )}>
                <button
                  onClick={() => onReply?.(message)}
                  className="h-5 w-5 rounded-full bg-background border shadow-sm flex items-center justify-center cursor-pointer hover:bg-accent"
                >
                  <Reply className="h-3 w-3 text-muted-foreground" />
                </button>
                <button
                  onClick={() => setPickerOpen((v) => !v)}
                  className="h-5 w-5 rounded-full bg-background border shadow-sm flex items-center justify-center cursor-pointer hover:bg-accent"
                >
                  <SmilePlus className="h-3 w-3 text-muted-foreground" />
                </button>
              </div>

              {/* Quick emoji picker */}
              {pickerOpen && (
                <div className="absolute -bottom-9 left-0 z-50 flex gap-0.5 p-0.5 bg-popover rounded-lg shadow-lg border">
                  {QUICK_EMOJIS.map((emoji) => (
                    <button
                      key={emoji}
                      onClick={() => handleEmojiSelect(emoji)}
                      className="hover:bg-accent rounded p-1 text-sm cursor-pointer transition-transform hover:scale-125 leading-none"
                    >
                      {emoji}
                    </button>
                  ))}
                </div>
              )}
            </>
          )}
        </div>

        {/* Reaction badges */}
        {message.reactions?.length > 0 && (
          <ReactionBar
            reactions={message.reactions}
            onToggle={(emoji) => onReaction?.(message.id, emoji)}
            currentUserId={currentUserId}
          />
        )}
      </div>
    </div>
  )
}
