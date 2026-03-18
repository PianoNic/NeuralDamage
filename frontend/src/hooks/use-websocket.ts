import { useEffect, useRef, useCallback } from "react"
import { useAuthStore } from "@/stores/auth-store"
import { useChatStore } from "@/stores/chat-store"
import { useUIStore } from "@/stores/ui-store"
import type { Message } from "@/types"

export function useWebSocket(chatId: string | null) {
  const ws = useRef<WebSocket | null>(null)
  const token = useAuthStore((s) => s.token)
  const userId = useAuthStore((s) => s.user?.id)
  const reconnectTimeout = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  // Keep stable references to store actions — never put these in the effect deps
  const addMessageRef = useRef(useChatStore.getState().addMessage)
  const updateReactionsRef = useRef(useChatStore.getState().updateReactions)
  const setMessagesRef = useRef(useChatStore.getState().setMessages)
  const addMemberRef = useRef(useChatStore.getState().addMember)
  const removeMemberRef = useRef(useChatStore.getState().removeMember)
  const removeChatRef = useRef(useChatStore.getState().removeChat)
  const addTypingBotRef = useRef(useUIStore.getState().addTypingBot)
  const removeTypingBotRef = useRef(useUIStore.getState().removeTypingBot)
  const addTypingUserRef = useRef(useUIStore.getState().addTypingUser)
  const removeTypingUserRef = useRef(useUIStore.getState().removeTypingUser)

  // Keep refs up to date without re-running the effect
  useEffect(() => {
    return useChatStore.subscribe((s) => {
      addMessageRef.current = s.addMessage
      updateReactionsRef.current = s.updateReactions
      setMessagesRef.current = s.setMessages
      addMemberRef.current = s.addMember
      removeMemberRef.current = s.removeMember
      removeChatRef.current = s.removeChat
    })
  }, [])
  useEffect(() => {
    return useUIStore.subscribe((s) => {
      addTypingBotRef.current = s.addTypingBot
      removeTypingBotRef.current = s.removeTypingBot
      addTypingUserRef.current = s.addTypingUser
      removeTypingUserRef.current = s.removeTypingUser
    })
  }, [])

  useEffect(() => {
    if (!chatId || !token) return

    let cancelled = false

    function connect() {
      if (cancelled) return

      if (ws.current && ws.current.readyState !== WebSocket.CLOSED) {
        ws.current.onclose = null
        ws.current.onmessage = null
        ws.current.onerror = null
        ws.current.close()
      }

      const isDev = import.meta.env.DEV
      const protocol = window.location.protocol === "https:" ? "wss:" : "ws:"
      const host = isDev ? "127.0.0.1:8000" : window.location.host
      const url = `${protocol}//${host}/api/ws/${chatId}?token=${token}`
      const socket = new WebSocket(url)
      ws.current = socket

      socket.onopen = () => {
        console.log("[WS] Connected to", chatId)
      }

      socket.onmessage = (event) => {
        const data = JSON.parse(event.data)
        switch (data.type) {
          case "message.new": {
            const msg = data.message as Message
            addMessageRef.current(msg)
            if (msg.sender_bot_id) {
              removeTypingBotRef.current(msg.sender_bot_id)
            }
            if (msg.sender_user_id) {
              removeTypingUserRef.current(msg.sender_user_id)
            }
            break
          }
          case "message.bot_typing":
            addTypingBotRef.current(data.bot_id, data.bot_name)
            break
          case "typing.indicator":
            if (data.user_id !== userId) {
              addTypingUserRef.current(data.user_id)
              setTimeout(() => {
                removeTypingUserRef.current(data.user_id)
              }, 3000)
            }
            break
          case "reaction.update":
            updateReactionsRef.current(data.message_id, data.reactions)
            break
          case "system.message":
            addMessageRef.current({
              id: `sys-${Date.now()}`,
              chat_id: chatId!,
              sender_user_id: null,
              sender_bot_id: null,
              sender_name: "System",
              sender_avatar: null,
              sender_type: "system" as any,
              content: data.content,
              mentions: [],
              reactions: [],
              reply_to: null,
              created_at: new Date().toISOString(),
            })
            break
          case "chat.cleared":
            setMessagesRef.current([])
            addMessageRef.current({
              id: `sys-${Date.now()}`,
              chat_id: chatId!,
              sender_user_id: null,
              sender_bot_id: null,
              sender_name: "System",
              sender_avatar: null,
              sender_type: "system" as any,
              content: `${data.by} cleared the chat.`,
              mentions: [],
              reactions: [],
              reply_to: null,
              created_at: new Date().toISOString(),
            })
            break
          case "chat.renamed": {
            const store = useChatStore.getState()
            const ac = store.activeChat
            useChatStore.setState({
              activeChat: ac && ac.id === data.chat_id ? { ...ac, name: data.name } : ac,
              chats: store.chats.map((c) =>
                c.id === data.chat_id ? { ...c, name: data.name } : c
              ),
            })
            break
          }
          case "chat.deleted":
            removeChatRef.current(data.chat_id)
            break
          case "member.added":
            addMemberRef.current(data.chat_id, data.member)
            break
          case "member.removed":
            removeMemberRef.current(data.chat_id, data.member_id)
            break
          case "ping":
            if (socket.readyState === WebSocket.OPEN) {
              socket.send(JSON.stringify({ type: "pong" }))
            }
            break
          case "error":
            console.error("[WS] Error:", data.detail)
            break
        }
      }

      socket.onclose = (e) => {
        console.log("[WS] Closed:", e.code, e.reason)
        if (!cancelled && ws.current === socket) {
          reconnectTimeout.current = setTimeout(connect, 3000)
        }
      }

      socket.onerror = () => {}
    }

    connect()

    return () => {
      cancelled = true
      clearTimeout(reconnectTimeout.current)
      if (ws.current) {
        ws.current.onclose = null
        ws.current.onmessage = null
        ws.current.onerror = null
        ws.current.close()
        ws.current = null
      }
    }
  }, [chatId, token, userId])

  const sendMessage = useCallback((content: string, mentions: string[] = [], replyToId?: string) => {
    if (ws.current?.readyState === WebSocket.OPEN) {
      ws.current.send(JSON.stringify({
        type: "message.send",
        content,
        mentions,
        reply_to_id: replyToId ?? null,
      }))
    }
  }, [])

  const sendTyping = useCallback(() => {
    if (ws.current?.readyState === WebSocket.OPEN) {
      ws.current.send(JSON.stringify({ type: "typing.start" }))
    }
  }, [])

  const sendReaction = useCallback((messageId: string, emoji: string) => {
    if (ws.current?.readyState === WebSocket.OPEN) {
      ws.current.send(JSON.stringify({
        type: "reaction.toggle",
        message_id: messageId,
        emoji,
      }))
    }
  }, [])

  return { sendMessage, sendTyping, sendReaction }
}
