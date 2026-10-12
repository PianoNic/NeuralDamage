import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { BotDto } from '../../core/models';

/**
 * Full details of the bots in the open chat (character, nicknames, stats), by bot id. A chat member
 * only carries a summary; the People sheet, the mention card and nickname mentions read these.
 */
@Injectable({ providedIn: 'root' })
export class BotDetails {
  private readonly api = inject(BotsService);

  readonly byId = signal<ReadonlyMap<string, BotDto>>(new Map());
  private readonly inFlight = new Set<string>();

  /** Fetches these bots (again, for fresh stats); a failed one keeps what was there. */
  async load(ids: readonly string[]): Promise<void> {
    if (!ids.length) return;
    for (const id of ids) this.inFlight.add(id);
    const loaded = await Promise.all(ids.map((id) => firstValueFrom(this.api.apiBotsBotIdGet(id)).catch(() => null)));
    for (const id of ids) this.inFlight.delete(id);
    this.byId.update((map) => {
      const next = new Map(map);
      for (const bot of loaded) if (bot) next.set(bot.id, bot);
      return next;
    });
  }

  /** Fetches the bots not loaded (or loading) yet. */
  loadMissing(ids: readonly string[]): Promise<void> {
    const known = this.byId();
    return this.load([...new Set(ids)].filter((id) => !known.has(id) && !this.inFlight.has(id)));
  }
}
