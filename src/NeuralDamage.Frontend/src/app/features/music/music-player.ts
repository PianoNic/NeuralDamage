import { Service, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import type { MusicDto } from '../../core/models';

/** What the player plays: a post's or story's clip, or a song being tried in the picker. */
export interface Clip {
  /** Who asked, so only their button shows "playing": a post's id, a story's, a picker row's. */
  key: string;
  /** A path on the API, or a full URL (Deezer's preview in the picker). */
  url: string;
  /** Seconds into the audio where the clip starts, and how long it plays before it starts over. */
  start: number;
  seconds: number;
}

export function clipOf(key: string, music: MusicDto): Clip {
  return { key, url: music.audioUrl, start: music.start, seconds: music.seconds };
}

export function audioSrc(url: string): string {
  return url.startsWith('/') ? `${environment.apiBaseUrl}${url}` : url;
}

/** "1:02". */
export function formatTime(seconds: number): string {
  const s = Math.max(0, Math.round(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

/**
 * The app's one audio player, so only one song plays at a time: starting a post's song stops the one
 * before, the way Instagram does. A clip loops between its start and its end.
 */
@Service()
export class MusicPlayer {
  private audio: HTMLAudioElement | null = null;
  private clip: Clip | null = null;

  private readonly playingKey = signal<string | null>(null);
  /** The key of what is playing, or null. */
  readonly playing = this.playingKey.asReadonly();

  private readonly mutedOn = signal(false);
  /** Muted by the reader; stories keep playing silently, like Instagram's. */
  readonly muted = this.mutedOn.asReadonly();

  private readonly failedKey = signal<string | null>(null);
  /** The key of a clip that could not be played, e.g. because the browser blocked it before a tap. */
  readonly failed = this.failedKey.asReadonly();

  isPlaying(key: string): boolean {
    return this.playingKey() === key;
  }

  async play(clip: Clip): Promise<void> {
    const audio = this.element();
    const src = audioSrc(clip.url);
    this.clip = clip;
    this.failedKey.set(null);
    if (audio.src !== src) {
      audio.src = src;
      audio.load();
    }
    this.seekToStart(audio, clip);
    audio.muted = this.mutedOn();
    this.playingKey.set(clip.key);
    try {
      await audio.play();
    } catch {
      // Autoplay refused, or the clip is gone: nothing plays until the reader taps.
      if (this.clip === clip) {
        this.playingKey.set(null);
        this.failedKey.set(clip.key);
      }
    }
  }

  /** Plays the clip, or stops it when it is the one playing. */
  toggle(clip: Clip): Promise<void> {
    if (this.isPlaying(clip.key)) {
      this.stop();
      return Promise.resolve();
    }
    return this.play(clip);
  }

  /** Stops whatever plays; with a key, only when that is what plays. */
  stop(key?: string): void {
    if (key !== undefined && this.playingKey() !== key) return;
    this.audio?.pause();
    this.clip = null;
    this.playingKey.set(null);
  }

  /** Holds the clip where it is (a story pressed and held), without forgetting it. */
  pause(key: string): void {
    if (this.playingKey() === key) this.audio?.pause();
  }

  resume(key: string): void {
    if (this.playingKey() === key && this.audio?.paused) void this.audio.play().catch(() => undefined);
  }

  setMuted(muted: boolean): void {
    this.mutedOn.set(muted);
    if (this.audio) this.audio.muted = muted;
  }

  private element(): HTMLAudioElement {
    if (this.audio) return this.audio;
    const audio = new Audio();
    audio.preload = 'auto';
    audio.addEventListener('loadedmetadata', () => {
      if (this.clip) this.seekToStart(audio, this.clip);
    });
    // Back to the clip's start at its end, and when the file itself ends.
    audio.addEventListener('timeupdate', () => {
      const clip = this.clip;
      if (clip && audio.currentTime >= clip.start + clip.seconds) audio.currentTime = clip.start;
    });
    audio.addEventListener('ended', () => {
      const clip = this.clip;
      if (!clip) return;
      audio.currentTime = clip.start;
      void audio.play().catch(() => undefined);
    });
    this.audio = audio;
    return audio;
  }

  private seekToStart(audio: HTMLAudioElement, clip: Clip): void {
    if (audio.readyState >= HTMLMediaElement.HAVE_METADATA && Math.abs(audio.currentTime - clip.start) > 0.5) {
      audio.currentTime = clip.start;
    }
  }
}
