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
    public async Task TooLongMessage_IsRejected()
    {
        var command = new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), new string('x', 4001));

        await Assert.That(async () => await Behavior.Handle(command, (_, _) => ValueTask.FromResult(Result.Success()), CancellationToken.None))
            .Throws<ValidationException>();
    }

    [Test]
    public async Task ValidMessage_IsHandled()
    {
        var command = new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "hello");

        var result = await Behavior.Handle(command, (_, _) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }
}
