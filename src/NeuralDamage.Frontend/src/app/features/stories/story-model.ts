import type { StorySpec } from '../../api/model/storySpec';

/** The editor's stage is 360 points wide, as the server renders it (×3 into 1080 pixels). */
export const STAGE_WIDTH = 360;
export const STAGE_HEIGHT = 640;

/** What the layers are drawn for: a story, or a post's picture in 4:5 or square. */
export type PictureFormat = 'story' | 'post-4x5' | 'post-1x1';

/** Each format's stage in points and the PNG the server renders from it. */
export const FORMATS: Record<PictureFormat, { stageHeight: number; width: number; height: number; label: string }> = {
  story: { stageHeight: 640, width: 1080, height: 1920, label: '9:16' },
  'post-4x5': { stageHeight: 450, width: 1080, height: 1350, label: '4:5' },
  'post-1x1': { stageHeight: 360, width: 1080, height: 1080, label: 'Square' },
};

export const BACKGROUNDS = ['#1f3a2b', '#1d3347', '#3a1d33', '#3f2a1d', '#141414', '#e0409a', '#f5d547', '#fafafa'] as const;
export const TEXT_COLORS = ['#fafafa', '#0a0a0a', '#f5d547', '#e0409a', '#7dd3fc'] as const;

/** The server's limits (StorySpecRules), checked here first so the button can say why it is off. */
export const MAX_LAYERS = 12;
export const MAX_IMAGE_LAYERS = 3;
export const MAX_TEXT_LENGTH = 200;
export const TEXT_SIZE = { min: 10, max: 120 } as const;
export const PICTURE_SIZE = { min: 30, max: 360 } as const;

interface LayerBase {
  /** Only for the editor; the spec has no ids. */
  id: string;
  x: number;
  y: number;
  size: number;
}

export interface TextLayer extends LayerBase {
  type: 'text';
  text: string;
  color: string;
  weight: number;
}

export interface StickerLayer extends LayerBase {
  type: 'sticker';
  name: string;
  /** The sticker's picture, for the stage. */
  url: string;
  rotation: number;
}

export interface ImageLayer extends LayerBase {
  type: 'image';
  /** `upload:<id>` or `wiki:<name>#<n>`. */
  ref: string;
  /** What the stage shows: an object URL or an API URL that needs the token. */
  previewUrl: string;
  rotation: number;
}

export type Layer = TextLayer | StickerLayer | ImageLayer;

/** A layer before it has an id. */
type NewLayer = Omit<TextLayer, 'id'> | Omit<StickerLayer, 'id'> | Omit<ImageLayer, 'id'>;

/** What the settings panel can change on a layer. */
export type LayerChange = Partial<{ x: number; y: number; size: number; text: string; color: string; weight: number; rotation: number }>;

export interface StoryDraft {
  /** Missing on a story's draft. */
  format?: PictureFormat;
  background: string;
  /** The background picture, if any. */
  backgroundImage: { ref: string; previewUrl: string } | null;
  layers: Layer[];
  selectedId: string | null;
  nextId: number;
}

export function emptyDraft(format: PictureFormat = 'story'): StoryDraft {
  return { ...(format === 'story' ? {} : { format }), background: BACKGROUNDS[0], backgroundImage: null, layers: [], selectedId: null, nextId: 1 };
}

export function formatOf(draft: StoryDraft): PictureFormat {
  return draft.format ?? 'story';
}

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

/** A new layer goes on top and is selected. */
function add(draft: StoryDraft, layer: NewLayer): StoryDraft {
  if (draft.layers.length >= MAX_LAYERS) return draft;
  const id = `l${draft.nextId}`;
  return { ...draft, layers: [...draft.layers, { ...layer, id } as Layer], selectedId: id, nextId: draft.nextId + 1 };
}

export function addText(draft: StoryDraft, text = 'New text'): StoryDraft {
  return add(draft, { type: 'text', text, x: 50, y: 30, size: 28, color: TEXT_COLORS[0], weight: 700 });
}

