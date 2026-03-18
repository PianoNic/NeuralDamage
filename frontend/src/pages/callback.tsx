import { useEffect, useState } from "react"
import { useNavigate, useSearchParams } from "react-router-dom"
import { useAuthStore } from "@/stores/auth-store"
import { apiFetch } from "@/lib/api"
import type { User } from "@/types"

export function CallbackPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    // If already authenticated and no code in URL, just go home
    if (!searchParams.get("code")) {
      if (isAuthenticated || localStorage.getItem("token")) {
        navigate("/", { replace: true })
        return
      }
      setError("Missing authorization code")
      return
    }

    const code = searchParams.get("code")!
    const codeVerifier = sessionStorage.getItem("code_verifier")

    if (!codeVerifier) {
      // No verifier — maybe opened in a new tab or revisited from history
      if (isAuthenticated || localStorage.getItem("token")) {
        navigate("/", { replace: true })
        return
      }
      setError("Missing code verifier. Please try logging in again.")
      return
    }

    sessionStorage.removeItem("code_verifier")

    apiFetch<{ token: string; user: User }>("/api/auth/callback", {
      method: "POST",
      body: JSON.stringify({ code, code_verifier: codeVerifier }),
    })
      .then(({ token, user }) => {
        setAuth(token, user)
        navigate("/", { replace: true })
      })
      .catch((err) => {
        setError(err.message)
      })
  }, [searchParams, navigate, setAuth, isAuthenticated])

  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="text-center">
          <h2 className="text-xl font-semibold mb-2">Authentication Failed</h2>
          <p className="text-muted-foreground mb-4">{error}</p>
          <a href="/login" className="text-primary underline">Try again</a>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen flex items-center justify-center">
      <p className="text-muted-foreground">Signing you in...</p>
    </div>
  )
}
