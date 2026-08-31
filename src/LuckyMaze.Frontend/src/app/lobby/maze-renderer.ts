import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  input,
  viewChild,
} from '@angular/core';
import { MazeCell } from '../api/model/mazeCell';
import { MazeExit } from '../api/model/mazeExit';

@Component({
  selector: 'luckymaze-maze-renderer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="relative mx-auto aspect-square w-full max-w-[280px] overflow-hidden rounded-xl border bg-card sm:max-w-[360px]">
      <canvas #mazeCanvas class="block size-full"></canvas>
    </div>
  `,
})
export class MazeRenderer implements AfterViewInit, OnDestroy {
  private readonly canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('mazeCanvas');

  public readonly width = input(7);
  public readonly height = input(7);
  public readonly gridData = input<MazeCell[] | null>(null);
  public readonly exits = input<MazeExit[] | null>(null);
  public readonly aiPosition = input<{ x: number; y: number } | null>(null);

  private ctx!: CanvasRenderingContext2D;
  private animationFrameId: number | null = null;

  // Interpolated AI position, eased toward the latest reported coordinate.
  private currentAiX: number | null = null;
  private currentAiY: number | null = null;
  private targetAiX: number | null = null;
  private targetAiY: number | null = null;
  private readonly lerpSpeed = 0.15;

  constructor() {
    effect(() => {
      const pos = this.aiPosition();
      if (!pos) return;
      if (this.currentAiX === null || this.currentAiY === null) {
        this.currentAiX = pos.x;
        this.currentAiY = pos.y;
      }
      this.targetAiX = pos.x;
      this.targetAiY = pos.y;
    });
  }

  ngAfterViewInit(): void {
    const canvas = this.canvasRef().nativeElement;
    this.ctx = canvas.getContext('2d')!;
    this.resizeCanvas();
    this.startRenderLoop();
    window.addEventListener('resize', this.onResize);
  }

  ngOnDestroy(): void {
    if (this.animationFrameId) cancelAnimationFrame(this.animationFrameId);
    window.removeEventListener('resize', this.onResize);
  }

  private readonly onResize = () => this.resizeCanvas();

  private resizeCanvas(): void {
    const canvas = this.canvasRef().nativeElement;
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    this.ctx.scale(dpr, dpr);
  }

  private startRenderLoop(): void {
    const render = () => {
      this.draw();
      this.animationFrameId = requestAnimationFrame(render);
    };
    render();
  }

  private draw(): void {
    const grid = this.gridData();
    if (!this.ctx || !grid) return;

    const width = this.width();
    const height = this.height();
    const canvas = this.canvasRef().nativeElement;
    const dpr = window.devicePixelRatio || 1;
    const drawWidth = canvas.width / dpr;
    const drawHeight = canvas.height / dpr;

    this.ctx.clearRect(0, 0, drawWidth, drawHeight);

    const cellWidth = drawWidth / width;
    const cellHeight = drawHeight / height;
    const cellSize = Math.min(cellWidth, cellHeight);
    const theme = this.readTheme();

    // A large maze (up to 64x64) packed into a few hundred pixels has too little room per cell for
    // a floor grid to read as anything but noise - and for thick wall strokes to read as anything
    // but static. Both scale down with cell size instead of staying a fixed pixel width.
    const showFloorGrid = cellSize >= 6;
    const wallWidth = Math.max(0.75, Math.min(3, cellSize * 0.22));

    if (showFloorGrid) {
      this.ctx.strokeStyle = theme.foreground;
      this.ctx.lineWidth = 1;
      this.ctx.globalAlpha = 0.1;
      for (let x = 0; x <= width; x++) {
        this.ctx.beginPath();
        this.ctx.moveTo(x * cellWidth, 0);
        this.ctx.lineTo(x * cellWidth, drawHeight);
        this.ctx.stroke();
      }
      for (let y = 0; y <= height; y++) {
        this.ctx.beginPath();
        this.ctx.moveTo(0, y * cellHeight);
        this.ctx.lineTo(drawWidth, y * cellHeight);
        this.ctx.stroke();
      }
      this.ctx.globalAlpha = 1;
    }

    // Walls
    this.ctx.strokeStyle = theme.foreground;
    this.ctx.lineWidth = wallWidth;
    this.ctx.lineCap = wallWidth >= 1.5 ? 'round' : 'butt';
    for (const cell of grid) {
      const x1 = cell.x * cellWidth;
      const y1 = cell.y * cellHeight;
      const x2 = x1 + cellWidth;
      const y2 = y1 + cellHeight;

      if (cell.north) this.line(x1, y1, x2, y1);
      if (cell.east) this.line(x2, y1, x2, y2);
      if (cell.south) this.line(x1, y2, x2, y2);
      if (cell.west) this.line(x1, y1, x1, y2);
    }

    // Exits - filled markers, the same solid-fill-plus-contrasting-text look as a default badge.
    const exits = this.exits();
    if (exits) {
      this.ctx.font = `600 12px ${theme.fontSans}`;
      this.ctx.textAlign = 'center';
      this.ctx.textBaseline = 'middle';
      for (const exit of exits) {
        const cx = (exit.x + 0.5) * cellWidth;
        const cy = (exit.y + 0.5) * cellHeight;
        const radius = Math.min(cellWidth, cellHeight) * 0.28;

        this.ctx.fillStyle = theme.foreground;
        this.ctx.beginPath();
        this.ctx.arc(cx, cy, radius, 0, Math.PI * 2);
        this.ctx.fill();

        this.ctx.fillStyle = theme.background;
        this.ctx.fillText(exit.name, cx, cy);
      }
    }

    // AI agent (eased toward target) - the one accent color this theme has, for the one thing
    // that's actually moving.
    if (this.currentAiX !== null && this.currentAiY !== null && this.targetAiX !== null && this.targetAiY !== null) {
      this.currentAiX += (this.targetAiX - this.currentAiX) * this.lerpSpeed;
      this.currentAiY += (this.targetAiY - this.currentAiY) * this.lerpSpeed;

      const x = (this.currentAiX + 0.5) * cellWidth;
      const y = (this.currentAiY + 0.5) * cellHeight;
      const radius = Math.min(cellWidth, cellHeight) * 0.22;

      this.ctx.fillStyle = theme.destructive;
      this.ctx.beginPath();
      this.ctx.arc(x, y, radius, 0, Math.PI * 2);
      this.ctx.fill();
    }
  }

  /** Canvas can't see Tailwind classes, so read the same CSS custom properties the rest of the
   * app's theme (light/dark/system) is built from, straight off the root element. */
  private readTheme(): { foreground: string; background: string; destructive: string; fontSans: string } {
    const styles = getComputedStyle(document.documentElement);
    const read = (name: string, fallback: string) => styles.getPropertyValue(name).trim() || fallback;

    return {
      foreground: read('--foreground', '#000'),
      background: read('--background', '#fff'),
      destructive: read('--destructive', '#ef4444'),
      fontSans: read('--font-sans', 'sans-serif'),
    };
  }

  private line(x1: number, y1: number, x2: number, y2: number): void {
    this.ctx.beginPath();
    this.ctx.moveTo(x1, y1);
    this.ctx.lineTo(x2, y2);
    this.ctx.stroke();
  }
}
