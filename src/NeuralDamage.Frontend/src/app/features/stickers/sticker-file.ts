/** What a sticker may be; the server (StickerRules) checks again. */
export const STICKER_TYPES = ['image/png', 'image/webp', 'image/gif'] as const;
export const STICKER_MAX_BYTES = 4 * 1024 * 1024;
/** Still stickers are scaled down to fit this square before upload. */
export const STICKER_SIDE = 512;

/**
 * Whether the image has more than one frame: a GIF with two image blocks, an animated WebP
 * (VP8X with the animation flag) or an APNG (an acTL chunk before the image data). Read from the
 * bytes, the same way the server reads them.
 */
export function isAnimated(bytes: Uint8Array): boolean {
  const ascii = (at: number, length: number) => String.fromCharCode(...bytes.subarray(at, at + length));
  if (ascii(0, 3) === 'GIF') return gifFrames(bytes) > 1;
  if (ascii(0, 4) === 'RIFF' && ascii(8, 4) === 'WEBP')
    return ascii(12, 4) === 'VP8X' && bytes.length > 20 && (bytes[20] & 0x02) !== 0;
  if (bytes[0] === 0x89 && ascii(1, 3) === 'PNG') {
    for (let i = 8; i + 8 <= bytes.length; ) {
      const length = ((bytes[i] << 24) | (bytes[i + 1] << 16) | (bytes[i + 2] << 8) | bytes[i + 3]) >>> 0;
      const type = ascii(i + 4, 4);
      if (type === 'acTL') return true;
      if (type === 'IDAT') return false;
      i += length + 12;
    }
  }
  return false;
}

function gifFrames(bytes: Uint8Array): number {
  if (bytes.length < 13) return 0;
  let i = 13;
  if (bytes[10] & 0x80) i += 3 * (1 << ((bytes[10] & 0x07) + 1));
  const skipBlocks = (at: number) => {
    while (at < bytes.length && bytes[at] !== 0) at += bytes[at] + 1;
    return at + 1;
  };
  let frames = 0;
  while (i < bytes.length && frames < 2) {
    if (bytes[i] === 0x2c) {
      frames++;
      if (i + 10 > bytes.length) break;
      const packed = bytes[i + 9];
      i += 10;
      if (packed & 0x80) i += 3 * (1 << ((packed & 0x07) + 1));
      i = skipBlocks(i + 1);
    } else if (bytes[i] === 0x21) {
      i = skipBlocks(i + 2);
    } else {
      break;
    }
  }
  return frames;
}

/** Why a file cannot be a sticker, or null when it can. */
export function stickerProblem(file: File): string | null {
  if (!(STICKER_TYPES as readonly string[]).includes(file.type))
    return `${file.name || 'That file'} is not a PNG, WebP or GIF image.`;
  if (file.size > STICKER_MAX_BYTES * 4)
    return `${file.name || 'That image'} is too big for a sticker.`;
  return null;
}

/** "Cat Laughing.png" → "cat-laughing", which the server turns into the sticker's name. */
export function nameFromFile(fileName: string): string {
  return fileName
    .replace(/\.[^.]+$/, '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 32)
    .replace(/-+$/, '');
}

/**
 * Gets a file ready to upload: an animated one goes as it is, so it keeps its frames; a still one
 * is cropped to what is not transparent around it and scaled down to fit 512×512, as WebP where the
 * browser can write it.
 */
export async function prepareSticker(file: File): Promise<Blob> {
  if (isAnimated(new Uint8Array(await file.arrayBuffer()))) return file;

  const bitmap = await createImageBitmap(file);
  try {
    const box = opaqueBox(bitmap);
    const scale = Math.min(1, STICKER_SIDE / Math.max(box.width, box.height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(box.width * scale));
    canvas.height = Math.max(1, Math.round(box.height * scale));
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Canvas is not available.');
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, box.x, box.y, box.width, box.height, 0, 0, canvas.width, canvas.height);
    return await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not encode the image.'))), 'image/webp', 0.9),
    );
  } finally {
    bitmap.close();
  }
}

/** The smallest box around every pixel that is not fully transparent; the whole image when there is none. */
function opaqueBox(bitmap: ImageBitmap): { x: number; y: number; width: number; height: number } {
  const whole = { x: 0, y: 0, width: bitmap.width, height: bitmap.height };
  const canvas = document.createElement('canvas');
  canvas.width = bitmap.width;
  canvas.height = bitmap.height;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (!context) return whole;
  context.drawImage(bitmap, 0, 0);
  const { data } = context.getImageData(0, 0, bitmap.width, bitmap.height);
  let top = bitmap.height, left = bitmap.width, right = -1, bottom = -1;
  for (let y = 0; y < bitmap.height; y++) {
    for (let x = 0; x < bitmap.width; x++) {
      if (data[(y * bitmap.width + x) * 4 + 3] === 0) continue;
      if (x < left) left = x;
      if (x > right) right = x;
      if (y < top) top = y;
      if (y > bottom) bottom = y;
    }
  }
  return right < 0 ? whole : { x: left, y: top, width: right - left + 1, height: bottom - top + 1 };
}
