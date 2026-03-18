namespace NeuralDamage.Application.Interfaces;

public interface IUserResolverService
{
    Task<Guid> GetCurrentUserIdAsync(CancellationToken ct = default);
}
