import { ChangeDetectionStrategy, Component } from '@angular/core';
import { HlmSidebarTrigger } from '@spartan-ng/helm/sidebar';

@Component({
  selector: 'app-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSidebarTrigger],
  templateUrl: './home.html',
  host: { class: 'flex flex-1' },
})
export class HomeComponent {}
