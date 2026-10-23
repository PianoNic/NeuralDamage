import { describe, expect, it } from 'vitest';
import { withOfflineAccess } from './oidc';

describe('withOfflineAccess', () => {
  it('adds offline_access once, so refresh tokens are issued', () => {
    expect(withOfflineAccess('openid profile email')).toBe('openid profile email offline_access');
    expect(withOfflineAccess(' openid  offline_access email ')).toBe('openid offline_access email');
  });
});
