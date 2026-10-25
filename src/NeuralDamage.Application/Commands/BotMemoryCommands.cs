using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Memory;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Who may read which memory. A bot's maker manages its memory, but what it learned about
/// someone else in a chat the maker isn't in stays unread: memories about the bot itself,
/// about the reader, or from chats the reader is in. Anyone reads what any bot remembers about them.
/// </summary>
internal static class MemoryAccess
{
    public static async Task<HashSet<Guid>> ChatsOfAsync(NeuralDamageDbContext db, Guid userId, CancellationToken ct) =>
        (await db.ChatMembers.Where(cm => cm.UserId == userId).Select(cm => cm.ChatId).ToListAsync(ct)).ToHashSet();

    public static bool CanRead(BotMemory memory, Guid userId, IReadOnlySet<Guid> chats) =>
        memory.SubjectUserId == userId
        || (memory.ChatId is { } chatId ? chats.Contains(chatId) : memory.Scope == MemoryScope.Self);

    public static async Task<List<BotMemoryDto>> ToDtosAsync(NeuralDamageDbContext db, IEnumerable<BotMemory> memories, IReadOnlySet<Guid> chats, CancellationToken ct)
    {
        var list = memories.ToList();
        var chatIds = list.Where(m => m.ChatId is { } id && chats.Contains(id)).Select(m => m.ChatId!.Value).Distinct().ToList();
        var names = await db.Chats.Where(c => chatIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return list
            .Select(m => new BotMemoryDto(
                m.Id, m.BotId, m.Bot.Name, m.Bot.AvatarUrl, m.Bot.ModelId,
                m.Scope.ToString(), m.Text, m.Importance, m.Source.ToString(), m.Pinned,
                m.SubjectUserId, m.SubjectUser is { } subject ? PeopleNames.Handle(subject) : null,
                m.ChatId, m.ChatId is { } id ? names.GetValueOrDefault(id) : null,
                m.StaysInChat, m.CreatedAt, m.LastUsedAt))
            .ToList();
    }
}

/// <summary>A bot's memory, for its maker or an admin.</summary>
public record GetBotMemoriesQuery(Guid BotId, Guid RequestingUserId) : IQuery<Result<BotMemoriesDto>>;

public class GetBotMemoriesHandler(NeuralDamageDbContext db) : IQueryHandler<GetBotMemoriesQuery, Result<BotMemoriesDto>>
{
    public async ValueTask<Result<BotMemoriesDto>> Handle(GetBotMemoriesQuery request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BotId && b.IsActive, cancellationToken);
        if (bot is null)
            return Result.Failure<BotMemoriesDto>("Bot not found.");
        if (!await Admins.RunsBotAsync(db, bot, request.RequestingUserId, cancellationToken))
            return Result.Failure<BotMemoriesDto>("Only the bot's maker can see its memory.");

        var chats = await MemoryAccess.ChatsOfAsync(db, request.RequestingUserId, cancellationToken);
        var all = await db.BotMemories
            .Where(m => m.BotId == bot.Id)
            .Include(m => m.Bot)
            .Include(m => m.SubjectUser)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var readable = all.Where(m => MemoryAccess.CanRead(m, request.RequestingUserId, chats))
            .OrderByDescending(m => m.Pinned).ThenByDescending(m => m.CreatedAt);
        var dtos = await MemoryAccess.ToDtosAsync(db, readable, chats, cancellationToken);
        return Result.Success(new BotMemoriesDto(bot.MemoryEnabled, bot.MemoryNotes, dtos, all.Count - dtos.Count));
    }
}

/// <summary>Turns a bot's memory on or off and sets the notes it always keeps.</summary>
public record UpdateBotMemorySettingsCommand(Guid BotId, Guid RequestingUserId, bool Enabled, string? Notes) : ICommand<Result>;

public class UpdateBotMemorySettingsHandler(NeuralDamageDbContext db) : ICommandHandler<UpdateBotMemorySettingsCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateBotMemorySettingsCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == request.BotId && b.IsActive, cancellationToken);
        if (bot is null)
            return Result.Failure("Bot not found.");
        if (!await Admins.RunsBotAsync(db, bot, request.RequestingUserId, cancellationToken))
            return Result.Failure("Only the bot's maker can change its memory.");
        var notes = request.Notes?.Trim();
        if (notes is { Length: > Bot.MaxMemoryNotesLength })
            return Result.Failure($"The notes can be at most {Bot.MaxMemoryNotesLength} characters.");

        bot.MemoryEnabled = request.Enabled;
        bot.MemoryNotes = string.IsNullOrEmpty(notes) ? null : notes;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Rewords or pins one memory: the bot's maker, or the person it is about.</summary>
public record UpdateBotMemoryCommand(Guid BotId, Guid MemoryId, Guid RequestingUserId, string? Text, bool? Pinned) : ICommand<Result<BotMemoryDto>>;

