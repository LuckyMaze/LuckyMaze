import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal, effect } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCheckCircle,
  lucideClock,
  lucideCoins,
  lucideInfo,
  lucideAlertTriangle,
  lucideGamepad2,
  lucideTrophy,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCard } from '@spartan-ng/helm/card';
import { HlmInput } from '@spartan-ng/helm/input';
import { GameStore } from '../shared/stores/game.store';
import { UserStore } from '../shared/stores/UserStore.store';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { MazeRenderer } from './maze-renderer';

const MIN_BET = 10;

@Component({
  selector: 'luckymaze-lobby',
  imports: [
    DecimalPipe,
    NgIcon,
    HlmAvatarImports,
    HlmBadge,
    HlmButton,
    HlmCard,
    HlmInput,
    ContentHeader,
    MazeRenderer,
  ],
  providers: [
    provideIcons({
      lucideCheckCircle,
      lucideClock,
      lucideCoins,
      lucideInfo,
      lucideAlertTriangle,
      lucideGamepad2,
      lucideTrophy,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './lobby.html',
})
export class LobbyComponent implements OnInit, OnDestroy {
  protected readonly gameStore = inject(GameStore);
  protected readonly userStore = inject(UserStore);

  constructor() {
    effect(() => {
      const state = this.gameStore.state();
      const seconds = this.gameStore.timerSeconds();
      if (state === 'Betting' && seconds > 0 && seconds <= 5) {
        this.playWarningBeep(seconds);
      }
    });
  }

  protected readonly selectedExit = signal('');
  protected readonly betAmount = signal(100);

  protected readonly currentUser = computed(() => this.userStore.currentUser());

  protected readonly currentInGamePlayer = computed(() => {
    const me = this.currentUser();
    if (!me) return null;
    return this.gameStore.players().find((p) => p.displayName === me.displayName) ?? null;
  });

  protected readonly alreadyPlacedBet = computed(() => {
    const me = this.currentUser();
    if (!me) return null;
    return this.gameStore.bets().find((b) => b.displayName === me.displayName) ?? null;
  });

  ngOnInit(): void {
    void this.gameStore.init();
    void this.userStore.load();
  }

  ngOnDestroy(): void {
    this.gameStore.disconnect();
  }

  protected toggleReady(): void {
    const player = this.currentInGamePlayer();
    void this.gameStore.toggleReady(player ? !player.isReady : true);
  }

  protected selectExit(exitName: string): void {
    if (this.alreadyPlacedBet()) return;
    this.selectedExit.set(exitName);
  }

  protected setBetAmount(amount: number): void {
    if (this.alreadyPlacedBet()) return;
    this.betAmount.set(amount);
  }

  protected setBetFromInput(value: string): void {
    const parsed = Number(value);
    if (!Number.isNaN(parsed)) this.setBetAmount(parsed);
  }

  protected adjustBet(delta: number): void {
    if (this.alreadyPlacedBet()) return;
    const balance = this.currentInGamePlayer()?.balance ?? 1000;
    this.betAmount.update((val) => Math.max(MIN_BET, Math.min(val + delta, balance)));
  }

  protected placeMaxBet(): void {
    if (this.alreadyPlacedBet()) return;
    const player = this.currentInGamePlayer();
    if (player) this.betAmount.set(player.balance);
  }

  protected submitBet(): void {
    if (this.alreadyPlacedBet()) return;

    const exit = this.selectedExit();
    const amount = this.betAmount();
    const player = this.currentInGamePlayer();

    if (!exit) {
      toast.error('Pick an exit before placing your bet.');
      return;
    }

    if (amount <= 0 || (player && amount > player.balance)) {
      toast.error('Invalid bet amount or insufficient balance.');
      return;
    }

    void this.gameStore.placeBet(exit, amount);
  }

  private playWarningBeep(seconds: number): void {
    try {
      const audioCtx = new (window.AudioContext || (window as any).webkitAudioContext)();
      const osc = audioCtx.createOscillator();
      const gain = audioCtx.createGain();
      
      osc.connect(gain);
      gain.connect(audioCtx.destination);
      
      // Pitch goes up as time runs out (500Hz to 900Hz)
      const frequency = 400 + (6 - seconds) * 100;
      osc.frequency.setValueAtTime(frequency, audioCtx.currentTime);
      
      const duration = 0.12;
      gain.gain.setValueAtTime(0.12, audioCtx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, audioCtx.currentTime + duration);
      
      osc.start();
      osc.stop(audioCtx.currentTime + duration);
    } catch (e) {
      console.warn('Web Audio API warning:', e);
    }
  }
}
