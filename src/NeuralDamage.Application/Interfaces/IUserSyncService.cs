using NeuralDamage.Domain;

namespace NeuralDamage.Application.Interfaces;

public interface IUserSyncService
{
    Task<User> SyncUserAsync(string externalId, string email, string displayName, string? avatarUrl = null, CancellationToken cancellationToken = default);
}
