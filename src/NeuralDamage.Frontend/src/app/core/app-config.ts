import { Service, signal } from '@angular/core';
import type { AppConfigurationDto, AttachmentLimitsDto } from '../api';

/** The server's defaults (`Attachments:*`), used until `/api/app` has answered. */
export const DEFAULT_UPLOAD_LIMITS: AttachmentLimitsDto = {
  maxBytes: 10 * 1024 * 1024,
  maxPerMessage: 4,
  contentTypes: ['image/png', 'image/jpeg', 'image/webp', 'image/gif'],
};

/**
 * What `/api/app` says beyond sign-in. The OIDC loader fetches that document at startup anyway and
 * hands it over here, so the limits the composer checks are the ones the server enforces, and the
 * version in the sidebar is the release that is actually running.
 */
@Service()
export class AppConfig {
  private readonly limits = signal<AttachmentLimitsDto>(DEFAULT_UPLOAD_LIMITS);
  readonly uploadLimits = this.limits.asReadonly();

  private readonly serverVersion = signal<string | null>(null);
  /** The release the server was built as, e.g. "0.1.1"; null until `/api/app` has answered. */
  readonly version = this.serverVersion.asReadonly();

  apply(config: Partial<Pick<AppConfigurationDto, 'attachments' | 'version'>>): void {
    if (config.attachments) this.limits.set(config.attachments);
    if (config.version) this.serverVersion.set(config.version);
  }
}
