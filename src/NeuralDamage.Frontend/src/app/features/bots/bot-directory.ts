import { inject, Service, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { BotDto } from '../../core/models';

/** Every bot the signed-in user can add to a chat, shared by the sidebar count and the People sheet. */
@Service()
export class BotDirectory {
  private readonly api = inject(BotsService);

  readonly bots = signal<BotDto[]>([]);
  private loaded: Promise<void> | null = null;

  load(): Promise<void> {
    this.loaded ??= this.reload();
    return this.loaded;
  }

  async reload(): Promise<void> {
    try {
      this.bots.set(await firstValueFrom(this.api.apiBotsGet()));
    } catch {
      // The count and the picker stay empty; the People sheet reports its own failures.
    }
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.api.apiBotsBotIdDelete(id));
    this.bots.update((list) => list.filter((bot) => bot.id !== id));
  }
}
