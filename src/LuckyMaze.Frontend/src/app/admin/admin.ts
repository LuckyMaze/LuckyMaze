import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { SettingsService } from '../api/api/settings.service';
import { SystemService } from '../api/api/system.service';
import { GameSettings } from '../api/model/gameSettings';
import { MazeSize } from '../api/model/mazeSize';
import { NetworkMode } from '../api/model/networkMode';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmSeparatorImports } from '@spartan-ng/helm/separator';

@Component({
  selector: 'app-admin',
  imports: [
    ReactiveFormsModule,
    HlmInputImports,
    HlmButtonImports,
    HlmLabelImports,
    HlmCardImports,
    HlmSwitchImports,
    HlmSeparatorImports,
  ],
  templateUrl: './admin.html',
})
export class AdminComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private systemService = inject(SystemService);
  private fb = inject(FormBuilder);

  makeHotspotPermanent = false;

  settingsForm: FormGroup;

  constructor() {
    // Not user-editable (see admin.html) - only Grid21x21 actually renders correctly on the
    // physical panel (see #39) - but still part of the form so saving other settings round-trips
    // it instead of resetting to the enum's default value.
    this.settingsForm = this.fb.group({
      mazeSize: [MazeSize.Grid21x21, Validators.required],
      gameSpeedMs: [850, [Validators.required, Validators.min(100), Validators.max(5000)]],
      minBet: [1.00, [Validators.required, Validators.min(0.01)]],
      maxBet: [500.00, [Validators.required, Validators.min(1)]],
      pixelPitchMm: [3.0, [Validators.required, Validators.min(0.1)]],
      originOffsetXMm: [0, Validators.required],
      originOffsetYMm: [0, Validators.required],
      invertX: [false],
      invertY: [false],
      stepFeedRateMmPerMin: [2400, [Validators.required, Validators.min(1)]],
      travelFeedRateMmPerMin: [3000, [Validators.required, Validators.min(1)]],
      accelerationMmPerSec2: [null],
    });
  }

  ngOnInit() {
    this.settingsService.apiSettingsGet().subscribe({
      next: (settings: GameSettings) => {
        this.settingsForm.patchValue({
          mazeSize: settings.mazeSize,
          gameSpeedMs: settings.gameSpeedMs,
          minBet: settings.minBet,
          maxBet: settings.maxBet,
          pixelPitchMm: settings.pixelPitchMm,
          originOffsetXMm: settings.originOffsetXMm,
          originOffsetYMm: settings.originOffsetYMm,
          invertX: settings.invertX,
          invertY: settings.invertY,
          stepFeedRateMmPerMin: settings.stepFeedRateMmPerMin,
          travelFeedRateMmPerMin: settings.travelFeedRateMmPerMin,
          accelerationMmPerSec2: settings.accelerationMmPerSec2,
        });
      },
      error: (err) => toast.error('Failed to load settings')
    });
  }

  saveSettings() {
    if (this.settingsForm.invalid) return;

    this.settingsService.apiSettingsPut(this.settingsForm.value).subscribe({
      next: () => toast.success('Settings updated successfully! These will apply to the next game.'),
      error: (err) => toast.error('Failed to save settings')
    });
  }

  shutdown() {
    if (!confirm('Park the carriage and shut down the Raspberry Pi? It will need to be physically powered back on.')) {
      return;
    }

    this.systemService.apiSystemShutdownPost().subscribe({
      next: () => toast.success('Parking the carriage, then shutting down.'),
      error: () => toast.error('Failed to request shutdown')
    });
  }

  enableHotspot() {
    const permanentNote = this.makeHotspotPermanent
      ? ''
      : ' It will automatically switch back to WiFi after 10 minutes unless you confirm it works and mark it permanent.';

    if (!confirm(`Switch to the cabinet's own hotspot? This disconnects the Pi from your home WiFi immediately - if you're reaching this panel over that network, you'll lose access to it.${permanentNote}`)) {
      return;
    }

    this.systemService.apiSystemNetworkModePost({ mode: NetworkMode.Hotspot, permanent: this.makeHotspotPermanent }).subscribe({
      next: () => toast.success('Switching to hotspot mode.'),
      error: () => toast.error('Failed to request network mode change')
    });
  }

  disableHotspot() {
    if (!confirm('Switch back to the home WiFi network?')) {
      return;
    }

    this.systemService.apiSystemNetworkModePost({ mode: NetworkMode.Wifi }).subscribe({
      next: () => toast.success('Switching back to WiFi.'),
      error: () => toast.error('Failed to request network mode change')
    });
  }
}
