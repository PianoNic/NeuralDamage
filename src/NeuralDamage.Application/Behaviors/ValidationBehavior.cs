using FluentValidation;
using Mediator;

namespace NeuralDamage.Application.Behaviors;

/// <summary>
/// Runs a command's FluentValidation validators before its handler. A failure
/// throws <see cref="ValidationException"/>, which the API answers with 400.
/// </summary>
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
            await validator.ValidateAndThrowAsync(message, cancellationToken);

        return await next(message, cancellationToken);
    }
}
