import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { ReactionGroup } from '@app/models';

@Component({
  selector: 'app-reaction-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton],
  templateUrl: './reaction-bar.html',
})
export class ReactionBarComponent {
  readonly reactions = input.required<ReactionGroup[]>();
  readonly toggleReaction = output<string>();

  onToggle(emoji: string): void {
    this.toggleReaction.emit(emoji);
  }
}
