using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// What a person is called: their handle and the nicknames they set, which @mention them like a
/// bot's aliases mention the bot. Bots know people by what their characters call them ("Niclas"),
/// so a nickname is how "@Niclas" finds PianoNic.
/// </summary>
public static partial class PeopleNames
{
    public const int MaxNicknames = 5;
    public const int MaxNicknameLength = 32;

    /// <summary>The handle shown for a person: their display name, else their email.</summary>
    public static string Handle(User user) => user.DisplayName.Length > 0 ? user.DisplayName : user.Email;

    /// <summary>Comma-separated nicknames, trimmed, without blanks or duplicates.</summary>
    public static List<string> Split(string? nicknames) =>
        (nicknames ?? "").Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Nicknames as stored, or why they can't be: at most <see cref="MaxNicknames"/>, each up to
    /// <see cref="MaxNicknameLength"/> characters, letters, digits, spaces, dots, dashes and underscores.
    /// </summary>
    public static (string? Value, string? Error) Normalize(string? nicknames)
    {
        var names = Split(nicknames?.Replace("@", ""));
        if (names.Count > MaxNicknames)
            return (null, $"Up to {MaxNicknames} nicknames.");
        if (names.FirstOrDefault(n => n.Length > MaxNicknameLength) is { } tooLong)
            return (null, $"\"{tooLong}\" is longer than {MaxNicknameLength} characters.");
        if (names.FirstOrDefault(n => !Allowed().IsMatch(n)) is { } odd)
            return (null, $"\"{odd}\" can only have letters, digits, spaces, dots, dashes and underscores.");
        return (names.Count == 0 ? null : string.Join(", ", names), null);
    }

    /// <summary>"PianoNic", or "PianoNic (also called Niclas)" for a bot's list of who is in the chat.</summary>
    public static string ForPrompt(User user)
    {
        var nicknames = Split(user.Nicknames);
        return nicknames.Count == 0 ? Handle(user) : $"{Handle(user)} (also called {string.Join(", ", nicknames)})";
    }

    /// <summary>True when <paramref name="text"/> has "@" and one of the person's names, as a whole word.</summary>
    public static bool IsMentioned(string text, string handle, string? nicknames) =>
        new[] { handle }.Concat(Split(nicknames)).Any(name => name.Length > 0
            && Regex.IsMatch(text, $@"(?<!\w)@{Regex.Escape(name)}(?!\w)", RegexOptions.IgnoreCase));

    /// <summary>
    /// The people <paramref name="text"/> @mentions, by handle or nickname.
    /// </summary>
    /// <remarks>Matched in memory: a self-hosted install has a handful of people, and this keeps the match identical to the web app's.</remarks>
    public static async Task<List<Guid>> MentionedAsync(NeuralDamageDbContext db, string text, CancellationToken ct = default)
    {
        if (!text.Contains('@'))
            return [];
        var people = await db.Users.AsNoTracking().Select(u => new { u.Id, u.DisplayName, u.Email, u.Nicknames }).ToListAsync(ct);
        return people
            .Where(p => IsMentioned(text, p.DisplayName.Length > 0 ? p.DisplayName : p.Email, p.Nicknames))
            .Select(p => p.Id)
            .ToList();
    }

    [GeneratedRegex(@"^[\p{L}\p{N} ._\-']+$")]
    private static partial Regex Allowed();
}
