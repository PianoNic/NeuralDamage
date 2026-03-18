import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-typing-indicator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './typing-indicator.html',
})
export class TypingIndicatorComponent {
  readonly typingUsers = input.required<Set<string>>();
  readonly typingBots = input.required<Map<string, string>>();

  readonly typingText = computed(() => {
    const names: string[] = [
      ...this.typingUsers(),
      ...this.typingBots().values(),
    ];
    if (names.length === 0) return '';
    if (names.length === 1) return `${names[0]} is typing...`;
    return `${names.join(', ')} are typing...`;
  });
}
