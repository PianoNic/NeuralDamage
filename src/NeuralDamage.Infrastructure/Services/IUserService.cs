namespace NeuralDamage.Infrastructure.Services
{
    public interface IUserService
    {
        Task SyncCurrentUserAsync(CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(string externalId, CancellationToken cancellationToken = default);
        Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Maps an OIDC subject onto the domain user id. SignalR hubs need this
        /// because the token's `sub` is the external id, not Users.Id.
        /// </summary>
        Task<Guid?> GetUserIdByExternalIdAsync(string externalId, CancellationToken cancellationToken = default);
    }
}
