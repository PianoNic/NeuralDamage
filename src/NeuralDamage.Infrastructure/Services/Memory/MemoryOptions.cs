using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services.Memory;

/// <summary>How bots remember (<c>Memory:*</c>).</summary>
public class MemoryOptions
{
    public const string SectionName = "Memory";

    /// <summary>Off turns memory off everywhere: nothing is learned or read.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The model that picks facts out of a chat; falls back to <c>History:SummaryModel</c>, then <c>App:DefaultModel</c>.</summary>
    public string? Model { get; set; }

    /// <summary>A bot's memory reads a chat again once it has written this many messages there.</summary>
    public int ExtractEvery { get; set; } = 8;

    /// <summary>The most messages one extraction reads.</summary>
    public int ExtractWindow { get; set; } = 60;

    /// <summary>The most extractions one bot runs in a day, to cap the cost.</summary>
    public int MaxExtractionsPerBotPerDay { get; set; } = 40;

    /// <summary>The most memories a bot keeps; the nightly sweep drops the least useful beyond it, never pinned ones.</summary>
    public int MaxPerBot { get; set; } = 400;

    /// <summary>The most memories that go into one prompt.</summary>
    public int MaxRecalled { get; set; } = 12;

    /// <summary>The most characters of memory in one prompt, about 600 tokens.</summary>
    public int MaxPromptChars { get; set; } = 2400;

    public static MemoryOptions FromConfiguration(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<MemoryOptions>() ?? new MemoryOptions();
}
