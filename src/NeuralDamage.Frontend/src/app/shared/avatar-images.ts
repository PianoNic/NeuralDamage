import { HttpClient } from '@angular/common/http';
import { Injectable, Signal, inject, signal } from '@angular/core';
import { environment } from '../../environments/environment';

/** True for an avatar the API serves (a bot's own picture), which needs the sign-in token. */
export function isApiAvatar(url: string): boolean {
  return url.startsWith('/api/');
}

/**
 * Loads avatars the API serves, once per URL, as object URLs. A bot's picture
 * shows on every message it sent, and each <img> fetching it with the token
 * would be a request per message. A URL never changes its image (a new upload
 * gets a new `?v=`), so nothing is ever evicted.
 */
@Injectable({ providedIn: 'root' })
export class AvatarImages {
  private readonly http = inject(HttpClient);
  private readonly loaded = new Map<string, Signal<string | null>>();

  /** The object URL, or null while it loads or when it could not be loaded. */
  get(url: string): Signal<string | null> {
    let image = this.loaded.get(url);
    if (!image) {
      const objectUrl = signal<string | null>(null);
      this.loaded.set(url, objectUrl);
      this.http.get(`${environment.apiBaseUrl}${url}`, { responseType: 'blob' }).subscribe({
        next: (blob) => objectUrl.set(URL.createObjectURL(blob)),
        // Left null, so the avatar keeps its fallback; the next page load tries again.
        error: () => {},
      });
      image = objectUrl;
    }
    return image;
  }
}
