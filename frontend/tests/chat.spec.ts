import { test, expect } from "@playwright/test"

const API = "http://127.0.0.1:8000"
const APP = "http://localhost:5173"

async function getTestToken(request: any) {
  const resp = await request.post(`${API}/api/auth/test-token`)
  if (!resp.ok()) return null
  return resp.json()
}

test.describe("Neural Damage Chat", () => {
  test("login page renders", async ({ page }) => {
    await page.goto(`${APP}/login`)
    await expect(page.locator("text=Neural Damage")).toBeVisible()
    await expect(page.locator("text=Sign in with Pocket ID")).toBeVisible()
  })

  test("unauthenticated user redirects to login", async ({ page }) => {
    await page.goto(`${APP}/`)
    await page.waitForURL("**/login")
    await expect(page.locator("text=Sign in with Pocket ID")).toBeVisible()
  })

  test("authenticated user sees sidebar and home", async ({ page, request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }

    await page.goto(`${APP}/login`)
    await page.evaluate(({ token, user }) => {
      localStorage.setItem("token", token)
      localStorage.setItem("user", JSON.stringify(user))
    }, auth)
    await page.goto(`${APP}/`)

    await expect(page.locator("h1:has-text('Neural Damage')")).toBeVisible()
    await expect(page.locator(`text=${auth.user.display_name}`)).toBeVisible()
  })

  test("health check", async ({ request }) => {
    const resp = await request.get(`${API}/api/health`)
    expect(resp.ok()).toBeTruthy()
    expect((await resp.json()).status).toBe("ok")
  })

  test("API: list chats requires auth", async ({ request }) => {
    const resp = await request.get(`${API}/api/chats`)
    expect(resp.status()).toBeGreaterThanOrEqual(400)
  })

  test("API: create chat flow", async ({ request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }
    const headers = { Authorization: `Bearer ${auth.token}` }

    const chatResp = await request.post(`${API}/api/chats`, {
      headers, data: { name: "Playwright Test Chat" },
    })
    expect(chatResp.ok()).toBeTruthy()
    const chat = await chatResp.json()
    expect(chat.name).toBe("Playwright Test Chat")
    expect(chat.members.length).toBe(1)

    const msgsResp = await request.get(`${API}/api/chats/${chat.id}/messages`, { headers })
    expect(msgsResp.ok()).toBeTruthy()
    expect((await msgsResp.json()).length).toBe(0)

    const listResp = await request.get(`${API}/api/chats`, { headers })
    expect(listResp.ok()).toBeTruthy()
    expect((await listResp.json()).some((c: any) => c.id === chat.id)).toBeTruthy()
  })

  test("API: bot CRUD and model listing", async ({ request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }
    const headers = { Authorization: `Bearer ${auth.token}` }

    const modelsResp = await request.get(`${API}/api/bots/models`, { headers })
    expect(modelsResp.ok()).toBeTruthy()
    expect((await modelsResp.json()).length).toBeGreaterThan(0)

    const botResp = await request.post(`${API}/api/bots`, {
      headers,
      data: {
        name: "PlaywrightBot",
        model_id: "google/gemini-2.0-flash-001",
        system_prompt: "You are a test bot.",
      },
    })
    expect(botResp.ok()).toBeTruthy()
    const bot = await botResp.json()
    expect(bot.name).toBe("PlaywrightBot")
    expect(bot.is_active).toBe(true)
  })

  test("API: add bot to chat and verify members", async ({ request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }
    const headers = { Authorization: `Bearer ${auth.token}` }

    const chat = await (await request.post(`${API}/api/chats`, {
      headers, data: { name: "Bot Test Chat" },
    })).json()

    const bot = await (await request.post(`${API}/api/bots`, {
      headers,
      data: { name: "MemberBot", model_id: "google/gemini-2.0-flash-001", system_prompt: "Test." },
    })).json()

    const addResp = await request.post(`${API}/api/chats/${chat.id}/members`, {
      headers, data: { bot_id: bot.id },
    })
    expect(addResp.ok()).toBeTruthy()
    expect((await addResp.json()).member_type).toBe("bot")

    const detail = await (await request.get(`${API}/api/chats/${chat.id}`, { headers })).json()
    expect(detail.members.length).toBe(2)
    expect(detail.members.some((m: any) => m.member_type === "bot")).toBeTruthy()
    expect(detail.members.some((m: any) => m.member_type === "user")).toBeTruthy()
  })

  test("E2E: send message and see it in chat", async ({ page, request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }
    const headers = { Authorization: `Bearer ${auth.token}` }

    // Create a fresh chat
    const chat = await (await request.post(`${API}/api/chats`, {
      headers, data: { name: "E2E Message Test" },
    })).json()

    // Inject auth and navigate to chat
    await page.goto(`${APP}/login`)
    await page.evaluate(({ token, user }) => {
      localStorage.setItem("token", token)
      localStorage.setItem("user", JSON.stringify(user))
    }, auth)
    await page.goto(`${APP}/chat/${chat.id}`)

    // Wait for chat to load
    await expect(page.getByRole("heading", { name: "E2E Message Test" })).toBeVisible()
    await expect(page.locator("text=No messages yet")).toBeVisible()

    // Type and send a message
    const input = page.getByPlaceholder("Type a message")
    await input.fill("Hello from Playwright!")
    await page.keyboard.press("Enter")

    // Verify message appears
    await expect(page.locator("text=Hello from Playwright!")).toBeVisible({ timeout: 10000 })
  })
})
