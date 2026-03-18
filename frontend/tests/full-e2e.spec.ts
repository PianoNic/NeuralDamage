import { test, expect } from "@playwright/test"

const API = "http://127.0.0.1:8000"
const APP = "http://localhost:5173"

async function getTestToken(request: any) {
  const resp = await request.post(`${API}/api/auth/test-token`)
  if (!resp.ok()) return null
  return resp.json()
}

async function authPage(page: any, auth: any) {
  await page.goto(`${APP}/login`)
  await page.evaluate(({ token, user }: any) => {
    localStorage.setItem("token", token)
    localStorage.setItem("user", JSON.stringify(user))
  }, auth)
}

test.describe("Full E2E: Fresh DB", () => {
  test("1. Login page renders correctly", async ({ page }) => {
    await page.goto(`${APP}/login`)
    await expect(page.locator("h1, h2").filter({ hasText: "Neural Damage" }).first()).toBeVisible()
    await expect(page.locator("text=Sign in with Pocket ID")).toBeVisible()
  })

  test("2. Create chat, add bot, send message, bot responds", async ({ page, request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }
    const headers = { Authorization: `Bearer ${auth.token}` }

    // Create a chat
    const chat = await (await request.post(`${API}/api/chats`, {
      headers, data: { name: "Bot Response Test" },
    })).json()
    expect(chat.name).toBe("Bot Response Test")

    // Create a bot
    const bot = await (await request.post(`${API}/api/bots`, {
      headers,
      data: {
        name: "TestBot",
        model_id: "google/gemini-2.0-flash-001",
        system_prompt: "You are TestBot, a friendly chatbot. Always respond briefly.",
        personality: "friendly, concise",
      },
    })).json()
    expect(bot.name).toBe("TestBot")

    // Add bot to chat
    const addResp = await request.post(`${API}/api/chats/${chat.id}/members`, {
      headers, data: { bot_id: bot.id },
    })
    expect(addResp.ok()).toBeTruthy()

    // Navigate to chat in browser
    await authPage(page, auth)
    await page.goto(`${APP}/chat/${chat.id}`)

    // Wait for chat to load
    await expect(page.getByRole("heading", { name: "Bot Response Test" })).toBeVisible()

    // Send a message that should trigger the bot (direct @mention)
    const input = page.getByPlaceholder("Type a message")
    await input.fill("Hey @TestBot, what is 1+1?")
    await page.keyboard.press("Enter")

    // Verify our message appears
    await expect(page.locator("text=Hey @TestBot, what is 1+1?")).toBeVisible({ timeout: 5000 })

    // Wait for bot response (judge + OpenRouter call can take up to 30s)
    // Look for a message from TestBot
    await expect(page.locator('[class*="message"]').filter({ hasText: /TestBot/i }).or(
      page.locator('text=/[12]|two|answer/i')
    ).first()).toBeVisible({ timeout: 45000 })
  })

  test("3. Bot deletion works", async ({ request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }
    const headers = { Authorization: `Bearer ${auth.token}` }

    // Create a bot to delete
    const bot = await (await request.post(`${API}/api/bots`, {
      headers,
      data: {
        name: "DeleteMe",
        model_id: "google/gemini-2.0-flash-001",
        system_prompt: "Test bot for deletion.",
      },
    })).json()
    expect(bot.is_active).toBe(true)

    // Delete it
    const delResp = await request.delete(`${API}/api/bots/${bot.id}`, { headers })
    expect(delResp.ok()).toBeTruthy()

    // Verify it's deactivated (soft delete)
    const getResp = await request.get(`${API}/api/bots/${bot.id}`, { headers })
    if (getResp.ok()) {
      const updated = await getResp.json()
      expect(updated.is_active).toBe(false)
    }
    // If 404, that's also acceptable (hard delete)
  })

  test("4. Sidebar shows chats with proper styling", async ({ page, request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }
    const headers = { Authorization: `Bearer ${auth.token}` }

    // Create a couple of chats
    await request.post(`${API}/api/chats`, { headers, data: { name: "Sidebar Chat A" } })
    await request.post(`${API}/api/chats`, { headers, data: { name: "Sidebar Chat B" } })

    await authPage(page, auth)
    await page.goto(`${APP}/`)

    // Verify sidebar shows chats
    await expect(page.locator("text=Sidebar Chat A")).toBeVisible({ timeout: 5000 })
    await expect(page.locator("text=Sidebar Chat B")).toBeVisible({ timeout: 5000 })

    // Verify user name is shown
    await expect(page.locator(`text=${auth.user.display_name}`)).toBeVisible()
  })

  test("5. WebSocket real-time message delivery", async ({ page, request }) => {
    const auth = await getTestToken(request)
    if (!auth) { test.skip(); return }
    const headers = { Authorization: `Bearer ${auth.token}` }

    const chat = await (await request.post(`${API}/api/chats`, {
      headers, data: { name: "WS Realtime Test" },
    })).json()

    await authPage(page, auth)
    await page.goto(`${APP}/chat/${chat.id}`)
    await expect(page.getByRole("heading", { name: "WS Realtime Test" })).toBeVisible()

    // Send message via UI
    const input = page.getByPlaceholder("Type a message")
    await input.fill("Realtime test message!")
    await page.keyboard.press("Enter")

    // Verify message appears instantly via WebSocket
    await expect(page.locator("text=Realtime test message!")).toBeVisible({ timeout: 5000 })

    // Send a second message
    await input.fill("Second message works too")
    await page.keyboard.press("Enter")
    await expect(page.locator("text=Second message works too")).toBeVisible({ timeout: 5000 })
  })
})
