using FluentValidation;
using NeuralDamage.Application.Commands;

namespace NeuralDamage.Application.Validators;

public class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
    }
}
