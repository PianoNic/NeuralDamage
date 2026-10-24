import { Component, computed, effect, inject, input, model, OnDestroy, output, signal, untracked } from '@angular/core';
import { form, FormField, FormRoot, maxLength, requiredError, SchemaPathTree, validate } from '@angular/forms/signals';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImage, lucideSearch, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { firstValueFrom } from 'rxjs';
import { UserService } from '../../api/api/user.service';
import { UserDto } from '../../api/model/userDto';
import { AuthService } from '../../core/auth/auth.service';
import { describeApiError } from '../../core/http-errors';
import { cropAvatar } from '../../shared/crop-avatar';
import { MemberAvatar } from '../../shared/member-avatar';
import { BotDirectory } from '../bots/bot-directory';
import { ChatList } from './chat-list';

/** The server's limit on a chat name (UpdateChatValidator.MaxNameLength). */
export const MAX_CHAT_NAME = 256;

/** A chat name needs something besides spaces, and fits the server's limit. */
export function chatNameRules(path: SchemaPathTree<{ name: string }>): void {
  validate(path.name, ({ value }) => (value().trim() ? null : requiredError()));
  maxLength(path.name, MAX_CHAT_NAME);
}

/** The chat a dialog acts on. */
export interface ChatRef {
  id: string;
  name: string;
  /** A group chat's own picture, for the edit dialog. */
  avatarUrl?: string | null;
}

/** A picture picked in a dialog and not saved yet; `removed` when the current one is to go. */
export type StagedPicture = { blob: Blob; url: string } | 'removed' | null;

/**
 * A group chat's picture in a dialog: the one it has or the one just picked, with Change and
 * Remove. It is cropped square in the browser, as a bot's is, and saved with the dialog.
 */
@Component({
  selector: 'app-chat-picture-field',
  imports: [NgIcon, HlmButton, MemberAvatar],
  providers: [provideIcons({ lucideImage, lucideTrash2 })],
  host: { class: 'flex items-center gap-4' },
  template: `
    <app-member-avatar [name]="name() || '?'" [avatarUrl]="shown()" [px]="72" />
    <div class="flex flex-wrap gap-2">
      <button hlmBtn variant="secondary" size="sm" type="button" (click)="file.click()">
        <ng-icon name="lucideImage" />
        {{ shown() ? 'Change picture' : 'Add a picture' }}
      </button>
      @if (shown()) {
        <button hlmBtn variant="ghost" size="icon-sm" type="button" aria-label="Remove picture" (click)="remove()">
          <ng-icon name="lucideTrash2" />
        </button>
      }
    </div>
    <input #file type="file" class="hidden" accept="image/png,image/jpeg,image/webp,image/gif" (change)="pick($event)" />
  `,
})
export class ChatPictureField implements OnDestroy {
  /** For the initials while there is no picture. */
  readonly name = input('');
  /** The picture the chat has now. */
  readonly current = input<string | null | undefined>(null);
  readonly staged = model<StagedPicture>(null);

  protected readonly shown = computed(() => {
    const staged = this.staged();
    if (staged === 'removed') return null;
    return staged?.url ?? this.current() ?? null;
  });

  protected async pick(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    try {
      const blob = await cropAvatar(file);
      this.release();
      this.staged.set({ blob, url: URL.createObjectURL(blob) });
    } catch {
      toast.error('That picture could not be read.');
    }
  }

  protected remove(): void {
    this.release();
    this.staged.set(this.current() ? 'removed' : null);
  }

  ngOnDestroy(): void {
    this.release();
  }

  private release(): void {
    const staged = this.staged();
    if (staged && staged !== 'removed') URL.revokeObjectURL(staged.url);
  }
}

/**
 * Starts a chat, as Instagram's New message does: a direct chat with one bot or person, opened
 * straight away (the one there already is, if any), or a named group with a picture if you like.
 * Open while `open` is true.
 */
