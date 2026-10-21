using FluentValidation;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Validators;

public class UpdateBotValidator : AbstractValidator<UpdateBotCommand>
{
    public UpdateBotValidator()
    {
        RuleFor(x => x.Temperature).InclusiveBetween(0.0, Bot.MaxTemperature).When(x => x.Temperature is not null);
        RuleFor(x => x.MaxPostsPerDay).InclusiveBetween(0, Bot.MaxPostsPerDayLimit).When(x => x.MaxPostsPerDay is not null);
    }
}
