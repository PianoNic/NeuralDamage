using System.Text.Json;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public class Tier3LlmJudge(IOpenRouterService openRouter)
{
    private const string JudgeModel = "google/gemini-2.0-flash-001";

    public async Task<List<Guid>> JudgeAsync(Message message, List<(Bot Bot, double Tier2Score)> undecidedBots, List<ChatMessage> recentHistory, CancellationToken ct)
    {
        try
        {
            var botDescriptions = string.Join("\n", undecidedBots.Select(b =>
                $"- {b.Bot.Id}: \"{b.Bot.Name}\" (personality: {b.Bot.Personality ?? "general"}, score: {b.Tier2Score:F2})"));

            var historyText = string.Join("\n", recentHistory.TakeLast(10).Select(h => $"[{h.Role}]: {h.Content}"));

            var systemPrompt = """
                You decide which bots should respond to a chat message.
                Return ONLY a JSON object: {"responders": ["bot-id-1", "bot-id-2"]}
                Return an empty array if no bot should respond.
                Consider: relevance to each bot's personality, whether adding a response adds value, avoid pile-ons.
                """;

            var prompt = $"""
                Recent conversation:
                {historyText}

                New message: "{message.Content}"

                Candidate bots (with pre-computed relevance scores):
                {botDescriptions}

                Which of these bots should respond? Return JSON only.
                """;

            var response = await openRouter.GenerateResponseAsync(
                JudgeModel, 0.1, systemPrompt,
                [new ChatMessage("user", prompt)], ct);

            return ParseResponse(response, undecidedBots);
        }
        catch
        {
            // Fallback: all bots with Tier2 score > 0.4
            return undecidedBots.Where(b => b.Tier2Score > 0.4).Select(b => b.Bot.Id).ToList();
        }
    }

    private static List<Guid> ParseResponse(string response, List<(Bot Bot, double Tier2Score)> candidates)
    {
        try
        {
            // Strip markdown code blocks if present
            var json = response.Replace("```json", "").Replace("```", "").Trim();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("responders", out var responders))
                return [];

            var validIds = candidates.Select(c => c.Bot.Id).ToHashSet();
            var result = new List<Guid>();

            foreach (var item in responders.EnumerateArray())
            {
                if (Guid.TryParse(item.GetString(), out var id) && validIds.Contains(id))
                    result.Add(id);
            }

            return result;
        }
        catch
        {
            // Parse failure fallback
            return candidates.Where(b => b.Tier2Score > 0.4).Select(b => b.Bot.Id).ToList();
        }
    }
}
