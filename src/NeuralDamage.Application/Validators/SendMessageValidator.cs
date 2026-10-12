using FluentValidation;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Application.Validators;

public class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator() : this(new AttachmentOptions()) { }

    public SendMessageValidator(AttachmentOptions options)
    {
        // A picture can go on its own; otherwise there has to be something to say.
        RuleFor(x => x.Content).NotEmpty()
            .When(x => x.AttachmentIds is not { Count: > 0 } && x.StickerId is null)
            .WithMessage("Write a message or attach an image.");
        RuleFor(x => x.Content).MaximumLength(4000);
        // A sticker is a message of its own, as in WhatsApp.
        RuleFor(x => x.Content).Empty()
            .When(x => x.StickerId is not null)
            .WithMessage("A sticker is sent on its own, without text.");
        RuleFor(x => x.AttachmentIds).Empty()
            .When(x => x.StickerId is not null)
            .WithMessage("A sticker is sent on its own, without images.");
        RuleFor(x => x.AttachmentIds!.Count)
            .LessThanOrEqualTo(options.MaxPerMessage)
            .When(x => x.AttachmentIds is not null)
            .WithMessage($"A message can carry at most {options.MaxPerMessage} images.");
    }
}
