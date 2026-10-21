import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLoader2 } from '@ng-icons/lucide';
import { classes } from '@spartan-ng/helm/utils';

@Component({
  selector: 'hlm-spinner',
  imports: [NgIcon],
  providers: [provideIcons({ lucideLoader2 })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'data-slot': 'spinner',
    role: 'status',
    '[attr.aria-label]': 'ariaLabel()',
  },
  // The icon fills the host's box, so whatever size a caller gives the host, the turning box is the
  // icon itself: square, and centred on its own middle.
  template: `<ng-icon class="block" size="100%" [name]="icon()" />`,
})
export class HlmSpinner {
  /**
   * The name of the icon to be used as the spinner.
   * Use provideIcons({ ... }) to register custom icons.
   */
  public readonly icon = input<string>('lucideLoader2');

  /** Aria label for the spinner for accessibility. */
  public readonly ariaLabel = input<string>('Loading', { alias: 'aria-label' });

  constructor() {
    // A square inline-block that spins about its centre. A size class from the caller (size-3)
    // replaces size-4 and the icon follows; before, the icon kept its own 16px inside a smaller box
    // and swung around the box's middle instead of its own.
    classes(() => 'inline-block size-4 shrink-0 origin-center leading-none motion-safe:animate-spin');
  }
}
