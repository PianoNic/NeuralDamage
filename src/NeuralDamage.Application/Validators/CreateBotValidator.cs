using FluentValidation;
using NeuralDamage.Application.Command;

namespace NeuralDamage.Application.Validators;

public class CreateBotValidator : AbstractValidator<CreateBotCommand>
{
    public CreateBotValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ModelId).NotEmpty().MaximumLength(256);
        RuleFor(x => x.SystemPrompt).NotEmpty();
        RuleFor(x => x.Temperature).InclusiveBetween(0.0, 2.0);
    }
}
