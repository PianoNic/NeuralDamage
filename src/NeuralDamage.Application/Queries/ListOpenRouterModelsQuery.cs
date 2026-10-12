using Mediator;
using Microsoft.Extensions.Configuration;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Application.Queries;

public record ListOpenRouterModelsQuery : IQuery<Result<List<OpenRouterModel>>>;

/// <summary>
/// The models a bot may use, the one a new bot starts on (<c>App:DefaultModel</c>) marked
/// and the recommended ones (<c>Recommendations</c>) ranked.
/// </summary>
public class ListOpenRouterModelsHandler(IOpenRouterService openRouter, ModelPolicy modelPolicy, IConfiguration configuration) : IQueryHandler<ListOpenRouterModelsQuery, Result<List<OpenRouterModel>>>
{
    public async ValueTask<Result<List<OpenRouterModel>>> Handle(ListOpenRouterModelsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var models = await modelPolicy.FilterAsync(openRouter, await openRouter.ListModelsAsync(cancellationToken), cancellationToken);
            var defaultModel = CustomProvider.DefaultModel(configuration["App:DefaultModel"], models);
            return Result<List<OpenRouterModel>>.Success(ModelRecommendation.Apply(
                ModelRecommendation.FromConfiguration(configuration),
                models.Select(m => m with { PriceTier = modelPolicy.PriceTier(m.Pricing), IsDefault = m.Id == defaultModel }),
                defaultModel));
        }
        catch (Exception ex)
        {
            return Result<List<OpenRouterModel>>.Failure($"Failed to fetch models: {ex.Message}");
        }
    }
}
