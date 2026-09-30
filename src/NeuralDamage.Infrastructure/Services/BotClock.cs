using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// The time the bots see, in the chat's time zone (<c>App:TimeZone</c>, an
/// IANA id) rather than the server's, which in a container is UTC. Only the
/// bots' prompt uses it; stored timestamps and the conversation flow stay UTC.
/// </summary>
public sealed class BotClock(TimeZoneInfo zone)
{
    public const string DefaultTimeZone = "Europe/Zurich";

    public static readonly BotClock Default = new(Resolve(DefaultTimeZone));

    public TimeZoneInfo Zone { get; } = zone;

    public static BotClock FromConfiguration(IConfiguration configuration, ILogger? logger = null) =>
        new(Resolve(configuration["App:TimeZone"], logger));

    /// <summary>The zone for <paramref name="id"/>, or UTC with a warning when there is no such zone.</summary>
    public static TimeZoneInfo Resolve(string? id, ILogger? logger = null)
    {
        id = string.IsNullOrWhiteSpace(id) ? DefaultTimeZone : id.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger?.LogWarning("App:TimeZone {TimeZone} is not a known time zone; bots see UTC", id);
            return TimeZoneInfo.Utc;
        }
    }

    public DateTimeOffset Local(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone);

    /// <summary>"Wednesday afternoon": the day and part of day, never the minute.</summary>
    public string Describe(DateTimeOffset now)
    {
        var local = Local(now);
        return $"{local.DayOfWeek} {PartOfDay(local.Hour)}";
    }

    public static string PartOfDay(int hour) => hour switch
    {
        < 5 => "night",
        < 12 => "morning",
        < 17 => "afternoon",
        < 22 => "evening",
        _ => "night",
    };
}
