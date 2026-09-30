import { HttpErrorResponse } from '@angular/common/http';
import { describeHttpError, type HttpErrorMessages } from '@prompt-kit/http-error';

const DEFAULTS: HttpErrorMessages = {
  forbidden: 'You are not allowed to do that here.',
};

/**
 * A failed request as a sentence for the user. The API answers a rejected command with its reason
 * as plain text, which reads better than a generic message, so that wins; a validation failure's
 * first message comes next; anything else falls back to prompt-kit's status-based wording.
 */
export function describeApiError(error: unknown, overrides?: HttpErrorMessages): string {
  if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
    if (typeof error.error === 'string' && error.error.trim()) return error.error.trim();
    const errors = (error.error as { errors?: Record<string, string[]> } | null)?.errors;
    const first = errors && Object.values(errors).flat()[0];
    if (first) return first;
  }
  return describeHttpError(error, { ...DEFAULTS, ...overrides });
}
