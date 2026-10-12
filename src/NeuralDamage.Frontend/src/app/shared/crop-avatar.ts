/** Side of a bot's uploaded picture, in pixels. Avatars never show larger than 48. */
export const AVATAR_SIZE = 256;

/**
 * Crops an image to a square and scales it down to at most
 * `size` pixels a side, so a phone photo uploads as a few dozen KB. WebP where
 * the browser can write it; browsers that can't write PNG instead. A GIF keeps
 * only its first frame. Throws when the file is not an image the browser can read.
 *
 * The square is centred, or with `anchor: 'top'` centred across and taken from the top, which keeps the
 * head of a portrait or a full-length figure in the picture.
 */
export async function cropAvatar(file: Blob, size = AVATAR_SIZE, anchor: 'center' | 'top' = 'center'): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  try {
    const side = Math.min(bitmap.width, bitmap.height);
    const out = Math.min(size, side);
    const canvas = document.createElement('canvas');
    canvas.width = out;
    canvas.height = out;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Canvas is not available.');
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, (bitmap.width - side) / 2, anchor === 'top' ? 0 : (bitmap.height - side) / 2, side, side, 0, 0, out, out);
    return await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not encode the image.'))), 'image/webp', 0.9),
    );
  } finally {
    bitmap.close();
  }
}
