using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Tests.Helpers;

public static class InstantBotOptions
{
    /// <summary>Real limits, no waiting: every read, typing and reaction delay is zero.</summary>
    public static IOptions<BotBehaviorOptions> Create() => Options.Create(new BotBehaviorOptions
    {
        ReadDelayMin = TimeSpan.Zero,
        ReadDelayMax = TimeSpan.Zero,
        TypingMin = TimeSpan.Zero,
        TypingMax = TimeSpan.Zero,
        TypingHeartbeat = TimeSpan.Zero,
        ReactionDelayMin = TimeSpan.Zero,
        ReactionDelayMax = TimeSpan.Zero,
    });
}
