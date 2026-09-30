import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { form, FormField, FormRoot, maxLength, requiredError, SchemaPathTree, validate } from '@angular/forms/signals';
import { Router } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { describeApiError } from '../../core/http-errors';
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
}

/** Names a new chat and opens it; open while `open` is true. */
@Component({
  selector: 'app-new-chat-dialog',
  imports: [FormRoot, FormField, HlmButton, HlmDialogImports, HlmFieldImports, HlmInput, HlmSpinner],
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="w-full sm:max-w-md">
        <form [formRoot]="chatForm" class="flex flex-col gap-4">
          <hlm-dialog-header>
            <h2 hlmDialogTitle>New chat</h2>
            <p hlmDialogDescription>Give the conversation a name. You can add bots and people next.</p>
          </hlm-dialog-header>
          <div hlmField>
            <label hlmFieldLabel for="new-chat-name">Name</label>
            <input hlmInput id="new-chat-name" placeholder="e.g. Friday night plans" [formField]="chatForm.name" />
          </div>
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="ctx.close()">Cancel</button>
            <button hlmBtn type="submit" [disabled]="chatForm().invalid() || saving()">
              @if (saving()) {
                <hlm-spinner />
              }
              Create
            </button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class NewChatDialog {
  private readonly chatList = inject(ChatList);
  private readonly router = inject(Router);

  readonly open = input(false);
  readonly closed = output();

  protected readonly saving = signal(false);
  private readonly model = signal({ name: '' });
  protected readonly chatForm = form(this.model, chatNameRules, {
    submission: { action: () => this.save() },
  });

  constructor() {
    effect(() => {
      if (this.open()) untracked(() => this.chatForm().reset({ name: '' }));
    });
  }

  private async save(): Promise<undefined> {
    const name = this.model().name.trim();
    if (!name) return undefined;
    this.saving.set(true);
    try {
      const chat = await this.chatList.create(name);
      this.closed.emit();
      if (chat) await this.router.navigate(['/chat', chat.id]);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not create the chat.' }));
    } finally {
      this.saving.set(false);
    }
    return undefined;
  }
}

/** Renames `chat`; open while a chat is given. */
@Component({
  selector: 'app-rename-chat-dialog',
  imports: [FormRoot, FormField, HlmButton, HlmDialogImports, HlmFieldImports, HlmInput, HlmSpinner],
  template: `
    <hlm-dialog [state]="chat() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="w-full sm:max-w-md">
        <form [formRoot]="renameForm" class="flex flex-col gap-4">
          <hlm-dialog-header>
            <h2 hlmDialogTitle>Rename chat</h2>
          </hlm-dialog-header>
          <div hlmField>
            <label hlmFieldLabel for="chat-name">Name</label>
            <input hlmInput id="chat-name" [formField]="renameForm.name" />
          </div>
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="ctx.close()">Cancel</button>
            <button hlmBtn type="submit" [disabled]="renameForm().invalid() || saving()">
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
export class RenameChatDialog {
  private readonly chatList = inject(ChatList);

  readonly chat = input<ChatRef | null>(null);
  readonly closed = output();

  protected readonly saving = signal(false);
  private readonly model = signal({ name: '' });
  protected readonly renameForm = form(this.model, chatNameRules, {
    submission: { action: () => this.save() },
  });

  constructor() {
    effect(() => {
      const chat = this.chat();
      if (chat) untracked(() => this.renameForm().reset({ name: chat.name }));
    });
  }

  private async save(): Promise<undefined> {
    const chat = this.chat();
    const name = this.model().name.trim();
    if (!chat || !name) return undefined;

    this.saving.set(true);
    try {
      await this.chatList.rename(chat.id, name);
      this.closed.emit();
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not rename the chat.' }));
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
      if (this.chatList.activeId() === chat.id) await this.router.navigateByUrl('/');
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not delete the chat.' }));
    }
  }
}
