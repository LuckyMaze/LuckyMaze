import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { SettingsService } from '../api/api/settings.service';
import { GameSettings } from '../api/model/gameSettings';
import { MazeSize } from '../api/model/mazeSize';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmCardImports } from '@spartan-ng/helm/card';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    HlmInputImports,
    HlmButtonImports,
    HlmLabelImports,
    HlmNativeSelectImports,
    HlmCardImports
  ],
  templateUrl: './admin.html',
})
export class AdminComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private fb = inject(FormBuilder);

  settingsForm: FormGroup;
  mazeSizes = [
    { label: 'Small (16x16)', value: MazeSize.Small16x16 },
    { label: 'Grid (21x21)', value: MazeSize.Grid21x21 },
    { label: 'Medium (32x32)', value: MazeSize.Medium32x32 },
    { label: 'Large (64x64)', value: MazeSize.Large64x64 },
  ];

  constructor() {
    this.settingsForm = this.fb.group({
      mazeSize: [MazeSize.Grid21x21, Validators.required],
      gameSpeedMs: [850, [Validators.required, Validators.min(100), Validators.max(5000)]],
      minBet: [1.00, [Validators.required, Validators.min(0.01)]],
      maxBet: [500.00, [Validators.required, Validators.min(1)]],
    });
  }

  ngOnInit() {
    this.settingsService.apiSettingsGet().subscribe({
      next: (settings: GameSettings) => {
        this.settingsForm.patchValue({
          mazeSize: settings.mazeSize,
          gameSpeedMs: settings.gameSpeedMs,
          minBet: settings.minBet,
          maxBet: settings.maxBet
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
