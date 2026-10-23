import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Overview, TopicStat } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ChartComponent, SimpleSeries } from '../../shared/chart.component';
import { RecommendationComponent } from '../../shared/recommendation.component';
import { EmptyStateComponent } from '../../shared/ui';

@Component({
  selector: 'rw-analytics',
  imports: [RouterLink, TranslatePipe, ChartComponent, RecommendationComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './analytics.component.html',
  styleUrl: './analytics.component.scss'
})
export class AnalyticsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);

  protected readonly subjectId = signal<number | null>(null);
  protected readonly overview = signal<Overview | null>(null);
  protected readonly topics = signal<TopicStat[]>([]);
  protected readonly showChapters = signal(false);

  protected readonly studySeries = computed<SimpleSeries>(() => ({
    labels: (this.overview()?.daily ?? []).map(d => this.i18n.date(d.date, 'short')),
    values: (this.overview()?.daily ?? []).map(d => d.minutes),
    unit: this.i18n.t('common.min')
  }));
  protected readonly quizSeries = computed<SimpleSeries>(() => ({
    labels: (this.overview()?.quizTrend ?? []).map(q => this.i18n.date(q.date, 'short')),
    values: (this.overview()?.quizTrend ?? []).map(q => q.percent),
    unit: '%'
  }));
  protected readonly heatmap = computed(() => {
    const list = this.topics().filter(t => t.isChapter === this.showChapters() && t.questionAttempts + t.cardReviews > 0);
    const groups = new Map<number, TopicStat[]>();
    for (const t of list) groups.set(t.subjectId, [...(groups.get(t.subjectId) ?? []), t]);
    return [...groups.entries()].map(([subjectId, items]) => ({ subjectId, items: items.sort((a, b) => a.estimatedMastery - b.estimatedMastery) }));
  });

  constructor() {
    effect(() => {
      const subjectId = this.subjectId();
      untracked(() => void this.load(subjectId));
    });
  }

  private async load(subjectId: number | null): Promise<void> {
    const [overview, topics] = await Promise.all([
      this.api.get<Overview>('/api/analytics/overview', { subjectId }),
      this.api.get<TopicStat[]>('/api/analytics/topics', { subjectId })
    ]);
    this.overview.set(overview);
    this.topics.set(topics);
  }

  protected cellColor(t: TopicStat): string {
    if (t.band === 'insufficient') return 'var(--surface-sunken)';
    const m = t.estimatedMastery;
    return m < 0.5 ? `color-mix(in srgb, var(--danger) ${30 + (0.5 - m) * 100}%, transparent)`
      : m < 0.8 ? `color-mix(in srgb, var(--warning) ${35 + (m - 0.5) * 80}%, transparent)`
        : `color-mix(in srgb, var(--success) ${40 + (m - 0.8) * 200}%, transparent)`;
  }

  protected percent(value: number | null | undefined, ofTotal: number): number {
    return ofTotal > 0 && value != null ? Math.round((100 * value) / ofTotal) : 0;
  }
}
