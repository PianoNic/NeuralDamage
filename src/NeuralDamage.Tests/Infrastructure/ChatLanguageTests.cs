using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Tests.Infrastructure;

public class ChatLanguageTests
{
    [Test]
    [Arguments("isch das din ernst?")]
    [Arguments("hesch zit morn?")]
    [Arguments("nei sicher nöd, das isch doch blöd")]
    [Arguments("ich weiss nicht, ob das heute noch klappt")]
    [Arguments("Hast du das schon gesehen?")]
    public async Task GermanAndSwissGerman_AreGerman(string text)
    {
        await Assert.That(ChatLanguage.Detect([text])).IsEqualTo(PeopleLanguage.German);
    }

    [Test]
    [Arguments("did you see that?")]
    [Arguments("what do you think about the new movie")]
    [Arguments("anyone up for pizza tonight")]
    public async Task English_IsEnglish(string text)
    {
        await Assert.That(ChatLanguage.Detect([text])).IsEqualTo(PeopleLanguage.English);
    }

    [Test]
    [Arguments("ok")]
    [Arguments("lol")]
    [Arguments("😂😂")]
    [Arguments("pizza?")]
    [Arguments("")]
    public async Task TooLittleText_IsUnknown(string text)
    {
        await Assert.That(ChatLanguage.Detect([text])).IsEqualTo(PeopleLanguage.Unknown);
    }

    [Test]
    public async Task EvenlyMixed_IsUnknown()
    {
        await Assert.That(ChatLanguage.Detect(["what do you think", "isch das din ernst"])).IsEqualTo(PeopleLanguage.Unknown);
    }

    [Test]
    public async Task MostlyGerman_WithAnEnglishWordOrTwo_IsGerman()
    {
        await Assert.That(ChatLanguage.Detect(["hesch zit morn?", "ja sicher, das isch nice", "ok cool, bis morn"]))
            .IsEqualTo(PeopleLanguage.German);
    }

    [Test]
    public async Task WordsBothLanguagesUse_CountForNeither()
    {
        await Assert.That(ChatLanguage.Detect(["so was in man will"])).IsEqualTo(PeopleLanguage.Unknown);
    }

    [Test]
    public async Task OnlyThePeople_Count_NotTheBots()
    {
        var user = Guid.NewGuid();
        var bot = new Bot { Name = "Rex", ModelId = "m", CreatedById = user };
        List<Message> messages =
        [
            Said(user, "hesch zit morn?"),
            Said(null, "Sure! What do you have in mind for tomorrow? I think it would be great.", bot),
            Said(null, "Yeah, I would love to do something fun with you all.", bot),
            Said(user, "isch das din ernst?"),
        ];

        await Assert.That(ChatLanguage.OfPeople(messages)).IsEqualTo(PeopleLanguage.German);
    }

    [Test]
    public async Task OnlyThePeoplesLatestMessages_Count()
    {
        var user = Guid.NewGuid();
        var messages = Enumerable.Repeat("what do you think about this", 10).Select(t => Said(user, t))
            .Concat(Enumerable.Repeat("isch das din ernst?", ChatLanguage.MessagesLooked).Select(t => Said(user, t)))
            .ToList();

        await Assert.That(ChatLanguage.OfPeople(messages)).IsEqualTo(PeopleLanguage.German);
    }

    [Test]
    public async Task Note_TellsTheBotThePeoplesLanguage()
    {
        var note = BotPromptBuilder.BuildNote(new DateTimeOffset(2026, 9, 30, 14, 51, 0, TimeSpan.Zero),
            clock: new BotClock(BotClock.Resolve("Europe/Zurich")), language: PeopleLanguage.German);

        await Assert.That(note.Content).IsEqualTo(
            "(It is Wednesday afternoon. Don't bring up the time or day unless it matters. The people here write German. Answer in German unless a person asks you for another language or writes to you in one.)");
    }

    [Test]
    public async Task Note_WithoutAKnownLanguage_KeepsTodaysRule()
    {
        var note = BotPromptBuilder.BuildNote(new DateTimeOffset(2026, 9, 30, 14, 51, 0, TimeSpan.Zero),
            clock: new BotClock(BotClock.Resolve("Europe/Zurich")));

        await Assert.That(note.Content).Contains("Answer in the language of the message you're answering.");
        await Assert.That(note.Content).DoesNotContain("The people here write");
    }

    [Test]
    public async Task SystemPrompt_TellsBotsToAnswerOtherBotsInThePeoplesLanguage()
    {
        var prompt = BotPromptBuilder.BuildSystemPrompt(new Bot { Name = "Rex", ModelId = "m", CreatedById = Guid.Empty }, ["Alice"]);

        await Assert.That(prompt).Contains("answer other bots in that language, never in the other bot's");
    }

    private static Message Said(Guid? userId, string content, Bot? bot = null) => new()
    {
        ChatId = Guid.Empty,
        SenderUserId = userId,
        SenderBotId = bot?.Id,
        SenderBot = bot,
        Content = content,
    };
}
