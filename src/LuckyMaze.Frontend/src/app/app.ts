import { Component, DestroyRef, effect, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { HlmToaster } from '@spartan-ng/helm/sonner';
import { UserService } from './api/api/user.service';
import { AuthService } from './shared/auth/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HlmToaster],
  templateUrl: './app.html',
})
export class App {
  private readonly authService = inject(AuthService);
  private readonly userService = inject(UserService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    effect(() => {
      if (!this.authService.isAuthenticated()) return;

      this.userService
        .apiUserSyncGet()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe();
    });
  }
}
