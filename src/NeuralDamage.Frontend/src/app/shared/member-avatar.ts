import { Component, computed, input } from '@angular/core';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { initials } from './initials';
import { providerIconUrl } from './provider-icon';

/**
 * A person's or bot's avatar at any size (`px`). A bot without a picture of
 * its own shows its model vendor's icon, as in the message list; everyone else falls back to initials.
 */
@Component({
  selector: 'app-member-avatar',
  imports: [HlmAvatarImports],
  host: { class: 'contents' },
  template: `
    <hlm-avatar class="shrink-0" [class.bg-background]="brandIcon()" [style.width.px]="px()" [style.height.px]="px()">
      @if (src()) {
        <img
          hlmAvatarImage
          [src]="src()"
          alt=""
          [style.padding.%]="brandIcon() ? 18 : null"
          [class.object-contain]="brandIcon()"
          [class.dark:invert]="brandIcon()"
        />
      }
      <span hlmAvatarFallback class="font-medium" [style.font-size.px]="fontSize()">{{ letters() }}</span>
    </hlm-avatar>
  `,
})
export class MemberAvatar {
  readonly name = input.required<string>();
  readonly avatarUrl = input<string | null | undefined>(null);
  /** A bot's model, for the vendor icon fallback. */
  readonly modelId = input<string | null | undefined>(null);
  /** Pixel size of the avatar. */
  readonly px = input(28);

  protected readonly letters = computed(() => initials(this.name()));
  protected readonly brandIcon = computed(() => !this.avatarUrl() && !!this.modelId());
  protected readonly src = computed(
    () => this.avatarUrl() || (this.modelId() ? providerIconUrl({ id: this.modelId()! }) : null),
  );
  protected readonly fontSize = computed(() => Math.max(10, Math.round(this.px() * 0.38)));
}
