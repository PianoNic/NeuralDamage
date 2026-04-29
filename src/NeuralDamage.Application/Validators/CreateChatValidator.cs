using FluentValidation;
using NeuralDamage.Application.Command;

namespace NeuralDamage.Application.Validators;

public class CreateChatValidator : AbstractValidator<CreateChatCommand>
{
    public CreateChatValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}
