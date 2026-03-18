namespace NeuralDamage.Application.Interfaces;

public interface ICurrentUserService
{
    string? ExternalId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
}
