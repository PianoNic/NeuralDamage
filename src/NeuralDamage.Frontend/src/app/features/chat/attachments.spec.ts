import { DEFAULT_UPLOAD_LIMITS } from '../../core/app-config';
import { formatBytes, imageProblem } from './attachments';

describe('imageProblem', () => {
  const file = (name: string, type: string, size = 10) =>
    new File([new Uint8Array(size)], name, { type });

  it('checks against the limits the server serves', () => {
    const limits = { maxBytes: 2 * 1024 * 1024, maxPerMessage: 2, contentTypes: ['image/png'] };

    expect(imageProblem(file('a.png', 'image/png'), limits)).toBeNull();
    expect(imageProblem(file('a.gif', 'image/gif'), limits)).toBe('a.gif is not a PNG image.');
    expect(imageProblem(file('big.png', 'image/png', 2 * 1024 * 1024 + 1), limits)).toBe(
      'big.png is over 2 MB.',
    );
  });

  it('names every allowed type by default', () => {
    expect(imageProblem(file('x.svg', 'image/svg+xml'), DEFAULT_UPLOAD_LIMITS)).toBe(
      'x.svg is not a PNG, JPEG, WebP or GIF image.',
    );
  });

  it('writes sizes the way people read them', () => {
    expect(formatBytes(10 * 1024 * 1024)).toBe('10 MB');
    expect(formatBytes(1.5 * 1024 * 1024)).toBe('1.5 MB');
    expect(formatBytes(500 * 1024)).toBe('500 KB');
  });
});