export function addSticker(draft: StoryDraft, name: string, url: string): StoryDraft {
  return add(draft, { type: 'sticker', name, url, x: 50, y: 60, size: 130, rotation: 0 });
}

export function addImage(draft: StoryDraft, ref: string, previewUrl: string): StoryDraft {
  if (draft.layers.filter((l) => l.type === 'image').length >= MAX_IMAGE_LAYERS) return draft;
  return add(draft, { type: 'image', ref, previewUrl, x: 50, y: 50, size: 220, rotation: 0 });
}

/** Moves a layer's centre to (x, y) percent, kept on the story. */
export function moveLayer(draft: StoryDraft, id: string, x: number, y: number): StoryDraft {
  return updateLayer(draft, id, { x: Math.round(clamp(x, 0, 100)), y: Math.round(clamp(y, 0, 100)) });
}

export function updateLayer(draft: StoryDraft, id: string, change: LayerChange): StoryDraft {
  return {
    ...draft,
    layers: draft.layers.map((l) => {
      if (l.id !== id) return l;
      const next = { ...l, ...change } as Layer;
      const bounds = next.type === 'text' ? TEXT_SIZE : PICTURE_SIZE;
      return { ...next, size: clamp(next.size, bounds.min, bounds.max) };
    }),
  };
}

export function removeLayer(draft: StoryDraft, id: string): StoryDraft {
  return { ...draft, layers: draft.layers.filter((l) => l.id !== id), selectedId: draft.selectedId === id ? null : draft.selectedId };
}

/** Moves a layer one step up (towards the top, drawn later) or down the stack. */
export function reorderLayer(draft: StoryDraft, id: string, step: 1 | -1): StoryDraft {
  const i = draft.layers.findIndex((l) => l.id === id);
  const j = i + step;
  if (i < 0 || j < 0 || j >= draft.layers.length) return draft;
  const layers = [...draft.layers];
  [layers[i], layers[j]] = [layers[j], layers[i]];
  return { ...draft, layers };
}

/** What gets posted and what a bot sends: the spec, without the editor's ids and previews. */
export function toSpec(draft: StoryDraft): StorySpec {
  return {
    ...(draft.format && draft.format !== 'story' ? { format: draft.format } : {}),
    background: draft.backgroundImage
      ? { color: draft.background, image: draft.backgroundImage.ref, fit: 'cover' }
      : { color: draft.background },
    layers: draft.layers.map((l) => {
      switch (l.type) {
        case 'text':
          return { type: 'text', text: l.text, x: l.x, y: l.y, size: l.size, color: l.color, weight: l.weight };
        case 'sticker':
          return { type: 'sticker', name: l.name, x: l.x, y: l.y, size: l.size, ...(l.rotation ? { rotation: l.rotation } : {}) };
        case 'image':
          return { type: 'image', image: l.ref, x: l.x, y: l.y, size: l.size, ...(l.rotation ? { rotation: l.rotation } : {}) };
      }
    }),
  };
}

/** Why the story cannot be posted yet, or null when it can. */
export function draftProblem(draft: StoryDraft): string | null {
  if (!draft.layers.length && !draft.backgroundImage) return 'Add some text, a sticker or a picture.';
  const text = draft.layers.filter((l): l is TextLayer => l.type === 'text');
  if (text.some((l) => !l.text.trim())) return 'A text layer is empty.';
  if (text.some((l) => l.text.length > MAX_TEXT_LENGTH)) return `Text layers can hold ${MAX_TEXT_LENGTH} characters.`;
  return null;
}

/** "Text: Hello", "Sticker: swole-doge", "Picture", for the layer list. */
export function layerLabel(layer: Layer): string {
  const label = layer.type === 'text' ? `Text: ${layer.text}` : layer.type === 'sticker' ? `Sticker: ${layer.name}` : 'Picture';
  return label.length > 42 ? `${label.slice(0, 41)}…` : label;
}
