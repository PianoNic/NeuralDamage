namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <param name="Notes">What the bot always remembers; empty clears it.</param>
public record UpdateBotMemorySettingsRequest(bool Enabled, string? Notes);

/// <param name="Text">A new wording; null leaves it.</param>
/// <param name="Pinned">Kept for good and always used; null leaves it.</param>
public record UpdateBotMemoryRequest(string? Text, bool? Pinned);

public record AllowBotMemoryRequest(bool Allowed);
