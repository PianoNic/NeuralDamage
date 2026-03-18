import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { generateCodeVerifier, generateCodeChallenge } from "@/lib/auth"
import { apiFetch } from "@/lib/api"

export function LoginPage() {
  const [loading, setLoading] = useState(false)

  const handleLogin = async () => {
    setLoading(true)
    try {
      const codeVerifier = generateCodeVerifier()
      const codeChallenge = await generateCodeChallenge(codeVerifier)

      // Store verifier for callback
      sessionStorage.setItem("code_verifier", codeVerifier)

      const { authorize_url } = await apiFetch<{ authorize_url: string }>("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({
          code_challenge: codeChallenge,
          state: crypto.randomUUID(),
        }),
      })

      window.location.href = authorize_url
    } catch (err) {
      console.error("Login failed:", err)
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background">
      <Card className="w-full max-w-[400px] mx-4">
        <CardHeader className="text-center">
          <CardTitle className="text-2xl">Neural Damage</CardTitle>
          <CardDescription>Group chat with AI bots that feel natural</CardDescription>
        </CardHeader>
        <CardContent>
          <Button onClick={handleLogin} disabled={loading} className="w-full" size="lg">
            {loading ? "Redirecting..." : "Sign in with Pocket ID"}
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}
