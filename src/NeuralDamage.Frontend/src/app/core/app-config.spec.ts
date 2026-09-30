import { TestBed } from '@angular/core/testing';
import { AppConfig, DEFAULT_UPLOAD_LIMITS } from './app-config';

describe('AppConfig', () => {
  it('shows no version until the server has said which release it is', () => {
    const config = TestBed.inject(AppConfig);

    expect(config.version()).toBeNull();
    expect(config.uploadLimits()).toEqual(DEFAULT_UPLOAD_LIMITS);
  });

  it('takes the version and limits from /api/app', () => {
    const config = TestBed.inject(AppConfig);
    const limits = { maxBytes: 100, maxPerMessage: 2, contentTypes: ['image/png'] };

    config.apply({ attachments: limits, version: '0.1.1' });

    expect(config.version()).toBe('0.1.1');
    expect(config.uploadLimits()).toEqual(limits);
  });
});
