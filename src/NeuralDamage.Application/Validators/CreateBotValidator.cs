using FluentValidation;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Validators;

public class CreateBotValidator : AbstractValidator<CreateBotCommand>
{
    public CreateBotValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ModelId).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Temperature).InclusiveBetween(0.0, Bot.MaxTemperature);
    }
}
