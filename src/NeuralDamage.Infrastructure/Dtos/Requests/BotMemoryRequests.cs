namespace NeuralDamage.Infrastructure.Dtos.Requests;

public record UpdateBotMemorySettingsRequest(bool Enabled);

/// <param name="Text">What the bot should remember, in plain words; the memory model turns it into memories.</param>
public record TeachBotMemoryRequest(string Text);

/// <param name="Text">A new wording; null leaves it.</param>
/// <param name="Pinned">Kept for good and always used; null leaves it.</param>
public record UpdateBotMemoryRequest(string? Text, bool? Pinned);

public record AllowBotMemoryRequest(bool Allowed);

public record AllowBotDirectMessagesRequest(bool Allowed);
