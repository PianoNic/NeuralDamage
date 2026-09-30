namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>The SPA's runtime configuration: the identity provider, and the upload limits.</summary>
public record AppConfigurationDto(
    string Authority,
    string ClientId,
    string RedirectUri,
    string PostLogoutRedirectUri,
    string Scope,
    AttachmentLimitsDto Attachments);

/// <summary>What an image upload may be (<c>Attachments:*</c>); the server checks again regardless.</summary>
public record AttachmentLimitsDto(long MaxBytes, int MaxPerMessage, List<string> ContentTypes);
