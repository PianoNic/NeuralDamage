using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;

namespace NeuralDamage.Tests.Helpers;

/// <summary>
/// Stands in for the Decisions API: records every call and answers each bot
/// by name. Null from <c>answer</c> means Jev said nothing about that bot;
/// <see cref="Unavailable"/> makes every call fail as the real client does.
/// </summary>
public sealed class FakeJev(Func<string, DecisionAnswer?>? answer = null) : IDecisionsClient
{
    public List<(DecisionState State, IReadOnlyDictionary<string, DecisionQuestion> Questions)> Calls { get; } = [];
    public bool Unavailable { get; set; }

    /// <summary>The conversation health Jev gives each state; no health answer when unset or null.</summary>
    public Func<DecisionState, double?>? Health { get; set; }

    public Task<DecisionsResponse?> DecideAsync(object state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken ct = default)
    {
        var typed = (DecisionState)state;
        lock (Calls)
            Calls.Add((typed, questions));
        if (Unavailable || answer is null)
            return Task.FromResult<DecisionsResponse?>(null);

        var answers = new Dictionary<string, DecisionAnswer>();
        foreach (var (key, bot) in typed.Bots)
            if (answer(bot.Name) is { } a)
                answers[key] = a;
        if (Health?.Invoke(typed) is { } health)
            answers[BotDecisionEngine.HealthKey] = Scored(health);
        return Task.FromResult<DecisionsResponse?>(new DecisionsResponse("gen-test", "jev", answers, null));
    }

    /// <summary>
    /// Jev choosing <paramref name="choice"/> with probability <paramref name="p"/>.
    /// The rest goes to <c>quiet</c> (to <c>reply</c> when quiet is the choice),
    /// so it never adds up to a reaction of its own.
    /// </summary>
    public static DecisionAnswer Chose(string choice, double p)
    {
        var rest = choice == BotDecisionEngine.Quiet ? BotDecisionEngine.Reply : BotDecisionEngine.Quiet;
        var probabilities = BotDecisionEngine.Criteria.Keys.ToDictionary(k => k, k => k == choice ? p : k == rest ? 1 - p : 0);
        return new DecisionAnswer("choice", null, choice, p, probabilities);
    }

    /// <summary>Jev's answer with these probabilities; the likeliest option is the choice.</summary>
    public static DecisionAnswer With(Dictionary<string, double> probabilities)
    {
        var top = probabilities.MaxBy(kv => kv.Value);
        return new DecisionAnswer("choice", null, top.Key, top.Value, probabilities);
    }

    /// <summary>A health answer with expected level <paramref name="score"/>.</summary>
    public static DecisionAnswer Scored(double score) =>
        new("score", null, null, 0.9, null, score, BotDecisionEngine.HealthLevels.Select((l, i) => (l, i)).ToDictionary(x => x.i.ToString(), x => x.l));

    /// <summary>Registers the real decision engine over this fake, for <see cref="OrchestratorHarness"/>.</summary>
    public Action<IServiceCollection> Engine(BotRankingOptions? ranking = null, ILogger<BotDecisionEngine>? logger = null) => services =>
        services.AddScoped<IBotDecisionEngine>(sp => new BotDecisionEngine(
            sp.GetRequiredService<NeuralDamageDbContext>(),
            this,
            ranking ?? new BotRankingOptions(),
            logger ?? NullLogger<BotDecisionEngine>.Instance,
            sp.GetService<IOptions<BotBehaviorOptions>>()));
}
