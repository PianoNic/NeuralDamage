import { afterNextRender, Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLayers, lucideX } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { AppConfig } from '../../core/app-config';
import { AuthService } from '../../core/auth/auth.service';
import { toastApiError } from '../../core/http-errors';
import { StickerDto, StorySpec } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';
import { BotDirectory } from '../bots/bot-directory';
import { Composer } from '../chat/composer';
import { StoryEditor } from '../stories/story-editor';
import { StoryDraft } from '../stories/story-model';
import { feedMembers, mentionNames } from './feed-members';
import { FeedStore } from './feed-store';
import { GenerateImage, GeneratedImageTile, ImageDraft } from './generate-image';

/**
 * `/feed/new`: write a post. Text with @mentions and emoji, a sticker, or a picture: one drawn from
 * a prompt, or composed from layers. Posting goes back to the feed.
 */
@Component({
  selector: 'app-new-post',
  imports: [
    RouterLink,
    NgIcon,
    HlmButton,
    HlmDialogImports,
    HlmTooltipImports,
    MemberAvatar,
    Composer,
    GenerateImage,
    GeneratedImageTile,
    StoryEditor,
  ],
  providers: [ImageDraft, provideIcons({ lucideLayers, lucideX })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-14 shrink-0 items-center gap-2 border-b px-2">
      <a hlmBtn variant="ghost" size="icon" routerLink="/" aria-label="Discard post">
        <ng-icon name="lucideX" />
      </a>
      <h1 class="font-display flex-1 text-xl font-extrabold tracking-tight">New post</h1>
    </header>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <section class="mx-auto flex w-full max-w-xl items-start gap-3 p-4" aria-label="New post">
        <app-member-avatar [name]="auth.displayName() || '?'" [avatarUrl]="auth.user()?.avatarUrl" [px]="40" />
        <app-composer
          class="min-w-0 flex-1"
          [feed]="true"
          [pill]="false"
          [limit]="2000"
          [members]="members()"
          [mentionNames]="names()"
          [currentUserId]="me()"
          placeholder="Write a caption, the bots are watching"
          [attachment]="draft.pending() ? 'busy' : draft.ready() ? 'ready' : 'none'"
          (send)="post($event.content)"
          (sendSticker)="postSticker($event)"
        >
          @if (draft.image(); as image) {
            <app-generated-image-tile
              composerAttachment
              [image]="image"
              [editable]="image.kind === 'Composed' && !!composed()"
              (discard)="discardImage()"
              (edit)="composeOpen.set(true)"
            />
          }
          <button
            composerTool
            hlmBtn
            variant="ghost"
            size="icon-sm"
            type="button"
            aria-label="Create image"
            hlmTooltip="Create image"
            [disabled]="draft.pending()"
            (click)="composeOpen.set(true)"
          >
            <ng-icon name="lucideLayers" />
          </button>
          @if (imageGeneration()) {
            <app-generate-image composerTool [bots]="bots.bots()" [busy]="draft.pending()" (generate)="draft.generate($event)" />
          }
        </app-composer>
      </section>
      <p class="text-muted-foreground mx-auto max-w-xl px-4 text-xs">Bots see your post and may comment.</p>
    </div>

    <hlm-dialog [state]="composeOpen() ? 'open' : 'closed'" (closed)="composeOpen.set(false)">
      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="flex max-h-[94vh] w-[min(96vw,72rem)] max-w-none flex-col overflow-hidden p-0 sm:max-w-none"
        data-testid="compose-dialog"
      >
        <h2 hlmDialogTitle class="sr-only">Create image</h2>
        <app-story-editor
          class="min-h-0 flex-1"
          mode="post"
          [initial]="composed()"
          [working]="draft.composing()"
          (compose)="useComposed($event)"
          (cancel)="composeOpen.set(false)"
        />
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class NewPostPage implements OnInit {
  private readonly router = inject(Router);
  protected readonly store = inject(FeedStore);
  protected readonly auth = inject(AuthService);
  protected readonly bots = inject(BotDirectory);
  protected readonly draft = inject(ImageDraft);
  protected readonly imageGeneration = inject(AppConfig).imageGeneration;

  protected readonly me = computed(() => this.auth.user()?.id ?? null);
  protected readonly members = computed(() => feedMembers(this.bots.bots(), this.store.posts()));
  protected readonly names = computed(() => mentionNames(this.members(), this.bots.bots()));
  /** The layer editor for the post's picture is open. */
  protected readonly composeOpen = signal(false);
  /** The layers of the picture composed for the post being written, to open it again. */
  protected readonly composed = signal<StoryDraft | null>(null);

  private readonly composer = viewChild(Composer);

  constructor() {
    afterNextRender(() => this.composer()?.focus());
  }

  ngOnInit(): void {
    void this.store.start();
    void this.bots.load();
  }

  /** Renders the layers into the post's picture and closes the editor. */
  protected async useComposed(result: { spec: StorySpec; draft: StoryDraft }): Promise<void> {
    if (!(await this.draft.compose(result.spec))) return;
    this.composed.set(result.draft);
    this.composeOpen.set(false);
  }

  protected async discardImage(): Promise<void> {
    this.composed.set(null);
    await this.draft.discard();
  }

  protected async post(content: string): Promise<void> {
    const image = this.draft.image();
    const imageId = this.draft.take();
    try {
      await this.store.post(content, null, imageId);
      if (imageId) this.composed.set(null);
      void this.router.navigate(['/']);
    } catch (error) {
      if (imageId && image) this.draft.restore(image);
      toastApiError(error, { network: 'Not posted. Check your connection and try again.' });
    }
  }

  protected async postSticker(sticker: StickerDto): Promise<void> {
    try {
      await this.store.post('', sticker.id);
      void this.router.navigate(['/']);
    } catch (error) {
      toastApiError(error, { network: 'Not posted. Check your connection and try again.' });
    }
  }
}
