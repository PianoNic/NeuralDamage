import { ChangeDetectionStrategy, Component, effect, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmTextarea } from '@spartan-ng/helm/textarea';
import { HlmLabel } from '@spartan-ng/helm/label';
import { HlmSlider } from '@spartan-ng/helm/slider';
import { HlmSelectContent, HlmSelectOption, HlmSelectTrigger, HlmSelectValue } from '@spartan-ng/helm/select';
import {
  HlmDialog, HlmDialogContent, HlmDialogDescription,
  HlmDialogFooter, HlmDialogHeader, HlmDialogTitle,
  HlmDialogClose,
} from '@spartan-ng/helm/dialog';
import { Bot, OpenRouterModel } from '@app/models';
import { BotsService } from '@app/api/api/bots.service';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-bot-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    HlmButton, HlmInput, HlmTextarea, HlmLabel, HlmSlider,
    HlmSelectContent, HlmSelectOption, HlmSelectTrigger, HlmSelectValue,
    HlmDialog, HlmDialogContent, HlmDialogDescription,
    HlmDialogFooter, HlmDialogHeader, HlmDialogTitle,
    HlmDialogClose,
  ],
  templateUrl: './bot-form.html',
})
export class BotFormComponent implements OnInit {
  private readonly botsApi = inject(BotsService);

  readonly bot = input<Bot | null>(null);
  readonly saved = output<void>();
  readonly cancel = output<void>();

  readonly name = signal('');
  readonly modelId = signal('');
  readonly systemPrompt = signal('');
  readonly personality = signal('');
  readonly temperature = signal(0.7);
  readonly aliases = signal('');
  readonly availableModels = signal<OpenRouterModel[]>([]);
  readonly loading = signal(false);
  readonly isEditing = signal(false);

  constructor() {
    effect(() => {
      const b = this.bot();
      if (b) {
        this.isEditing.set(true);
        this.name.set(b.name);
        this.modelId.set(b.modelId);
        this.systemPrompt.set(b.systemPrompt);
        this.personality.set(b.personality ?? '');
        this.temperature.set(b.temperature);
        this.aliases.set((b as any).aliases ?? '');
      } else {
        this.isEditing.set(false);
      }
    });
  }

  async ngOnInit() {
    try {
      const models = (await firstValueFrom(this.botsApi.apiBotsModelsGet())) as OpenRouterModel[];
      this.availableModels.set(models);
    } catch {
      console.error('Failed to load models');
    }
  }

  async onSave() {
    this.loading.set(true);
    try {
      if (this.isEditing()) {
        const b = this.bot()!;
        await firstValueFrom(this.botsApi.apiBotsBotIdPut(b.id, {
          name: this.name(),
          modelId: this.modelId(),
          systemPrompt: this.systemPrompt(),
          personality: this.personality() || undefined,
          temperature: this.temperature(),
          aliases: this.aliases() || undefined,
        }));
      } else {
        await firstValueFrom(this.botsApi.apiBotsPost({
          name: this.name(),
          modelId: this.modelId(),
          systemPrompt: this.systemPrompt(),
          personality: this.personality() || undefined,
          temperature: this.temperature(),
          aliases: this.aliases() || undefined,
        }));
      }
      this.saved.emit();
    } catch (e) {
      console.error('Failed to save bot', e);
    }
    this.loading.set(false);
  }

  onCancel() {
    this.cancel.emit();
  }
}
