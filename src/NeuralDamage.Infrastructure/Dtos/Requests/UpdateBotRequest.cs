namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <param name="SystemPrompt">The character: the full instructions for how the bot talks and thinks.</param>
/// <param name="Personality">The short description: one line, at most 140 characters, saying who the bot is.</param>
public record UpdateBotRequest(string? Name = null, string? ModelId = null, string? SystemPrompt = null, string? Personality = null, double? Temperature = null, string? Aliases = null, bool? IsActive = null, bool? FeedEnabled = null, int? MaxPostsPerDay = null);
