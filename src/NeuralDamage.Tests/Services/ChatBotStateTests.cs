using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Tests.Services;

/// <summary>
/// Stops and mutes are kept in the database, so a restarted API, which builds a
/// fresh <see cref="ChatBotState"/>, still has them.
/// </summary>
public class ChatBotStateTests
{
    private static (IServiceScopeFactory Scopes, Chat Chat, Bot Gpt, Bot Claude) Setup()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<NeuralDamageDbContext>(o => o.UseInMemoryDatabase(name));
        var provider = services.BuildServiceProvider();

        var user = new User { ExternalId = "ext-1", Email = "a@test.com" };
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        var gpt = new Bot { Name = "GPT", ModelId = "m", SystemPrompt = "x", CreatedById = user.Id };
        var claude = new Bot { Name = "Claude", ModelId = "m", SystemPrompt = "x", CreatedById = user.Id };
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            db.AddRange(user, chat, gpt, claude);
            db.SaveChanges();
        }
        return (provider.GetRequiredService<IServiceScopeFactory>(), chat, gpt, claude);
    }

    [Test]
    public async Task StopAndMute_SurviveARestart()
    {
        var (scopes, chat, gpt, claude) = Setup();
        var before = new ChatBotState(scopes);
        before.Stop(chat.Id);
        before.Mute(chat.Id, gpt.Id);
        before.Mute(chat.Id, gpt.Id);

        var after = new ChatBotState(scopes);

        await Assert.That(after.IsStopped(chat.Id)).IsTrue();
        await Assert.That(after.IsMuted(chat.Id, gpt.Id)).IsTrue();
        await Assert.That(after.IsMuted(chat.Id, claude.Id)).IsFalse();
        using var scope = scopes.CreateScope();
        await Assert.That(await scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>().BotSilences.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task ResumeAndUnmute_StayLiftedAfterARestart()
    {
        var (scopes, chat, gpt, claude) = Setup();
        var before = new ChatBotState(scopes);
        before.Stop(chat.Id);
        before.Mute(chat.Id, gpt.Id);
        before.Mute(chat.Id, claude.Id);
        before.Resume(chat.Id);
        before.Unmute(chat.Id, gpt.Id);

        var after = new ChatBotState(scopes);

        await Assert.That(after.IsStopped(chat.Id)).IsFalse();
        await Assert.That(after.IsMuted(chat.Id, gpt.Id)).IsFalse();
        await Assert.That(after.IsMuted(chat.Id, claude.Id)).IsTrue();
    }
}
