import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { AiUsage } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { ChartComponent, SimpleSeries } from '../../shared/chart.component';

/** The student's own AI use: today against the daily limits, a per-day chart and a per-feature table. */
@Component({
  selector: 'rw-ai-usage',
  imports: [TranslatePipe, ChartComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="muted small">{{ 'usage.lead' | t }}</p>
    @if (data(); as d) {
      <div class="today">
        <div class="meter">
          <div class="row between small"><span>{{ 'usage.todayRequests' | t }}</span>
            <span class="num" dir="ltr">{{ i18n.number(d.todayRequests) }} / {{ i18n.number(d.dailyRequestLimit) }}</span></div>
          <div class="bar"><span [style.width.%]="percent(d.todayRequests, d.dailyRequestLimit)"></span></div>
        </div>
        <div class="meter">
          <div class="row between small"><span>{{ 'usage.todayTokens' | t }}</span>
            <span class="num" dir="ltr">{{ i18n.number(d.todayOutputTokens) }} / {{ i18n.number(d.dailyOutputTokenLimit) }}</span></div>
          <div class="bar"><span [style.width.%]="percent(d.todayOutputTokens, d.dailyOutputTokenLimit)"></span></div>
        </div>
        <p class="xsmall subtle">{{ 'usage.resets' | t }}</p>
      </div>

      <h3 class="small">{{ 'usage.perDay' | t: { n: d.days } }}</h3>
      <rw-chart [data]="series()" [height]="200" [label]="'usage.perDay' | t: { n: d.days }" />

      <h3 class="small">{{ 'usage.perFeature' | t }}</h3>
      @if (d.byFeature.length === 0) { <p class="muted small">{{ 'usage.none' | t }}</p> }
      @else {
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>{{ 'usage.feature' | t }}</th><th>{{ 'usage.requests' | t }}</th><th>{{ 'usage.failed' | t }}</th><th>{{ 'usage.input' | t }}</th><th>{{ 'usage.output' | t }}</th></tr></thead>
            <tbody>
              @for (f of d.byFeature; track f.feature) {
                <tr>
                  <td>{{ featureName(f.feature) }}</td>
                  <td class="num">{{ i18n.number(f.requests) }}</td>
                  <td class="num">{{ i18n.number(f.failed) }}</td>
                  <td class="num">{{ i18n.number(f.inputTokens) }}</td>
                  <td class="num">{{ i18n.number(f.outputTokens) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    } @else {
      <span class="spinner"></span>
    }
  `,
  styles: [`
    .today { display: flex; flex-direction: column; gap: 12px; margin: 12px 0 20px; }
    .meter { display: flex; flex-direction: column; gap: 6px; }
    h3 { margin: 18px 0 8px; }
    .table-wrap { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: var(--text-sm); }
    .table th, .table td { padding: 8px 10px; border-bottom: 1px solid var(--line); text-align: start; }
    .table th { color: var(--ink-3); font-weight: 600; }
  `]
})
export class AiUsageComponent {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  protected readonly i18n = inject(I18nService);
  protected readonly data = signal<AiUsage | null>(null);

  protected readonly series = computed<SimpleSeries>(() => {
    const daily = this.data()?.daily ?? [];
    return { labels: daily.map(d => this.i18n.date(d.date, 'short')), values: daily.map(d => d.requests) };
  });

  constructor() {
    this.api.get<AiUsage>('/api/account/ai-usage?days=14').then(d => this.data.set(d)).catch(e => this.toast.error(e));
  }

  protected percent(value: number, limit: number): number {
    return limit > 0 ? Math.min(100, (100 * value) / limit) : 0;
  }

  protected featureName(feature: string): string {
    const key = `usage.features.${feature}`;
    return this.i18n.has(key) ? this.i18n.t(key) : feature;
  }
}
