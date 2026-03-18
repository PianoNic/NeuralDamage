import { useState, useEffect, useMemo } from "react"
import { Search } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Slider } from "@/components/ui/slider"
import { ScrollArea } from "@/components/ui/scroll-area"
import { apiFetch } from "@/lib/api"
import type { OpenRouterModel, Bot } from "@/types"
import { cn } from "@/lib/utils"
import { toast } from "sonner"

interface Props {
  onCreated: (bot: Bot) => void
  editingBot?: Bot | null
}

export function BotForm({ onCreated, editingBot }: Props) {
  const [name, setName] = useState(editingBot?.name ?? "")
  const [modelId, setModelId] = useState(editingBot?.model_id ?? "")
  const [systemPrompt, setSystemPrompt] = useState(editingBot?.system_prompt ?? "")
  const [personality, setPersonality] = useState(editingBot?.personality ?? "")
  const [temperature, setTemperature] = useState(String(editingBot?.temperature ?? "0.7"))
  const [models, setModels] = useState<OpenRouterModel[]>([])
  const [modelSearch, setModelSearch] = useState("")
  const [loading, setLoading] = useState(false)
  const [modelsLoading, setModelsLoading] = useState(true)

  useEffect(() => {
    setName(editingBot?.name ?? "")
    setModelId(editingBot?.model_id ?? "")
    setSystemPrompt(editingBot?.system_prompt ?? "")
    setPersonality(editingBot?.personality ?? "")
    setTemperature(String(editingBot?.temperature ?? "0.7"))
  }, [editingBot])

  useEffect(() => {
    setModelsLoading(true)
    apiFetch<OpenRouterModel[]>("/api/bots/models")
      .then((data) => {
        setModels(data)
        if (data.length > 0 && !modelId && !editingBot) {
          setModelId(data[0].id)
        }
      })
      .catch(console.error)
      .finally(() => setModelsLoading(false))
  }, [])

  const filteredModels = useMemo(() => {
    if (!modelSearch.trim()) return models
    const q = modelSearch.toLowerCase()
    return models.filter(
      (m) => m.id.toLowerCase().includes(q) || m.name.toLowerCase().includes(q)
    )
  }, [models, modelSearch])

  const isEditing = !!editingBot

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim() || !systemPrompt.trim() || !modelId) return

    setLoading(true)
    try {
      const payload = {
        name: name.trim(),
        model_id: modelId,
        system_prompt: systemPrompt.trim(),
        personality: personality.trim() || null,
        temperature: parseFloat(temperature),
      }

      const bot = isEditing
        ? await apiFetch<Bot>(`/api/bots/${editingBot.id}`, {
            method: "PUT",
            body: JSON.stringify(payload),
          })
        : await apiFetch<Bot>("/api/bots", {
            method: "POST",
            body: JSON.stringify(payload),
          })

      onCreated(bot)
      toast.success(isEditing ? "Bot updated" : "Bot created")
      if (!isEditing) {
        setName("")
        setSystemPrompt("")
        setPersonality("")
      }
    } catch (err) {
      toast.error(isEditing ? "Failed to update bot" : "Failed to create bot")
    } finally {
      setLoading(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <Label htmlFor="bot-name">Name</Label>
        <Input id="bot-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Sage" required />
      </div>

      <div>
        <Label>Model</Label>
        <div className="relative mt-1">
          <Search className="absolute left-2 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input
            value={modelSearch}
            onChange={(e) => setModelSearch(e.target.value)}
            placeholder="Search models..."
            className="pl-8"
          />
        </div>
        {modelsLoading ? (
          <p className="text-xs text-muted-foreground mt-1">Loading models...</p>
        ) : (
          <ScrollArea className="h-[200px] mt-1 border rounded-md">
            <div className="p-1">
              {filteredModels.map((m) => (
                <button
                  key={m.id}
                  type="button"
                  onClick={() => setModelId(m.id)}
                  className={cn(
                    "w-full text-left px-2 py-1.5 rounded text-sm transition-colors",
                    modelId === m.id
                      ? "bg-primary text-primary-foreground"
                      : "hover:bg-muted"
                  )}
                >
                  <div className="font-medium truncate">{m.name}</div>
                  <div className={cn(
                    "text-xs truncate",
                    modelId === m.id ? "text-primary-foreground/70" : "text-muted-foreground"
                  )}>
                    {m.id}
                  </div>
                </button>
              ))}
              {filteredModels.length === 0 && (
                <p className="text-xs text-muted-foreground p-2">No models match "{modelSearch}"</p>
              )}
            </div>
          </ScrollArea>
        )}
        {modelId && (
          <p className="text-xs text-muted-foreground mt-1">Selected: {modelId}</p>
        )}
      </div>

      <div>
        <Label htmlFor="bot-prompt">System Prompt</Label>
        <Textarea
          id="bot-prompt"
          value={systemPrompt}
          onChange={(e) => setSystemPrompt(e.target.value)}
          placeholder="You are a helpful assistant who specializes in..."
          rows={3}
          required
        />
      </div>

      <div>
        <Label htmlFor="bot-personality">Personality (optional, helps response engine)</Label>
        <Input
          id="bot-personality"
          value={personality}
          onChange={(e) => setPersonality(e.target.value)}
          placeholder="e.g. sarcastic tech expert, friendly tutor"
        />
      </div>

      <div>
        <Label>Temperature ({temperature})</Label>
        <Slider
          value={[parseFloat(temperature)]}
          onValueChange={(vals: number[]) => setTemperature(vals[0].toFixed(1))}
          min={0}
          max={2}
          step={0.1}
          className="mt-2"
        />
      </div>

      <Button type="submit" disabled={loading || !modelId} className="w-full">
        {loading ? (isEditing ? "Saving..." : "Creating...") : (isEditing ? "Save Changes" : "Create Bot")}
      </Button>
    </form>
  )
}
