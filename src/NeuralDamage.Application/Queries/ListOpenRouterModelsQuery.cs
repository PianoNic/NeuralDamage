using Mediator;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Models;

namespace NeuralDamage.Application.Queries;

public record ListOpenRouterModelsQuery : IQuery<Result<List<OpenRouterModel>>>;

public class ListOpenRouterModelsHandler(IOpenRouterService openRouter) : IQueryHandler<ListOpenRouterModelsQuery, Result<List<OpenRouterModel>>>
{
    public async ValueTask<Result<List<OpenRouterModel>>> Handle(ListOpenRouterModelsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var models = await openRouter.ListModelsAsync(cancellationToken);
            return Result<List<OpenRouterModel>>.Success(models);
        }
        catch (Exception ex)
        {
            return Result<List<OpenRouterModel>>.Failure($"Failed to fetch models: {ex.Message}");
        }
    }
}
