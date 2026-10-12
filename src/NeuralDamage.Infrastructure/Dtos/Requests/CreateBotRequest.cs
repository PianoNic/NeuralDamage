namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <param name="IsPublic">Public bots are listed for everyone; a private bot only exists in <paramref name="ChatId"/>.</param>
/// <param name="ChatId">The chat the bot joins on creation. Required for a private bot.</param>
/// <param name="SystemPrompt">The character: the full instructions for how the bot talks and thinks.</param>
/// <param name="Personality">The short description: one line, at most 140 characters, saying who the bot is.</param>
public record CreateBotRequest(string Name, string ModelId, string SystemPrompt, string? Personality = null, double Temperature = 0.7, string? Aliases = null, bool IsPublic = true, Guid? ChatId = null);
