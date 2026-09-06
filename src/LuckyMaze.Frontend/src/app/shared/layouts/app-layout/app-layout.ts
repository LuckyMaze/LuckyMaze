import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { Sidenav } from '../../../sidenav/sidenav';
import { BottomNav } from '../../components/bottom-nav/bottom-nav';

@Component({
  selector: 'luckymaze-app-layout',
  imports: [RouterOutlet, HlmSidebarImports, Sidenav, BottomNav],
  templateUrl: './app-layout.html',
})
export class AppLayout {}
