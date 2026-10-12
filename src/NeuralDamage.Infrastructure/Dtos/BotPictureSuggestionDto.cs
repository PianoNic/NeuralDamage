namespace NeuralDamage.Infrastructure.Dtos;

/// <param name="Index">Which suggestion this is; the picture loads from the suggestion image endpoint by it.</param>
/// <param name="Title">The article or file the picture comes from.</param>
/// <param name="SourceUrl">Its page on Wikipedia or Wikimedia Commons.</param>
public record BotPictureSuggestionDto(int Index, string Title, string SourceUrl);
