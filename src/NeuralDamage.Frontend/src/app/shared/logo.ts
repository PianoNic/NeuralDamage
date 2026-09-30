import { Component, input } from '@angular/core';

/** The glitch bubble, the one coloured thing in the app. Decorative: the name always sits beside it. */
@Component({
  selector: 'app-logo',
  host: { class: 'inline-flex shrink-0' },
  template: `<img src="/icon.svg" alt="" [width]="size()" [height]="size()" class="block" />`,
})
export class Logo {
  readonly size = input(32);
}
