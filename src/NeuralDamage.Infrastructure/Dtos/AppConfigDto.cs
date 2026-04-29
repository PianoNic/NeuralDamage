namespace NeuralDamage.Infrastructure.Dtos;

public record AppConfigDto(string Authority, string ClientId, string RedirectUri, string PostLogoutRedirectUri, string Scope);
