using System.Text.Json;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using Microsoft.Extensions.Logging;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public class Tier3LlmJudge(IBotRankingService ranking, ILogger<Tier3LlmJudge> logger)
{

    public async Task<List<Guid>> JudgeAsync(Message message, List<(Bot Bot, double Tier2Score)> undecidedBots, List<ChatMessage> recentHistory, CancellationToken ct)
    {
        try
        {
            var botDescriptions = string.Join("\n", undecidedBots.Select(b =>
                $"- {b.Bot.Id}: \"{b.Bot.Name}\" (personality: {b.Bot.Personality ?? "general"}, score: {b.Tier2Score:F2})"));

            var historyText = string.Join("\n", recentHistory.TakeLast(10).Select(h => $"[{h.Role}]: {h.Content}"));

            var systemPrompt = """
                You decide which bots should respond to a chat message in a group chat.
                Return ONLY a JSON object: {"responders": ["bot-id-1", "bot-id-2"]}

                Default to letting a bot respond. People expect a reply when they say
                something to the room, so silence should be the exception, not the norm.
                Pick a bot when the message is a question, is addressed at the room, or
                continues a thread that bot was already part of.

                Return an empty array only when replying would clearly be noise: the
                message is chatter between two other people, or the same bot has just
                spoken and has nothing to add. With several strong candidates prefer the
                one or two best fits by personality rather than every bot at once.
                """;

            var prompt = $"""
                Recent conversation:
                {historyText}

                New message: "{message.Content}"

                Candidate bots (with pre-computed relevance scores):
                {botDescriptions}

                Which of these bots should respond? Return JSON only.
                """;

            var response = await ranking.RankAsync(systemPrompt, prompt, ct);
            logger.LogInformation("Ranking reply for candidates [{Ids}]: {Reply}",
                string.Join(", ", undecidedBots.Select(b => b.Bot.Id)), response ?? "<null>");

            // Ranking unavailable - fall back to the Tier 2 scores.
            if (response is null)
                return FallbackToTier2(undecidedBots);

            return ParseResponse(response, undecidedBots);
        }
        catch
        {
            return FallbackToTier2(undecidedBots);
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
            return FallbackToTier2(candidates);
        }
    }

    private static List<Guid> FallbackToTier2(List<(Bot Bot, double Tier2Score)> candidates)
        => candidates.Where(b => b.Tier2Score > 0.4).Select(b => b.Bot.Id).ToList();
}
