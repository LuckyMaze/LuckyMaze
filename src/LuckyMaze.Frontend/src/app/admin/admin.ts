import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { SettingsService } from '../api/api/settings.service';
import { GameSettings } from '../api/model/gameSettings';
import { MazeSize } from '../api/model/mazeSize';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    HlmInputImports,
    HlmButtonImports,
    HlmLabelImports,
    HlmCardImports,
    HlmSwitchImports
  ],
  templateUrl: './admin.html',
})
export class AdminComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private fb = inject(FormBuilder);

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
}
