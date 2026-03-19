import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './home.html',
  host: { class: 'flex flex-1' },
})
export class HomeComponent {}
