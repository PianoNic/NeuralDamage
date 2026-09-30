using FluentValidation;
using NeuralDamage.Application.Commands;

namespace NeuralDamage.Application.Validators;

/// <summary>
/// The rules for a chat name, shared by renaming from the menu and /rename,
/// so both refuse the same names with the same message.
/// </summary>
public class UpdateChatValidator : AbstractValidator<UpdateChatCommand>
{
    public const int MaxNameLength = 256;
    public const string EmptyName = "Chat names cannot be empty.";
    public static readonly string TooLongName = $"Chat names can be at most {MaxNameLength} characters.";

    public UpdateChatValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage(EmptyName).MaximumLength(MaxNameLength).WithMessage(TooLongName);
    }
}
