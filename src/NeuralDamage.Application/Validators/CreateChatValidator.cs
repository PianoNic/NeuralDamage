using FluentValidation;
using NeuralDamage.Application.Commands;

namespace NeuralDamage.Application.Validators;

public class CreateChatValidator : AbstractValidator<CreateChatCommand>
{
    public CreateChatValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}
