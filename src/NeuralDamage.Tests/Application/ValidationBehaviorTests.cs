using System.Globalization;
using FluentValidation;
using NeuralDamage.Application.Behaviors;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Validators;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Tests.Application;

public class ValidationBehaviorTests
{
    private static readonly ValidationBehavior<SendMessageCommand, Result> Behavior = new([new SendMessageValidator()]);

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task InvalidMessage_NeverReachesTheHandler(string content)
    {
        var handled = false;
        var command = new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), content);

        await Assert.That(async () => await Behavior.Handle(command, (_, _) => { handled = true; return ValueTask.FromResult(Result.Success()); }, CancellationToken.None))
            .Throws<ValidationException>();
        await Assert.That(handled).IsFalse();
    }

    [Test]
    [Arguments("", "Chat names cannot be empty.")]
    [Arguments("   ", "Chat names cannot be empty.")]
    [Arguments(null, "Chat names can be at most 256 characters.")]
    public async Task RenamingFromTheMenu_RejectsWhatRenameRejects(string? name, string message)
    {
        var behavior = new ValidationBehavior<UpdateChatCommand, Result>([new UpdateChatValidator()]);
        var command = new UpdateChatCommand(Guid.NewGuid(), name ?? new string('x', 257), Guid.NewGuid());

        var thrown = await Assert.That(async () => await behavior.Handle(command, (_, _) => ValueTask.FromResult(Result.Success()), CancellationToken.None))
            .Throws<ValidationException>();
        await Assert.That(thrown!.Errors.Single().ErrorMessage).IsEqualTo(message);
    }

    [Test]
    public async Task RenamingFromTheMenu_AcceptsANameOfTheMaximumLength()
    {
        var result = await new UpdateChatValidator().ValidateAsync(new UpdateChatCommand(Guid.NewGuid(), new string('x', 256), Guid.NewGuid()));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task TooLongMessage_IsRejected()
    {
        var command = new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), new string('x', 4001));

        await Assert.That(async () => await Behavior.Handle(command, (_, _) => ValueTask.FromResult(Result.Success()), CancellationToken.None))
            .Throws<ValidationException>();
    }

    [Test]
    public async Task PinnedCulture_KeepsMessagesEnglish_OnAGermanHost()
    {
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            ValidationCulture.Pin();

            var result = await new CreateChatValidator().ValidateAsync(new CreateChatCommand("", Guid.NewGuid()));

            await Assert.That(result.Errors.Single().ErrorMessage).IsEqualTo("'Name' must not be empty.");
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
        }
    }

    [Test]
    public async Task ValidMessage_IsHandled()
    {
        var command = new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "hello");

        var result = await Behavior.Handle(command, (_, _) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }
}
