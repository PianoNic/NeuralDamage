import { useEffect, useState } from "react"
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom"
import { Toaster } from "sonner"
import { useAuthStore } from "@/stores/auth-store"
import { AppShell } from "@/components/layout/app-shell"
import { LoginPage } from "@/pages/login"
import { CallbackPage } from "@/pages/callback"
import { HomePage } from "@/pages/home"
import { ChatView } from "@/components/chat/chat-view"

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  if (!isAuthenticated) return <Navigate to="/login" replace />
  return <>{children}</>
}

export default function App() {
  const loadFromStorage = useAuthStore((s) => s.loadFromStorage)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    loadFromStorage()
    setReady(true)
  }, [loadFromStorage])

  // Don't render routes until we've loaded auth from storage,
  // otherwise ProtectedRoute flashes a redirect to /login
  if (!ready) return null

  return (
    <BrowserRouter>
      <Toaster position="bottom-right" richColors closeButton />
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/callback" element={<CallbackPage />} />
        <Route
          path="/"
          element={
            <ProtectedRoute>
              <AppShell />
            </ProtectedRoute>
          }
        >
          <Route index element={<HomePage />} />
          <Route path="chat/:chatId" element={<ChatView />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
