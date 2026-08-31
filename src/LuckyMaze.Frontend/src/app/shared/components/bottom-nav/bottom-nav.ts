import { ChangeDetectionStrategy, Component, computed, inject, OnInit } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideGamepad2,
  lucideTrophy,
  lucideHistory,
  lucideSettings,
  lucideCircleUser,
  lucideLogOut,
  lucideSun,
  lucideMoon,
  lucideMonitor,
} from '@ng-icons/lucide';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { AuthService } from '../../auth/auth.service';
import { ThemeService, THEME_OPTIONS } from '../../services/theme.service';
import { UserStore } from '../../stores/UserStore.store';

/**
 * Primary navigation on small screens: a persistent bottom tab bar, the pattern people already
 * know from every native app, instead of a drawer hidden behind a menu button. The account menu
 * (theme, sign out) rides along as its own tab rather than living in a sidebar footer that no
 * longer exists at this width.
 */
@Component({
  selector: 'luckymaze-bottom-nav',
  imports: [RouterLink, RouterLinkActive, NgIcon, HlmDropdownMenuImports],
  providers: [
    provideIcons({
      lucideGamepad2,
      lucideTrophy,
      lucideHistory,
      lucideSettings,
      lucideCircleUser,
      lucideLogOut,
      lucideSun,
      lucideMoon,
      lucideMonitor,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bottom-nav.html',
})
export class BottomNav implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly theme = inject(ThemeService);
  protected readonly userStore = inject(UserStore);

  ngOnInit(): void {
    void this.userStore.load();
  }

  protected readonly themeMode = this.theme.mode;
  protected readonly themeOptions = THEME_OPTIONS;

  protected readonly isAdmin = computed(() => this.userStore.currentUser()?.role === 'Admin');

  protected setTheme(mode: (typeof THEME_OPTIONS)[number]['mode']): void {
    this.theme.set(mode);
  }

  protected async logout(): Promise<void> {
    await this.authService.logout();
    await this.router.navigateByUrl('/login');
  }
}
