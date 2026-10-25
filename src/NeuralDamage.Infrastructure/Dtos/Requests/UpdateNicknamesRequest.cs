namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <param name="Nicknames">Comma-separated, or empty to clear them.</param>
public record UpdateNicknamesRequest(string? Nicknames);
