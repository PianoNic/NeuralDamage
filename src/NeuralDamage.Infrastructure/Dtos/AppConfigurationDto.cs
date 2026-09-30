namespace NeuralDamage.Infrastructure.Dtos;

/// <summary>The SPA's runtime configuration: the identity provider, the upload limits, and the release.</summary>
/// <param name="Version">The release the server was built as, from <c>application.properties</c>, e.g. "0.1.1".</param>
public record AppConfigurationDto(
    string Authority,
    string ClientId,
    string RedirectUri,
    string PostLogoutRedirectUri,
    string Scope,
    AttachmentLimitsDto Attachments,
    string Version);

/// <summary>What an image upload may be (<c>Attachments:*</c>); the server checks again regardless.</summary>
public record AttachmentLimitsDto(long MaxBytes, int MaxPerMessage, List<string> ContentTypes);
