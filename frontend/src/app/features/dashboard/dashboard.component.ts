import { Dialog } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Dashboard, StudyTask, SubjectStats } from '../../core/models';
import { RealtimeService } from '../../core/realtime.service';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { RecommendationComponent } from '../../shared/recommendation.component';
import { EmptyStateComponent, RingComponent } from '../../shared/ui';
import { UploadDialogComponent } from '../library/upload-dialog.component';
import { TaskItemComponent } from '../planner/task-item.component';

@Component({
  selector: 'rw-dashboard',
  imports: [RouterLink, TranslatePipe, IconComponent, RingComponent, EmptyStateComponent, TaskItemComponent, RecommendationComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly dialog = inject(Dialog);
  private readonly toast = inject(ToastService);
  protected readonly realtime = inject(RealtimeService);

  protected readonly data = signal<Dashboard | null>(null);
  protected readonly error = signal(false);
  protected readonly todayIso = new Date().toISOString();

  protected readonly greetingKey = computed(() => {
    const h = new Date().getHours();
    return h < 12 ? 'dashboard.morning' : h < 18 ? 'dashboard.afternoon' : 'dashboard.evening';
  });
  protected readonly weeklyPercent = computed(() => {
    const d = this.data();
    return d && d.weeklyGoalMinutes > 0 ? Math.min(100, Math.round((100 * d.weeklyMinutes) / d.weeklyGoalMinutes)) : 0;
  });
  protected readonly todayDone = computed(() => this.data()?.today?.tasks.filter(t => t.status === 'completed').length ?? 0);
  protected readonly subjectsWithStats = computed(() => {
    const stats = new Map((this.data()?.subjects ?? []).map(s => [s.subjectId, s]));
    return this.store.subjects().map(subject => ({ subject, stats: stats.get(subject.id) }));
  });
  protected readonly nextExam = computed(() => this.data()?.upcomingExams[0] ?? null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(false);
    try {
      this.data.set(await this.api.get<Dashboard>('/api/dashboard'));
    } catch {
      this.error.set(true);
    }
  }

  protected upload(): void {
    this.dialog.open(UploadDialogComponent, { autoFocus: 'first-tabbable', restoreFocus: true }).closed.subscribe(() => void this.load());
  }

  protected async toggleTask(task: StudyTask): Promise<void> {
    await this.updateTask(task, task.status === 'completed' ? 'pending' : 'completed');
  }

  protected async skipTask(task: StudyTask): Promise<void> {
    await this.updateTask(task, 'skipped');
  }

  private async updateTask(task: StudyTask, status: StudyTask['status']): Promise<void> {
    try {
      const updated = await this.api.patch<StudyTask>(`/api/plan/tasks/${task.id}`, { status });
      this.data.update(d => d && d.today ? { ...d, today: { ...d.today, tasks: d.today.tasks.map(t => (t.id === task.id ? updated : t)) } } : d);
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected progress(stats: SubjectStats | undefined): number {
    if (!stats || stats.pagesTotal === 0) return 0;
    return Math.round((100 * stats.pagesRead) / stats.pagesTotal);
  }
}
