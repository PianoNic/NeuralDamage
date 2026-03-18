import { create } from "zustand"
import type { User } from "@/types"

interface AuthState {
  user: User | null
  token: string | null
  isAuthenticated: boolean
  setAuth: (token: string, user: User) => void
  logout: () => void
  loadFromStorage: () => void
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  token: null,
  isAuthenticated: false,

  setAuth: (token, user) => {
    localStorage.setItem("token", token)
    localStorage.setItem("user", JSON.stringify(user))
    set({ token, user, isAuthenticated: true })
  },

  logout: () => {
    localStorage.removeItem("token")
    localStorage.removeItem("user")
    set({ token: null, user: null, isAuthenticated: false })
  },

  loadFromStorage: () => {
    const token = localStorage.getItem("token")
    const userStr = localStorage.getItem("user")
    if (token && userStr) {
      try {
        const user = JSON.parse(userStr) as User
        set({ token, user, isAuthenticated: true })
      } catch {
        localStorage.removeItem("token")
        localStorage.removeItem("user")
      }
    }
  },
}))

// Sync auth state across tabs via storage events
if (typeof window !== "undefined") {
  window.addEventListener("storage", (e) => {
    if (e.key === "token") {
      if (!e.newValue) {
        // Logged out in another tab
        useAuthStore.getState().logout()
      } else {
        // Logged in from another tab
        useAuthStore.getState().loadFromStorage()
      }
    }
  })
}
