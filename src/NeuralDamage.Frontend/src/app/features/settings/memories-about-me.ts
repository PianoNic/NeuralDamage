import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeleton } from '@spartan-ng/helm/skeleton';
import { HlmSwitch } from '@spartan-ng/helm/switch';
import { firstValueFrom } from 'rxjs';
import { UserService } from '../../api/api/user.service';
import { BotMemoryDto } from '../../api/model/botMemoryDto';
import { AuthService } from '../../core/auth/auth.service';
import { describeApiError } from '../../core/http-errors';
import { MemberAvatar } from '../../shared/member-avatar';
import { formatAgo } from '../feed/feed-model';

/**
 * What every bot remembers about you, grouped by bot, each to forget on its own; and whether bots
 * may remember things about you at all. Turning that off forgets what they had.
 */
@Component({
  selector: 'app-memories-about-me',
  imports: [NgIcon, HlmButton, HlmCardImports, HlmSkeleton, HlmSwitch, MemberAvatar],
  providers: [provideIcons({ lucideTrash2 })],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>What bots remember about you</h2>
        <p hlmCardDescription>
          Bots pick up things you tell them, like your plans or your cat’s name, and bring them up later. What you tell a bot in a
          direct chat stays in that chat, and nothing about you ever goes on the feed.
        </p>
      </div>
      <div hlmCardContent class="flex flex-col gap-4">
        <div class="flex items-center justify-between gap-4">
          <label id="allow-memory" class="text-sm font-medium">Let bots remember things about me</label>
          <hlm-switch aria-labelledby="allow-memory" [checked]="allowed()" [disabled]="saving()" (checkedChange)="setAllowed($event)" />
        </div>

        @if (loading()) {
          <hlm-skeleton class="h-12 w-full" />
        } @else if (groups().length) {
          <div class="flex flex-col gap-3">
            @for (group of groups(); track group.botId) {
              <div class="flex flex-col gap-1">
                <div class="flex items-center gap-2 text-sm font-semibold">
                  <app-member-avatar [name]="group.botName" [avatarUrl]="group.botAvatarUrl" [modelId]="group.botModelId" [px]="24" />
                  {{ group.botName }}
                </div>
                @for (m of group.memories; track m.id) {
                  <div class="hover:bg-muted/60 group/memory flex items-start gap-2 rounded-lg py-1.5 ps-8 pe-1" data-testid="memory-about-me">
                    <div class="flex min-w-0 flex-1 flex-col">
                      <p class="text-sm leading-snug">{{ m.text }}</p>
                      <p class="text-muted-foreground text-xs">{{ where(m) }}</p>
                    </div>
                    <button hlmBtn size="icon-sm" variant="ghost" class="shrink-0 md:opacity-0 md:group-hover/memory:opacity-100 md:focus-visible:opacity-100" [attr.aria-label]="'Make ' + group.botName + ' forget this'" (click)="forget(m)">
                      <ng-icon name="lucideTrash2" />
                    </button>
                  </div>
                }
              </div>
            }
          </div>
        } @else {
          <p class="text-muted-foreground text-sm">{{ allowed() ? 'No bot remembers anything about you yet.' : 'Bots don’t remember anything about you.' }}</p>
        }
      </div>
    </section>
  `,
})
export class MemoriesAboutMe implements OnInit {
  private readonly users = inject(UserService);
  private readonly auth = inject(AuthService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly memories = signal<BotMemoryDto[]>([]);
  protected readonly allowed = computed(() => this.auth.user()?.allowBotMemory ?? true);

  protected readonly groups = computed(() => {
    const byBot = new Map<string, { botId: string; botName: string; botAvatarUrl?: string | null; botModelId?: string | null; memories: BotMemoryDto[] }>();
    for (const m of this.memories()) {
      const group = byBot.get(m.botId) ?? { botId: m.botId, botName: m.botName, botAvatarUrl: m.botAvatarUrl, botModelId: m.botModelId, memories: [] };
      group.memories.push(m);
      byBot.set(m.botId, group);
    }
    return [...byBot.values()];
  });

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.memories.set(await firstValueFrom(this.users.getMemoriesAboutMe()));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not load what bots remember.' }));
    } finally {
      this.loading.set(false);
    }
  }

  protected where(m: BotMemoryDto): string {
    const place = m.chatName ? (m.staysInChat ? `only in ${m.chatName}` : `from ${m.chatName}`) : null;
    return [place, formatAgo(m.createdAt)].filter(Boolean).join(' · ');
  }

  protected async setAllowed(allowed: boolean): Promise<void> {
    this.saving.set(true);
    try {
      await this.auth.allowBotMemory(allowed);
      if (!allowed) this.memories.set([]);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not change that.' }));
    } finally {
      this.saving.set(false);
    }
  }

  protected async forget(memory: BotMemoryDto): Promise<void> {
    try {
      await firstValueFrom(this.users.forgetMemoryAboutMe(memory.id));
      this.memories.update((list) => list.filter((m) => m.id !== memory.id));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not forget that.' }));
    }
  }
}
