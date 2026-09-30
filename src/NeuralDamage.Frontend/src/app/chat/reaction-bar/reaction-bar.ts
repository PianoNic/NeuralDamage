import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { ReactionGroupDto } from '@app/models';

@Component({
  selector: 'app-reaction-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton],
  templateUrl: './reaction-bar.html',
})
export class ReactionBarComponent {
  readonly reactions = input.required<ReactionGroupDto[]>();
  readonly currentUserId = input<string | null>(null);
  readonly toggleReaction = output<string>();

  isMine(reaction: ReactionGroupDto): boolean {
    const userId = this.currentUserId();
    return !!userId && reaction.userIds.includes(userId);
  }

  onToggle(emoji: string): void {
    this.toggleReaction.emit(emoji);
  }
}
