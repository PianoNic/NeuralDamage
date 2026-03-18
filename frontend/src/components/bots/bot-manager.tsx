import { useEffect, useState } from "react"
import { Plus, X, Bot as BotIcon, Users, UserPlus, Trash2, Pencil, Search } from "lucide-react"
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from "@/components/ui/sheet"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { Separator } from "@/components/ui/separator"
import { ScrollArea } from "@/components/ui/scroll-area"
import { BotForm } from "./bot-form"
import { useUIStore } from "@/stores/ui-store"
import { useChatStore } from "@/stores/chat-store"
import { Input } from "@/components/ui/input"
import { apiFetch } from "@/lib/api"
import type { Bot, User } from "@/types"
import { toast } from "sonner"

export function BotManager() {
  const { botManagerOpen, setBotManagerOpen } = useUIStore()
  const { activeChat } = useChatStore()
  const [availableBots, setAvailableBots] = useState<Bot[]>([])
  const [allUsers, setAllUsers] = useState<User[]>([])
  const [botFormOpen, setBotFormOpen] = useState(false)
  const [editingBot, setEditingBot] = useState<Bot | null>(null)
  const [botSearch, setBotSearch] = useState("")

  useEffect(() => {
    if (botManagerOpen) {
      apiFetch<Bot[]>("/api/bots").then(setAvailableBots).catch(console.error)
      apiFetch<User[]>("/api/users").then(setAllUsers).catch(console.error)
    }
  }, [botManagerOpen])

  if (!activeChat) return null

  const botMembers = activeChat.members.filter((m) => m.member_type === "bot")
  const userMembers = activeChat.members.filter((m) => m.member_type === "user")
  const addedBotIds = new Set(botMembers.map((m) => m.bot_id))
  const addedUserIds = new Set(userMembers.map((m) => m.user_id))
  const unaddedBots = availableBots.filter((b) => !addedBotIds.has(b.id))
  const filteredBots = botSearch
    ? unaddedBots.filter((b) =>
        b.name.toLowerCase().includes(botSearch.toLowerCase()) ||
        b.model_id.toLowerCase().includes(botSearch.toLowerCase())
      )
    : unaddedBots
  const unaddedUsers = allUsers.filter((u) => !addedUserIds.has(u.id))

  // State updates come via WebSocket broadcasts — no manual store updates needed
  const handleAddBot = async (botId: string) => {
    try {
      await apiFetch(`/api/chats/${activeChat.id}/members`, {
        method: "POST",
        body: JSON.stringify({ bot_id: botId }),
      })
      toast.success("Bot added to chat")
    } catch (err) {
      toast.error("Failed to add bot")
    }
  }

  const handleAddUser = async (userId: string) => {
    try {
      await apiFetch(`/api/chats/${activeChat.id}/members`, {
        method: "POST",
        body: JSON.stringify({ user_id: userId }),
      })
      toast.success("User invited to chat")
    } catch (err) {
      toast.error("Failed to invite user")
    }
  }

  const handleRemoveMember = async (memberId: string) => {
    try {
      await apiFetch(`/api/chats/${activeChat.id}/members/${memberId}`, { method: "DELETE" })
      toast.success("Member removed")
    } catch (err) {
      toast.error("Failed to remove member")
    }
  }

  const handleBotCreated = (bot: Bot) => {
    if (editingBot) {
      setAvailableBots((prev) => prev.map((b) => (b.id === bot.id ? bot : b)))
    } else {
      setAvailableBots((prev) => [...prev, bot])
    }
    setEditingBot(null)
    setBotFormOpen(false)
  }

  const handleDeleteBot = async (botId: string) => {
    try {
      await apiFetch(`/api/bots/${botId}`, { method: "DELETE" })
      setAvailableBots((prev) => prev.filter((b) => b.id !== botId))
      toast.success("Bot deleted")
    } catch (err) {
      toast.error("Failed to delete bot")
    }
  }

  const openEditBot = (bot: Bot) => {
    setEditingBot(bot)
    setBotFormOpen(true)
  }

  const openCreateBot = () => {
    setEditingBot(null)
    setBotFormOpen(true)
  }

  return (
    <>
      <Sheet open={botManagerOpen} onOpenChange={(open) => { setBotManagerOpen(open); if (!open) setBotSearch(""); }}>
        <SheetContent className="w-[380px] sm:w-[420px] p-0 flex flex-col gap-0">
          <SheetHeader className="px-4 pt-4 pb-3">
            <SheetTitle className="flex items-center gap-2 text-base">
              <Users className="h-4 w-4" /> Manage Members
            </SheetTitle>
            <SheetDescription className="text-xs">Add or remove bots and users from this chat</SheetDescription>
          </SheetHeader>

          <ScrollArea className="flex-1">
            <div className="px-4 pb-4">
              {/* Users section */}
              <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2 flex items-center gap-1.5">
                <Users className="h-3 w-3" /> Users ({userMembers.length})
              </h3>
              <div className="max-h-[180px] overflow-y-auto space-y-1 mb-4">
                {userMembers.map((m) => (
                  <div key={m.id} className="flex items-center gap-2.5 py-1.5 px-2 rounded-md hover:bg-muted/50">
                    <Avatar className="h-7 w-7 shrink-0">
                      <AvatarImage src={m.avatar_url ?? undefined} />
                      <AvatarFallback className="text-[10px]">{m.display_name?.[0]?.toUpperCase()}</AvatarFallback>
                    </Avatar>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-1.5">
                        <span className="text-sm font-medium truncate">{m.display_name}</span>
                        {m.role === "owner" && <Badge variant="outline" className="text-[9px] px-1 py-0">OWNER</Badge>}
                      </div>
                    </div>
                    {m.role !== "owner" && (
                      <button onClick={() => handleRemoveMember(m.id)} className="h-6 w-6 rounded hover:bg-muted flex items-center justify-center shrink-0 text-muted-foreground hover:text-foreground">
                        <X className="h-3.5 w-3.5" />
                      </button>
                    )}
                  </div>
                ))}
              </div>

              {/* Invite users */}
              {unaddedUsers.length > 0 && (
                <div className="mb-4">
                  <h4 className="text-[11px] text-muted-foreground mb-1.5">Invite users</h4>
                  <div className="space-y-1">
                    {unaddedUsers.map((user) => (
                      <div key={user.id} className="flex items-center gap-2.5 py-1.5 px-2 rounded-md hover:bg-muted/50">
                        <Avatar className="h-6 w-6 shrink-0">
                          <AvatarImage src={user.avatar_url ?? undefined} />
                          <AvatarFallback className="text-[9px]">{user.display_name[0]?.toUpperCase()}</AvatarFallback>
                        </Avatar>
                        <div className="flex-1 min-w-0">
                          <span className="text-sm truncate block">{user.display_name}</span>
                        </div>
                        <Button variant="ghost" size="sm" className="h-6 text-xs px-2 shrink-0" onClick={() => handleAddUser(user.id)}>
                          <UserPlus className="h-3 w-3 mr-1" /> Invite
                        </Button>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              <Separator className="my-3" />

              {/* Bots in chat */}
              <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2 flex items-center gap-1.5">
                <BotIcon className="h-3 w-3" /> Bots in chat ({botMembers.length})
              </h3>
              {botMembers.length === 0 ? (
                <p className="text-xs text-muted-foreground mb-3">No bots yet. Add one below!</p>
              ) : (
                <div className="max-h-[180px] overflow-y-auto space-y-1 mb-4">
                  {botMembers.map((m) => (
                    <div key={m.id} className="flex items-center gap-2.5 py-1.5 px-2 rounded-md hover:bg-muted/50">
                      <Avatar className="h-7 w-7 shrink-0">
                        <AvatarImage src={m.avatar_url ?? undefined} />
                        <AvatarFallback className="text-[10px] bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300">
                          {m.display_name?.[0]?.toUpperCase()}
                        </AvatarFallback>
                      </Avatar>
                      <div className="flex items-center gap-1.5 flex-1 min-w-0">
                        <span className="text-sm font-medium truncate">{m.display_name}</span>
                        <Badge variant="secondary" className="text-[9px] px-1 py-0 shrink-0">BOT</Badge>
                      </div>
                      <button onClick={() => handleRemoveMember(m.id)} className="h-6 w-6 rounded hover:bg-muted flex items-center justify-center shrink-0 text-muted-foreground hover:text-foreground">
                        <X className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  ))}
                </div>
              )}

              <Separator className="my-3" />

              {/* Available bots */}
              {unaddedBots.length > 0 && (
                <>
                  <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2">
                    Available bots ({unaddedBots.length})
                  </h3>
                  {unaddedBots.length > 4 && (
                    <div className="relative mb-2">
                      <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground" />
                      <Input
                        placeholder="Search bots..."
                        value={botSearch}
                        onChange={(e) => setBotSearch(e.target.value)}
                        className="pl-7 h-7 text-xs"
                      />
                    </div>
                  )}
                  {botSearch && filteredBots.length === 0 && (
                    <p className="text-xs text-muted-foreground text-center py-2">No bots match "{botSearch}"</p>
                  )}
                  <div className="max-h-[180px] overflow-y-auto space-y-1 mb-2">
                    {filteredBots.map((bot) => (
                      <div key={bot.id} className="flex items-center gap-2.5 py-1.5 px-2 rounded-md hover:bg-muted/50">
                        <Avatar className="h-7 w-7 shrink-0">
                          <AvatarFallback className="text-[10px] bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300">
                            {bot.name[0]?.toUpperCase()}
                          </AvatarFallback>
                        </Avatar>
                        <div className="flex-1 min-w-0">
                          <span className="text-sm font-medium truncate block">{bot.name}</span>
                          <span className="text-[11px] text-muted-foreground truncate block">{bot.model_id}</span>
                        </div>
                        <div className="flex items-center gap-0.5 shrink-0">
                          <Button variant="ghost" size="sm" className="h-6 text-xs px-2" onClick={() => handleAddBot(bot.id)}>
                            <Plus className="h-3 w-3 mr-0.5" /> Add
                          </Button>
                          <button onClick={() => openEditBot(bot)} className="h-6 w-6 rounded hover:bg-muted flex items-center justify-center text-muted-foreground hover:text-foreground">
                            <Pencil className="h-3 w-3" />
                          </button>
                          <button onClick={() => handleDeleteBot(bot.id)} className="h-6 w-6 rounded hover:bg-destructive/10 flex items-center justify-center text-muted-foreground hover:text-destructive">
                            <Trash2 className="h-3 w-3" />
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                  <Separator className="my-3" />
                </>
              )}

              {/* Create new bot */}
              <Button variant="outline" size="sm" className="w-full h-8 text-xs" onClick={openCreateBot}>
                <Plus className="h-3.5 w-3.5 mr-1.5" /> Create New Bot
              </Button>
            </div>
          </ScrollArea>
        </SheetContent>
      </Sheet>

      {/* Bot Create/Edit Modal */}
      <Dialog open={botFormOpen} onOpenChange={(open) => { setBotFormOpen(open); if (!open) setEditingBot(null); }}>
        <DialogContent className="sm:max-w-[480px] max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>{editingBot ? `Edit: ${editingBot.name}` : "Create New Bot"}</DialogTitle>
            <DialogDescription>
              {editingBot ? "Update your bot's settings" : "Set up a new AI bot to join your chats"}
            </DialogDescription>
          </DialogHeader>
          <BotForm onCreated={handleBotCreated} editingBot={editingBot} />
        </DialogContent>
      </Dialog>
    </>
  )
}
