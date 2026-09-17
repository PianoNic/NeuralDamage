import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmSidebarInset, HlmSidebarWrapper } from '@spartan-ng/helm/sidebar';
import { SidebarComponent } from '@app/shared/sidebar/sidebar';

@Component({
  selector: 'app-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, HlmSidebarWrapper, HlmSidebarInset, SidebarComponent],
  templateUrl: './layout.html',
})
export class LayoutComponent {}
