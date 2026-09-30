import { Component, computed, inject, input } from '@angular/core';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { AvatarImages, isApiAvatar } from './avatar-images';
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

  private readonly images = inject(AvatarImages);

  protected readonly letters = computed(() => initials(this.name()));
  /** The member's own picture, once it has loaded. A bot's comes from the API with the token. */
  private readonly own = computed(() => {
    const url = this.avatarUrl();
    if (!url) return null;
    return isApiAvatar(url) ? this.images.get(url)() : url;
  });
  /** No picture of its own (yet), so the model vendor's icon stands in. */
  protected readonly brandIcon = computed(() => !this.own() && !!this.modelId());
  protected readonly src = computed(
    () => this.own() || (this.modelId() ? providerIconUrl({ id: this.modelId()! }) : null),
  );
  protected readonly fontSize = computed(() => Math.max(10, Math.round(this.px() * 0.38)));
}
