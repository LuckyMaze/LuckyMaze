import { ChangeDetectionStrategy, Component, computed, inject, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideHouse,
  lucideChevronsUpDown,
  lucideLogOut,
  lucideSun,
  lucideMoon,
  lucideMonitor,
  lucideGamepad2,
  lucideTrophy,
  lucideHistory,
} from '@ng-icons/lucide';
import { ThemeService, ThemeMode, THEME_OPTIONS } from '../shared/services/theme.service';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { UserStore } from '../shared/stores/UserStore.store';
import { AuthService } from '../shared/auth/auth.service';

@Component({
  selector: 'luckymaze-sidenav',
  imports: [HlmSidebarImports, HlmDropdownMenuImports, HlmAvatarImports, NgIcon, RouterLink],
  providers: [
    provideIcons({
      lucideHouse,
      lucideChevronsUpDown,
      lucideLogOut,
      lucideSun,
      lucideMoon,
      lucideMonitor,
      lucideGamepad2,
      lucideTrophy,
      lucideHistory,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sidenav.html',
})
export class Sidenav implements OnInit {
  private readonly sidebarService = inject(HlmSidebarService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly theme = inject(ThemeService);
  protected readonly userStore = inject(UserStore);

  ngOnInit(): void {
    void this.userStore.load();
  }

  protected readonly themeMode = this.theme.mode;
  protected readonly themeOptions = THEME_OPTIONS;
  protected readonly menuSide = computed(() => (this.sidebarService.isMobile() ? 'top' : 'right'));

  protected readonly user = computed(() => {
    const u = this.userStore.currentUser();
    return {
      name: u?.displayName ?? u?.email ?? '',
      email: u?.email ?? '',
      avatar: u?.avatarUrl ?? '',
    };
  });

  protected setTheme(mode: ThemeMode): void {
    this.theme.set(mode);
  }

  protected async logout(): Promise<void> {
    await this.authService.logout();
    await this.router.navigateByUrl('/login');
  }
}
