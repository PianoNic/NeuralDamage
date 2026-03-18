import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"
import { Plus, MessageSquare, LogOut, Bot, Trash2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog"
import { useAuthStore } from "@/stores/auth-store"
import { useChatStore } from "@/stores/chat-store"
import { apiFetch } from "@/lib/api"
import type { Chat } from "@/types"
import { cn } from "@/lib/utils"
import { toast } from "sonner"

interface Props {
  onNavigate?: () => void
}

export function Sidebar({ onNavigate }: Props) {
  const navigate = useNavigate()
  const { user, logout } = useAuthStore()
  const { chats, setChats, activeChat, setActiveChat, setMessages, removeChat } = useChatStore()
  const [newChatName, setNewChatName] = useState("")
  const [dialogOpen, setDialogOpen] = useState(false)

  useEffect(() => {
    apiFetch<Chat[]>("/api/chats").then(setChats).catch(console.error)
  }, [setChats])

  const createChat = async () => {
    if (!newChatName.trim()) return
    const chat = await apiFetch<Chat>("/api/chats", {
      method: "POST",
      body: JSON.stringify({ name: newChatName }),
    })
    setChats([...chats, chat])
    setNewChatName("")
    setDialogOpen(false)
    toast.success("Chat created")
    navigate(`/chat/${chat.id}`)
    onNavigate?.()
  }

  const deleteChat = async (e: React.MouseEvent, chatId: string) => {
    e.stopPropagation()
    try {
      await apiFetch(`/api/chats/${chatId}`, { method: "DELETE" })
      removeChat(chatId)
      toast.success("Chat deleted")
      if (activeChat?.id === chatId) {
        navigate("/")
      }
    } catch (err) {
      toast.error("Failed to delete chat")
    }
  }

  const selectChat = (chat: Chat) => {
    setActiveChat(chat)
    setMessages([])
    navigate(`/chat/${chat.id}`)
    onNavigate?.()
  }

  return (
    <div className="w-72 border-r bg-sidebar-background flex flex-col h-full">
      {/* Header */}
      <div className="px-4 py-2.5 border-b flex items-center justify-between">
        <h2 className="font-semibold text-base tracking-tight text-sidebar-foreground">Neural Damage</h2>
        <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
          <DialogTrigger asChild>
            <Button variant="ghost" size="icon" className="h-8 w-8 rounded-lg hover:bg-sidebar-accent/60">
              <Plus className="h-4 w-4" />
            </Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>New Chat</DialogTitle>
              <DialogDescription>Create a new group chat</DialogDescription>
            </DialogHeader>
            <div className="flex gap-2 mt-2">
              <Input
                placeholder="Chat name..."
                value={newChatName}
                onChange={(e) => setNewChatName(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && createChat()}
              />
              <Button onClick={createChat}>Create</Button>
            </div>
          </DialogContent>
        </Dialog>
      </div>

      {/* Chat list */}
      <ScrollArea className="flex-1">
        <div className="px-3 py-2 space-y-0.5">
          {chats.map((chat) => (
            <div key={chat.id} className="group relative">
              <button
                onClick={() => selectChat(chat)}
                className={cn(
                  "w-full flex items-center gap-2.5 px-2.5 py-2 rounded-md text-sm text-left transition-colors",
                  activeChat?.id === chat.id
                    ? "bg-sidebar-accent text-sidebar-accent-foreground font-medium"
                    : "hover:bg-sidebar-accent/50 text-sidebar-foreground"
                )}
              >
                <MessageSquare className="h-4 w-4 shrink-0 opacity-60" />
                <span className="truncate flex-1">{chat.name}</span>
                {chat.members.filter((m) => m.member_type === "bot").length > 0 && (
                  <Bot className="h-3 w-3 text-muted-foreground shrink-0 group-hover:hidden" />
                )}
              </button>
              <button
                onClick={(e) => deleteChat(e, chat.id)}
                className="absolute right-1.5 top-1/2 -translate-y-1/2 hidden group-hover:flex h-6 w-6 items-center justify-center rounded text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors"
                title="Delete chat"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          ))}
        </div>
      </ScrollArea>

      {/* User footer */}
      <div className="px-4 py-3 border-t">
        <div className="flex items-center gap-2.5">
          <Avatar className="h-7 w-7">
            <AvatarImage src={user?.avatar_url ?? undefined} />
            <AvatarFallback className="text-xs">{user?.display_name?.[0]?.toUpperCase() ?? "?"}</AvatarFallback>
          </Avatar>
          <span className="text-sm truncate flex-1 text-sidebar-foreground">{user?.display_name}</span>
          <Button variant="ghost" size="icon" className="h-7 w-7 rounded-lg hover:bg-sidebar-accent/60" onClick={logout} title="Logout">
            <LogOut className="h-3.5 w-3.5" />
          </Button>
        </div>
      </div>
    </div>
  )
}
