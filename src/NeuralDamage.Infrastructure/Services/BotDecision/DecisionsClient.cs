using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

/// <summary>
/// The OpenRouter Decisions API (alpha), which answers typed questions about a
/// state with calibrated probabilities instead of generated text. It is not on
/// <c>/chat/completions</c>, so it gets its own plain HTTP client.
/// </summary>
public interface IDecisionsClient
{
    /// <summary>
    /// Returns the answers, or null when the API is unconfigured, unreachable,
    /// slow or returns an error and the caller should use its own heuristic.
    /// </summary>
    Task<DecisionsResponse?> DecideAsync(
        object state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken ct = default);
}

/// <param name="Criteria">
/// For a <c>choice</c> question, each option and what it means. Dictionary
/// keys are sent as written; only property names become snake_case.
/// </param>
public record DecisionQuestion(string Type, string Instructions, IReadOnlyDictionary<string, string>? Criteria = null);

public record DecisionsRequest(string Model, object State, IReadOnlyDictionary<string, DecisionQuestion> Questions);

public record DecisionsResponse(
    string? Id,
    string? Model,
    Dictionary<string, DecisionAnswer>? Answers,
    DecisionUsage? Usage);

public record DecisionAnswer(
    string? Type,
    double? Noul,
    string? Choice,
    double? Confidence,
    Dictionary<string, double>? Probabilities);

public record DecisionUsage(int InputTokens, int OutputTokens, decimal Cost);

public class DecisionsClient(HttpClient http, BotRankingOptions options, ILogger<DecisionsClient> logger) : IDecisionsClient
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<DecisionsResponse?> DecideAsync(
        object state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken ct = default)
    {
        if (options.ApiKey is null)
        {
            // Ranking is an optimization, so a missing key degrades decision
            // quality rather than taking the API down.
            logger.LogWarning("BotRanking has no API key (BotRanking:ApiKey or OpenRouter:ApiKey); falling back to mentions and replies only.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
            {
                Content = JsonContent.Create(new DecisionsRequest(options.Model, state, questions), options: JsonOptions),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Decisions API returned {Status}: {Body}; falling back to mentions and replies only.",
                    (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<DecisionsResponse>(JsonOptions, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Includes the HttpClient timeout, which surfaces as a cancellation.
            logger.LogWarning(ex, "Decisions API request failed; falling back to mentions and replies only.");
            return null;
        }
    }
}
