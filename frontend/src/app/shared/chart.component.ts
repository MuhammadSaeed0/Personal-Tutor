import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, inject, input, viewChild } from '@angular/core';
import {
  BarController, BarElement, CategoryScale, Chart, ChartConfiguration, Filler, LinearScale, LineController, LineElement, PointElement, Tooltip
} from 'chart.js';
import { I18nService } from '../core/i18n.service';
import { ThemeService } from '../core/theme.service';

Chart.register(BarController, BarElement, LineController, LineElement, PointElement, CategoryScale, LinearScale, Filler, Tooltip);

export interface SimpleSeries {
  labels: string[];
  values: number[];
  /** Optional unit appended in tooltips, e.g. "%" or "min". */
  unit?: string;
}

/**
 * Minimal, themed Chart.js wrapper for single-series bar/line charts. The x-axis is reversed in RTL so
 * time still flows in the reading direction.
 */
@Component({
  selector: 'rw-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="chart-box" [style.height.px]="height()"><canvas #canvas role="img" [attr.aria-label]="label()"></canvas></div>`,
  styles: [`.chart-box { position: relative; width: 100%; }`]
})
export class ChartComponent implements OnDestroy {
  readonly type = input<'bar' | 'line'>('bar');
  readonly data = input.required<SimpleSeries>();
  readonly height = input(200);
  readonly label = input('');
  readonly max = input<number | undefined>(undefined);

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly i18n = inject(I18nService);
  private readonly theme = inject(ThemeService);
  private chart: Chart | null = null;

  constructor() {
    effect(() => {
      const series = this.data();
      const rtl = this.i18n.dir() === 'rtl';
      this.theme.resolved(); // re-render on theme change
      const css = getComputedStyle(document.documentElement);
      const brand = css.getPropertyValue('--brand').trim() || '#0e6b66';
      const ink = css.getPropertyValue('--ink-3').trim() || '#7b8595';
      const line = css.getPropertyValue('--line').trim() || '#e7e1d7';
      const font = css.getPropertyValue('--font-ui').trim();

      const config: ChartConfiguration = {
        type: this.type(),
        data: {
          labels: series.labels,
          datasets: [{
            data: series.values,
            backgroundColor: this.type() === 'bar' ? brand : hexAlpha(brand, 0.12),
            borderColor: brand,
            borderWidth: this.type() === 'line' ? 2 : 0,
            borderRadius: 6,
            maxBarThickness: 26,
            fill: this.type() === 'line',
            tension: 0.35,
            pointRadius: 3,
            pointBackgroundColor: brand
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          animation: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? false : { duration: 500 },
          plugins: {
            legend: { display: false },
            tooltip: {
              rtl,
              textDirection: rtl ? 'rtl' : 'ltr',
              callbacks: { label: ctx => `${this.i18n.number(Number(ctx.raw), 1)}${series.unit ? ' ' + series.unit : ''}` }
            }
          },
          scales: {
            x: { reverse: rtl, grid: { display: false }, ticks: { color: ink, font: { family: font, size: 11 } }, border: { color: line } },
            y: {
              position: rtl ? 'right' : 'left', beginAtZero: true, max: this.max(),
              grid: { color: line }, border: { display: false },
              ticks: { color: ink, font: { family: font, size: 11 }, maxTicksLimit: 5, callback: v => this.i18n.number(Number(v)) }
            }
          }
        }
      };

      this.chart?.destroy();
      this.chart = new Chart(this.canvas().nativeElement, config);
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}

function hexAlpha(color: string, alpha: number): string {
  if (!color.startsWith('#') || color.length !== 7) return color;
  const n = parseInt(color.slice(1), 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
}
