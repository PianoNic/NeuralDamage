import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmButton } from '@spartan-ng/helm/button';
import { AuthService } from '@app/shared/auth/auth.service';

@Component({
  selector: 'app-callback',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, HlmSpinner, HlmButton],
  templateUrl: './callback.html',
})
export class CallbackComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly error = signal<string | null>(null);

  async ngOnInit() {
    await this.auth.checkAuth();
    if (this.auth.isAuthenticated()) {
      this.router.navigate(['/']);
    } else {
      this.error.set('Authentication failed. Please try again.');
    }
  }
}
