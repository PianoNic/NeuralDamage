using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// The people who may do anything any owner can, to anyone's bots, chats, posts,
/// stories and stickers: those listed in <c>App:Admins</c>, a comma-separated list
/// of sign-in emails, and those whose sign-in token carries the role
/// <c>App:AdminRole</c> (<see cref="DefaultRole"/> unless set).
/// </summary>
public sealed class AppAdmins(IEnumerable<string> emails, string? role = null)
{
    public const string DefaultRole = "private";

    /// <summary>
    /// Where providers put a person's roles: the mapped role claim, the plain
    /// <c>role</c> and <c>roles</c> claims, and <c>groups</c>.
    /// </summary>
    public static readonly string[] RoleClaimTypes = [ClaimTypes.Role, "role", "roles", "groups"];

    private readonly HashSet<string> _emails = emails
        .Select(e => e.Trim())
        .Where(e => e.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static readonly AppAdmins None = new([]);

    /// <summary>The role that makes its holder an admin.</summary>
    public string Role { get; } = string.IsNullOrWhiteSpace(role) ? DefaultRole : role.Trim();

    public bool IsAdmin(string? email) => email is not null && _emails.Contains(email.Trim());

    /// <summary>Whether the signed-in person is an admin, by their email or by a role their token carries.</summary>
    public bool IsAdmin(ClaimsPrincipal? principal, string? email) => IsAdmin(email) || HasAdminRole(principal);

    public bool HasAdminRole(ClaimsPrincipal? principal) =>
        RolesOf(principal).Contains(Role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every role the principal carries. A claim is one role, or, when a provider
    /// hands the whole list over as one value, a JSON array of them, or a JSON
    /// object keyed by them.
    /// </summary>
    public static IEnumerable<string> RolesOf(ClaimsPrincipal? principal)
    {
        if (principal is null)
            return [];

        var types = RoleClaimTypes
            .Concat(principal.Identities.Select(i => i.RoleClaimType))
            .ToHashSet(StringComparer.Ordinal);
        return principal.Claims
            .Where(c => types.Contains(c.Type))
            .SelectMany(c => Split(c.Value))
            .Select(r => r.Trim())
            .Where(r => r.Length > 0);
    }

    private static IEnumerable<string> Split(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(trimmed);
                return json.RootElement.ValueKind switch
                {
                    JsonValueKind.Array => json.RootElement.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .ToList(),
                    JsonValueKind.Object => json.RootElement.EnumerateObject().Select(p => p.Name).ToList(),
                    _ => [trimmed],
                };
            }
            catch (JsonException)
            {
            }
        }
        return [trimmed];
    }

    public static AppAdmins FromConfiguration(IConfiguration configuration) =>
        new((configuration["App:Admins"] ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries),
            configuration["App:AdminRole"]);
}
