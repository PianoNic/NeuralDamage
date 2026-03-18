import { MessageSquare, Bot, Zap } from "lucide-react"

export function HomePage() {
  return (
    <div className="flex-1 flex items-center justify-center">
      <div className="text-center space-y-6 max-w-md">
        <h1 className="text-3xl font-bold">Neural Damage</h1>
        <p className="text-muted-foreground">
          Create a chat and add AI bots from different providers. They'll join the conversation naturally.
        </p>
        <div className="grid grid-cols-3 gap-4 text-sm">
          <div className="flex flex-col items-center gap-2 p-3">
            <MessageSquare className="h-8 w-8 text-muted-foreground" />
            <span>Group Chat</span>
          </div>
          <div className="flex flex-col items-center gap-2 p-3">
            <Bot className="h-8 w-8 text-muted-foreground" />
            <span>Multi-AI Bots</span>
          </div>
          <div className="flex flex-col items-center gap-2 p-3">
            <Zap className="h-8 w-8 text-muted-foreground" />
            <span>Natural Flow</span>
          </div>
        </div>
        <p className="text-xs text-muted-foreground">
          Select a chat from the sidebar or create a new one to get started.
        </p>
      </div>
    </div>
  )
}
