using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// The people who may tidy up what anyone shared app-wide, such as stickers:
/// <c>App:Admins</c>, a comma-separated list of sign-in emails.
/// </summary>
public sealed class AppAdmins(IEnumerable<string> emails)
{
    private readonly HashSet<string> _emails = emails
        .Select(e => e.Trim())
        .Where(e => e.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static readonly AppAdmins None = new([]);

    public bool IsAdmin(string? email) => email is not null && _emails.Contains(email.Trim());

    public static AppAdmins FromConfiguration(IConfiguration configuration) =>
        new((configuration["App:Admins"] ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries));
}
