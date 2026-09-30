import { HttpErrorResponse } from '@angular/common/http';
import { toastState } from '@spartan-ng/brain/sonner';
import { RATE_LIMIT_TOAST_ID, toastApiError } from './http-errors';

describe('toastApiError', () => {
  beforeEach(() => toastState.reset());
  afterEach(() => toastState.reset());

  const tooFast = () =>
    new HttpErrorResponse({ status: 429, error: "You're sending messages too fast." });

  it('keeps one toast for any number of rate-limited requests', () => {
    for (let i = 0; i < 4; i++) toastApiError(tooFast());

    const toasts = toastState.toasts();
    expect(toasts.length).toBe(1);
    expect(toasts[0].id).toBe(RATE_LIMIT_TOAST_ID);
    expect(toasts[0].title).toBe("You're sending messages too fast.");
  });

  it('updates the rate-limit toast with the latest reason', () => {
    toastApiError(tooFast());
    toastApiError(new HttpErrorResponse({ status: 429, error: "You're uploading images too fast." }));

    expect(toastState.toasts().map((t) => t.title)).toEqual(["You're uploading images too fast."]);
  });

  it('does not repeat an identical error, but shows different ones', () => {
    const refused = () => new HttpErrorResponse({ status: 400, error: 'Nope.' });
    toastApiError(refused());
    toastApiError(refused());
    toastApiError(new HttpErrorResponse({ status: 400, error: 'Something else.' }));

    expect(toastState.toasts().map((t) => t.title).sort()).toEqual(['Nope.', 'Something else.']);
  });
});
