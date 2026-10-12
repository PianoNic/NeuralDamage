using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Validators;

namespace NeuralDamage.Tests.Application;

/// <summary>Temperatures above 1.5 turn replies to gibberish, so neither creating nor editing a bot takes one.</summary>
public class BotValidatorTests
{
    private static CreateBotCommand Create(double temperature) =>
        new("Mona", "deepseek/deepseek-chat", "", null, temperature, null, Guid.NewGuid());

    private static UpdateBotCommand Update(double? temperature) =>
        new(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, temperature, null, null);

    [Test]
    [Arguments(0.0, true)]
    [Arguments(1.5, true)]
    [Arguments(1.51, false)]
    [Arguments(2.0, false)]
    [Arguments(-0.1, false)]
    public async Task Temperature_IsBetweenZeroAndOnePointFive(double temperature, bool valid)
    {
        await Assert.That(new CreateBotValidator().Validate(Create(temperature)).IsValid).IsEqualTo(valid);
        await Assert.That(new UpdateBotValidator().Validate(Update(temperature)).IsValid).IsEqualTo(valid);
    }

    [Test]
    public async Task Update_WithoutATemperature_IsValid()
    {
        await Assert.That(new UpdateBotValidator().Validate(Update(null)).IsValid).IsTrue();
    }
}
