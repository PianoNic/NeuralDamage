import { Component, computed, input } from '@angular/core';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { BotDto, ChatMember } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';
import { memberSubtitle, roleLabel } from './people-meta';

/** Avatar, name, Bot badge or role, and the line under it: the top of a profile or mention card. */
@Component({
  selector: 'app-member-summary',
  imports: [HlmBadge, MemberAvatar],
  host: { class: 'flex min-w-0 items-center gap-3' },
  template: `
    <app-member-avatar [name]="member().displayName" [avatarUrl]="member().avatarUrl" [modelId]="member().modelId" [px]="px()" />
    <div class="flex min-w-0 flex-1 flex-col gap-0.5">
      <div class="flex items-center gap-1.5">
        <span class="truncate text-base font-semibold">{{ member().displayName }}</span>
        <span hlmBadge variant="secondary">{{ badge() }}</span>
      </div>
      <span class="text-muted-foreground truncate text-[13px]">{{ subtitle() }}</span>
    </div>
  `,
})
export class MemberSummary {
  readonly member = input.required<ChatMember>();
  readonly bot = input<BotDto | null>(null);
  readonly currentUserId = input<string | null>(null);
  readonly px = input(48);

  protected readonly badge = computed(() =>
    this.member().memberType === 'bot' ? 'Bot' : roleLabel(this.member().role),
  );
  protected readonly subtitle = computed(() => memberSubtitle(this.member(), this.bot(), this.currentUserId()));
}
