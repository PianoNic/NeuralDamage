using NeuralDamage.Infrastructure.Dtos;

namespace NeuralDamage.Infrastructure.Services
{
    public interface IOidcService
    {
        Task<OidcUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    }
}
