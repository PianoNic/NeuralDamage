using FluentValidation;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Validators;

public class CreateBotValidator : AbstractValidator<CreateBotCommand>
{
    public const string NoShortDescription = "Give it a short description.";
    public static readonly string TooLongShortDescription = $"The short description can be at most {Bot.MaxShortDescriptionLength} characters.";

    public CreateBotValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ModelId).NotEmpty().MaximumLength(256);
        // Required for new bots only: older ones may have none and must stay editable.
        RuleFor(x => x.Personality).NotEmpty().WithMessage(NoShortDescription)
            .Must(FitsAShortDescription).WithMessage(TooLongShortDescription);
        RuleFor(x => x.Temperature).InclusiveBetween(0.0, Bot.MaxTemperature);
    }

    /// <summary>Measured trimmed, as it is saved.</summary>
    public static bool FitsAShortDescription(string? description) =>
        (description ?? "").Trim().Length <= Bot.MaxShortDescriptionLength;
}
