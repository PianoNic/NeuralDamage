import { Component, computed, input } from '@angular/core';
import { ChatDto } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';

/** A direct chat goes by who it is with; a group by its name. */
export function chatTitle(chat: ChatDto): string {
  return (chat.isDirect && chat.preview?.members[0]?.name) || chat.name;
}

/**
 * A chat's face at any size (`px`): a group's own picture, else who is in it, two of them stacked
 * as on Instagram, else its initials.
 */
@Component({
  selector: 'app-chat-avatar',
  imports: [MemberAvatar],
  host: { class: 'relative block shrink-0', '[style.width.px]': 'px()', '[style.height.px]': 'px()' },
  template: `
    @let members = faces();
    @if (members.length >= 2) {
      <span class="absolute start-0 top-0 flex">
        <app-member-avatar [name]="members[0].name" [avatarUrl]="members[0].avatarUrl" [modelId]="members[0].modelId" [px]="pairPx()" />
      </span>
      <span class="ring-background absolute end-0 bottom-0 flex rounded-full ring-2">
        <app-member-avatar [name]="members[1].name" [avatarUrl]="members[1].avatarUrl" [modelId]="members[1].modelId" [px]="pairPx()" />
      </span>
    } @else if (members.length === 1) {
      <app-member-avatar [name]="members[0].name" [avatarUrl]="members[0].avatarUrl" [modelId]="members[0].modelId" [px]="px()" />
    } @else {
      <app-member-avatar [name]="title()" [avatarUrl]="ownPicture()" [px]="px()" />
    }
  `,
})
export class ChatAvatar {
  readonly chat = input.required<ChatDto>();
  readonly px = input(48);

  protected readonly title = computed(() => chatTitle(this.chat()));
  /** A group's own picture; a direct chat always shows who it is with. */
  protected readonly ownPicture = computed(() => (this.chat().isDirect ? null : (this.chat().avatarUrl ?? null)));
  protected readonly faces = computed(() => (this.ownPicture() ? [] : (this.chat().preview?.members ?? [])));
  protected readonly pairPx = computed(() => Math.round(this.px() * 0.7));
}