public class UpdateBotMemoryHandler(NeuralDamageDbContext db) : ICommandHandler<UpdateBotMemoryCommand, Result<BotMemoryDto>>
{
    public async ValueTask<Result<BotMemoryDto>> Handle(UpdateBotMemoryCommand request, CancellationToken cancellationToken)
    {
        var memory = await db.BotMemories.Include(m => m.Bot).Include(m => m.SubjectUser)
            .FirstOrDefaultAsync(m => m.Id == request.MemoryId && m.BotId == request.BotId, cancellationToken);
        if (memory is null)
            return Result.Failure<BotMemoryDto>("That memory is gone.");
        var chats = await MemoryAccess.ChatsOfAsync(db, request.RequestingUserId, cancellationToken);
        var maker = await Admins.RunsBotAsync(db, memory.Bot, request.RequestingUserId, cancellationToken);
        if (!(maker && MemoryAccess.CanRead(memory, request.RequestingUserId, chats)) && memory.SubjectUserId != request.RequestingUserId)
            return Result.Failure<BotMemoryDto>("You can't change that memory.");

        if (request.Text is { } text)
        {
            var clean = BotMemories.Clean(text);
            if (clean.Length == 0)
                return Result.Failure<BotMemoryDto>("Write what the bot should remember.");
            memory.Text = clean;
        }
        if (request.Pinned is { } pinned)
            memory.Pinned = pinned;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success((await MemoryAccess.ToDtosAsync(db, [memory], chats, cancellationToken))[0]);
    }
}

/// <summary>Forgets one memory: the bot's maker, or the person it is about.</summary>
public record DeleteBotMemoryCommand(Guid MemoryId, Guid RequestingUserId) : ICommand<Result>;

public class DeleteBotMemoryHandler(NeuralDamageDbContext db) : ICommandHandler<DeleteBotMemoryCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteBotMemoryCommand request, CancellationToken cancellationToken)
    {
        var memory = await db.BotMemories.Include(m => m.Bot).FirstOrDefaultAsync(m => m.Id == request.MemoryId, cancellationToken);
        if (memory is null)
            return Result.Success();
        var mine = memory.SubjectUserId == request.RequestingUserId;
        if (!mine)
        {
            var chats = await MemoryAccess.ChatsOfAsync(db, request.RequestingUserId, cancellationToken);
            if (!await Admins.RunsBotAsync(db, memory.Bot, request.RequestingUserId, cancellationToken) || !MemoryAccess.CanRead(memory, request.RequestingUserId, chats))
                return Result.Failure("You can't delete that memory.");
        }
        db.BotMemories.Remove(memory);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Wipes a bot's memory, all of it, notes aside: its maker only.</summary>
public record ClearBotMemoryCommand(Guid BotId, Guid RequestingUserId) : ICommand<Result<int>>;

public class ClearBotMemoryHandler(NeuralDamageDbContext db) : ICommandHandler<ClearBotMemoryCommand, Result<int>>
{
    public async ValueTask<Result<int>> Handle(ClearBotMemoryCommand request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BotId && b.IsActive, cancellationToken);
        if (bot is null)
            return Result.Failure<int>("Bot not found.");
        if (!await Admins.RunsBotAsync(db, bot, request.RequestingUserId, cancellationToken))
            return Result.Failure<int>("Only the bot's maker can clear its memory.");

        var memories = await db.BotMemories.Where(m => m.BotId == bot.Id).ToListAsync(cancellationToken);
        var cursors = await db.BotMemoryCursors.Where(c => c.BotId == bot.Id).ToListAsync(cancellationToken);
        db.BotMemories.RemoveRange(memories);
        db.BotMemoryCursors.RemoveRange(cursors);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(memories.Count);
    }
}

/// <summary>What every bot remembers about the reader.</summary>
public record GetMemoriesAboutMeQuery(Guid UserId) : IQuery<Result<List<BotMemoryDto>>>;

public class GetMemoriesAboutMeHandler(NeuralDamageDbContext db) : IQueryHandler<GetMemoriesAboutMeQuery, Result<List<BotMemoryDto>>>
{
    public async ValueTask<Result<List<BotMemoryDto>>> Handle(GetMemoriesAboutMeQuery request, CancellationToken cancellationToken)
    {
        var chats = await MemoryAccess.ChatsOfAsync(db, request.UserId, cancellationToken);
        var memories = await db.BotMemories
            .Where(m => m.SubjectUserId == request.UserId && m.Bot.IsActive)
            .Include(m => m.Bot)
            .Include(m => m.SubjectUser)
            .OrderBy(m => m.Bot.Name).ThenByDescending(m => m.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return Result.Success(await MemoryAccess.ToDtosAsync(db, memories, chats, cancellationToken));
    }
}

/// <summary>
/// Whether bots may remember things about the person. Turning it off forgets what they had too,
/// and the bots stop picking up anything new about them.
/// </summary>
public record AllowBotMemoryCommand(Guid UserId, bool Allowed) : ICommand<Result<UserDto>>;

public class AllowBotMemoryHandler(NeuralDamageDbContext db, AppAdmins admins) : ICommandHandler<AllowBotMemoryCommand, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(AllowBotMemoryCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<UserDto>("User not found.");

        user.AllowBotMemory = request.Allowed;
        if (!request.Allowed)
            db.BotMemories.RemoveRange(await db.BotMemories.Where(m => m.SubjectUserId == user.Id).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(Infrastructure.Mappers.UserMapper.ToDto(user) with { IsAdmin = user.IsAdmin || admins.IsAdmin(user.Email) });
    }
}