@Component({
  selector: 'app-new-chat-dialog',
  imports: [
    FormRoot,
    FormField,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmDialogImports,
    HlmFieldImports,
    HlmInput,
    HlmInputGroupImports,
    HlmItemImports,
    HlmSpinner,
    HlmTabsImports,
    ChatPictureField,
    MemberAvatar,
  ],
  providers: [provideIcons({ lucideSearch })],
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="w-full sm:w-md sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>New chat</h2>
          <p hlmDialogDescription>Message one bot or person, or start a group chat.</p>
        </hlm-dialog-header>
        <hlm-tabs [tab]="mode()" (tabActivated)="mode.set($any($event))" class="min-w-0">
          <hlm-tabs-list class="w-full">
            <button hlmTabsTrigger="direct">Direct message</button>
            <button hlmTabsTrigger="group">Group chat</button>
          </hlm-tabs-list>

          <!-- Both tabs are the same height, so switching does not resize the dialog. -->
          <div hlmTabsContent="direct" class="flex h-[min(26rem,60dvh)] min-w-0 flex-none flex-col gap-2 pt-2">
            <div hlmInputGroup>
              <input
                hlmInputGroupInput
                type="search"
                placeholder="Search bots and people"
                aria-label="Search bots and people"
                [value]="query()"
                (input)="search($any($event.target).value)"
              />
              <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
            </div>
            <div hlmItemGroup class="-mx-2 min-h-0 flex-1 overflow-y-auto" role="list">
              @for (pick of picks(); track pick.key) {
                <button hlmItem size="sm" type="button" class="flex-nowrap" [disabled]="!!opening()" (click)="message(pick)">
                  <div hlmItemMedia>
                    <app-member-avatar [name]="pick.name" [avatarUrl]="pick.avatarUrl" [modelId]="pick.modelId" [px]="40" />
                  </div>
                  <div hlmItemContent class="min-w-0">
                    <div hlmItemTitle class="w-full">
                      <span class="truncate">{{ pick.name }}</span>
                      @if (pick.botId) {
                        <span hlmBadge class="bg-bot text-bot-foreground h-4 rounded px-1 text-[10px] font-extrabold tracking-wide">BOT</span>
                      }
                    </div>
                    @if (pick.about) {
                      <p hlmItemDescription class="truncate">{{ pick.about }}</p>
                    }
                  </div>
                  @if (opening() === pick.key) {
                    <hlm-spinner class="ms-auto" />
                  }
                </button>
              } @empty {
                <p class="text-muted-foreground px-2 py-6 text-center text-sm">
                  {{ query().trim() ? 'Nobody matches.' : 'No bots or people yet.' }}
                </p>
              }
            </div>
          </div>

          <form hlmTabsContent="group" [formRoot]="chatForm" class="flex h-[min(26rem,60dvh)] flex-none flex-col gap-4 pt-2">
            <app-chat-picture-field [name]="model().name" [(staged)]="picture" />
            <div hlmField>
              <label hlmFieldLabel for="new-chat-name">Name</label>
              <input hlmInput id="new-chat-name" placeholder="e.g. Friday night plans" [formField]="chatForm.name" />
            </div>
            <hlm-dialog-footer class="mt-auto">
              <button hlmBtn variant="outline" type="button" (click)="ctx.close()">Cancel</button>
              <button hlmBtn type="submit" [disabled]="chatForm().invalid() || saving()">
                @if (saving()) {
                  <hlm-spinner />
                }
                Create
              </button>
            </hlm-dialog-footer>
          </form>
        </hlm-tabs>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class NewChatDialog {
  private readonly chatList = inject(ChatList);
  private readonly router = inject(Router);
  private readonly bots = inject(BotDirectory);
  private readonly users = inject(UserService);
  private readonly auth = inject(AuthService);

  readonly open = input(false);
  readonly closed = output();

  protected readonly mode = signal<'direct' | 'group'>('direct');
  protected readonly query = signal('');
  private readonly people = signal<UserDto[]>([]);
  /** The pick being opened, so a second click waits for it. */
  protected readonly opening = signal<string | null>(null);

  /** Public bots, then people, narrowed by the search. */
  protected readonly picks = computed<Pick[]>(() => {
    const query = this.query().trim().toLowerCase();
    const bots = this.bots
      .bots()
      .filter((bot) => bot.isActive && bot.isPublic && (!query || bot.name.toLowerCase().includes(query)))
      .map((bot) => ({ key: 'b' + bot.id, botId: bot.id, name: bot.name, avatarUrl: bot.avatarUrl, modelId: bot.modelId, about: bot.personality }));
    const me = this.auth.user()?.id;
    const people = this.people()
      .filter((user) => user.id !== me)
      .map((user) => ({ key: 'u' + user.id, userId: user.id, name: user.displayName || user.email, avatarUrl: user.avatarUrl, about: user.email }));
    return [...bots, ...people];
  });

  protected readonly saving = signal(false);
  protected readonly model = signal({ name: '' });
  protected readonly picture = signal<StagedPicture>(null);
  protected readonly chatForm = form(this.model, chatNameRules, {
    submission: { action: () => this.save() },
  });

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      if (this.open())
        untracked(() => {
          this.mode.set('direct');
          this.query.set('');
          this.chatForm().reset({ name: '' });
          this.picture.set(null);
          void this.bots.load();
          void this.findPeople('');
        });
    });
  }

  protected search(value: string): void {
    this.query.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.findPeople(value.trim()), 250);
  }

  private async findPeople(search: string): Promise<void> {
    try {
      this.people.set(await firstValueFrom(this.users.getUsers(search || undefined)));
    } catch {
      this.people.set([]);
    }
  }

  protected async message(pick: Pick): Promise<void> {
    if (this.opening()) return;
    this.opening.set(pick.key);
    try {
      const chat = await this.chatList.openDirect(pick.botId ? { botId: pick.botId } : { userId: pick.userId });
      this.closed.emit();
      await this.router.navigate(['/chats', chat.id]);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not open a chat with ${pick.name}.` }));
    } finally {
      this.opening.set(null);
    }
  }

  private async save(): Promise<undefined> {
    const name = this.model().name.trim();
    if (!name) return undefined;
    this.saving.set(true);
    try {
      const chat = await this.chatList.create(name);
      const picture = this.picture();
      if (chat && picture && picture !== 'removed') {
        // The chat is made either way; a picture that fails can be added again from Edit.
        await this.chatList.setAvatar(chat.id, picture.blob).catch((error: unknown) =>
          toast.error(describeApiError(error, { fallback: 'The chat is made, but its picture could not be saved.' })),
        );
      }
      this.closed.emit();
      if (chat) await this.router.navigate(['/chats', chat.id]);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not create the chat.' }));
    } finally {
      this.saving.set(false);
    }
    return undefined;
  }
}

/** Someone the New chat dialog can message: a public bot or a person. */
interface Pick {
  key: string;
  botId?: string;
  userId?: string;
  name: string;
  avatarUrl?: string | null;
  modelId?: string | null;
  about?: string | null;
}

/** Edits a group chat's name and picture; open while a chat is given. */
@Component({
  selector: 'app-edit-chat-dialog',
  imports: [FormRoot, FormField, HlmButton, HlmDialogImports, HlmFieldImports, HlmInput, HlmSpinner, ChatPictureField],
  template: `
    <hlm-dialog [state]="chat() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="w-full sm:max-w-md">
        <form [formRoot]="editForm" class="flex flex-col gap-4">
          <hlm-dialog-header>
            <h2 hlmDialogTitle>Edit chat</h2>
            <p hlmDialogDescription>Everyone in it sees the new name and picture.</p>
          </hlm-dialog-header>
          <app-chat-picture-field [name]="model().name" [current]="chat()?.avatarUrl" [(staged)]="picture" />
          <div hlmField>
            <label hlmFieldLabel for="chat-name">Name</label>
            <input hlmInput id="chat-name" [formField]="editForm.name" />
          </div>
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="ctx.close()">Cancel</button>
            <button hlmBtn type="submit" [disabled]="editForm().invalid() || saving()">
              @if (saving()) {
                <hlm-spinner />
              }
              Save
            </button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class EditChatDialog {
  private readonly chatList = inject(ChatList);

  readonly chat = input<ChatRef | null>(null);
  readonly closed = output();

  protected readonly saving = signal(false);
  protected readonly model = signal({ name: '' });
  protected readonly picture = signal<StagedPicture>(null);
  protected readonly editForm = form(this.model, chatNameRules, {
    submission: { action: () => this.save() },
  });

  constructor() {
    effect(() => {
      const chat = this.chat();
      if (chat)
        untracked(() => {
          this.editForm().reset({ name: chat.name });
          this.picture.set(null);
        });
    });
  }

  private async save(): Promise<undefined> {
    const chat = this.chat();
    const name = this.model().name.trim();
    if (!chat || !name) return undefined;

    this.saving.set(true);
    try {
      if (name !== chat.name) await this.chatList.rename(chat.id, name);
      const picture = this.picture();
      if (picture === 'removed') await this.chatList.removeAvatar(chat.id);
      else if (picture) await this.chatList.setAvatar(chat.id, picture.blob);
      this.closed.emit();
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not save the chat.' }));
    } finally {
      this.saving.set(false);
    }
    return undefined;
  }
}

/** Asks before deleting `chat` for everyone; open while a chat is given. */
@Component({
  selector: 'app-delete-chat-dialog',
  imports: [HlmAlertDialogImports],
  template: `
    <hlm-alert-dialog [state]="chat() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <hlm-alert-dialog-header>
          <h2 hlmAlertDialogTitle>Delete chat?</h2>
          <p hlmAlertDialogDescription>
            “{{ chat()?.name }}” and its messages are removed for everyone. This cannot be undone.
          </p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="destructive" (click)="remove(); ctx.close()">
            Delete
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class DeleteChatDialog {
  private readonly chatList = inject(ChatList);
  private readonly router = inject(Router);

  readonly chat = input<ChatRef | null>(null);
  readonly closed = output();

  protected async remove(): Promise<void> {
    const chat = this.chat();
    if (!chat) return;
    try {
      await this.chatList.remove(chat.id);
      if (this.chatList.activeId() === chat.id) await this.router.navigateByUrl('/chats');
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not delete the chat.' }));
    }
  }
}
