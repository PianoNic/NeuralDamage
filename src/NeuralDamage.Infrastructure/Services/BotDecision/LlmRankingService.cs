using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using AgentChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

/// <summary>
/// Ranks bot responders through an OpenAI-wire-compatible endpoint.
/// Endpoint, model and key all come from configuration; there are no
/// defaults, so nothing about the deployment target lives in the source.
/// </summary>
public class LlmRankingService : IBotRankingService
{
    private readonly ILogger<LlmRankingService> _logger;
    private readonly AIAgent? _agent;

    public LlmRankingService(IConfiguration configuration, ILogger<LlmRankingService> logger)
    {
        _logger = logger;

        var endpoint = configuration["BotRanking:Endpoint"];
        var apiKey = configuration["BotRanking:ApiKey"];
        var model = configuration["BotRanking:Model"];

        if (string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(model))
        {
            // Ranking is an optimization, so an unconfigured endpoint degrades
            // decision quality rather than taking the API down.
            _logger.LogWarning(
                "BotRanking is not fully configured (Endpoint, ApiKey and Model are all required). "
                + "Bot response ranking will fall back to Tier 2 scores.");
            return;
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        _agent = client.GetChatClient(model).AsIChatClient().AsAIAgent();
    }

    public async Task<string?> RankAsync(string systemPrompt, string prompt, CancellationToken ct = default)
    {
        if (_agent is null)
            return null;

        try
        {
            var messages = new List<AgentChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, prompt),
            };

            var options = new ChatClientAgentRunOptions(new ChatOptions
            {
                Temperature = 0.1f,
                MaxOutputTokens = 256,
            });

            var response = await _agent.RunAsync(messages, options: options, cancellationToken: ct);
            return response.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bot ranking request failed; falling back to Tier 2 scores.");
            return null;
        }
    }
}
