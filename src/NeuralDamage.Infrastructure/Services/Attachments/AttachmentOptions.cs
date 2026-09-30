using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services.Attachments;

/// <summary>Limits and storage for image uploads (<c>Attachments:*</c>).</summary>
public record AttachmentOptions
{
    public const string SectionName = "Attachments";

    /// <summary>Where the files go; relative paths resolve against the app's content root.</summary>
    public string Path { get; init; } = "data/attachments";

    public long MaxBytes { get; init; } = 10 * 1024 * 1024;

    public int MaxPerMessage { get; init; } = 4;

    /// <summary>
    /// How long a round waits for the describer before text-only bots answer
    /// with a bare "[image from alice]" instead.
    /// </summary>
    public TimeSpan DescriptionWait { get; init; } = TimeSpan.FromSeconds(20);

    public static AttachmentOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var defaults = new AttachmentOptions();
        return new AttachmentOptions
        {
            Path = string.IsNullOrWhiteSpace(section["Path"]) ? defaults.Path : section["Path"]!,
            MaxBytes = section.GetValue("MaxBytes", defaults.MaxBytes),
            MaxPerMessage = section.GetValue("MaxPerMessage", defaults.MaxPerMessage),
            DescriptionWait = TimeSpan.FromSeconds(section.GetValue("DescriptionWaitSeconds", defaults.DescriptionWait.TotalSeconds)),
        };
    }
}
