import { cn } from "@/lib/utils"
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import type { ReactionGroup } from "@/types"

interface Props {
  reactions: ReactionGroup[]
  onToggle: (emoji: string) => void
  currentUserId: string | undefined
}

export function ReactionBar({ reactions, onToggle, currentUserId }: Props) {
  if (reactions.length === 0) return null

  return (
    <TooltipProvider delayDuration={200}>
      <div className="flex flex-wrap gap-1 mt-1">
        {reactions.map((r) => {
          const isMine = currentUserId && r.user_ids.includes(currentUserId)
          const names = r.names?.length ? r.names.join(", ") : `${r.count} reaction${r.count !== 1 ? "s" : ""}`
          return (
            <Tooltip key={r.emoji}>
              <TooltipTrigger asChild>
                <button
                  onClick={() => onToggle(r.emoji)}
                  className={cn(
                    "inline-flex items-center gap-0.5 rounded-full px-1.5 py-0.5 text-xs border cursor-pointer transition-colors",
                    isMine
                      ? "bg-primary/10 border-primary/30 text-primary"
                      : "bg-muted border-transparent text-muted-foreground hover:border-border"
                  )}
                >
                  <span>{r.emoji}</span>
                  <span>{r.count}</span>
                </button>
              </TooltipTrigger>
              <TooltipContent side="top" className="text-xs">
                {names}
              </TooltipContent>
            </Tooltip>
          )
        })}
      </div>
    </TooltipProvider>
  )
}
