using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class NicknameTests
{
    [Test]
    public async Task Nicknames_AreTrimmedDeduplicatedAndChecked()
    {
        await Assert.That(PeopleNames.Normalize(" Niclas, @Nic ,niclas,, ").Value).IsEqualTo("Niclas, Nic");
        await Assert.That(PeopleNames.Normalize("").Value).IsNull();
        await Assert.That(PeopleNames.Normalize("a,b,c,d,e,f").Error).IsNotNull();
        await Assert.That(PeopleNames.Normalize("Niclas <3").Error).IsNotNull();
        await Assert.That(PeopleNames.Normalize(new string('x', 33)).Error).IsNotNull();
    }

    [Test]
    public async Task ANickname_MentionsThePerson_AsAWholeWord()
    {
        await Assert.That(PeopleNames.IsMentioned("also @Niclas if you're reading this", "PianoNic", "Niclas")).IsTrue();
        await Assert.That(PeopleNames.IsMentioned("hey @pianonic", "PianoNic", null)).IsTrue();
        await Assert.That(PeopleNames.IsMentioned("@Niclasssss", "PianoNic", "Niclas")).IsFalse();
        await Assert.That(PeopleNames.IsMentioned("Niclas without the at", "PianoNic", "Niclas")).IsFalse();
    }

    [Test]
    public async Task BotsAreToldThePersonsNicknames()
    {
        var user = new User { ExternalId = "x", Email = "n@example.com", DisplayName = "PianoNic", Nicknames = "Niclas, Nic" };

        await Assert.That(PeopleNames.ForPrompt(user)).IsEqualTo("PianoNic (also called Niclas, Nic)");
        await Assert.That(PeopleNames.ForPrompt(new User { ExternalId = "y", Email = "b@example.com", DisplayName = "Bob" })).IsEqualTo("Bob");
    }

    [Test]
    public async Task SettingNicknames_SavesThemNormalised()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "x", Email = "n@example.com", DisplayName = "PianoNic" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await new UpdateNicknamesHandler(db, AppAdmins.None).Handle(new UpdateNicknamesCommand(user.Id, "Niclas , Nic"), default);
        var refused = await new UpdateNicknamesHandler(db, AppAdmins.None).Handle(new UpdateNicknamesCommand(user.Id, "a,b,c,d,e,f"), default);

        await Assert.That(result.Value!.Nicknames).IsEqualTo("Niclas, Nic");
        await Assert.That(refused.IsSuccess).IsFalse();
        await Assert.That(user.Nicknames).IsEqualTo("Niclas, Nic");
    }

    [Test]
    public async Task ABotsPost_MentioningANickname_TellsThatPerson()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        h.User.Nicknames = "Niclas";
        await h.Db.SaveChangesAsync();
        var post = await h.PostAsync("also @Niclas if you're reading this, i see you", h.Bots[0]);

        await FeedNotices.NotifyMentionsAsync(h.Db, h.Notifier, post, h.Bots[0].Name);

        await h.Notifier.Received(1).Reply(h.User.Id, Arg.Is<FeedReplyNoticeDto>(n => n.Mention && n.PostId == post.Id && n.CommentId == null));
    }

    [Test]
    public async Task YourOwnPost_MentioningYou_TellsNobody()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        h.User.Nicknames = "Niclas";
        await h.Db.SaveChangesAsync();
        var post = await h.PostAsync("talking about @Niclas in the third person");

        await FeedNotices.NotifyMentionsAsync(h.Db, h.Notifier, post, "me");

        await h.Notifier.DidNotReceiveWithAnyArgs().Reply(default, default!);
    }
}
