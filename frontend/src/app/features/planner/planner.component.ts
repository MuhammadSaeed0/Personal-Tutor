import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Plan, Profile, StudyTask } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { EmptyStateComponent } from '../../shared/ui';
import { TaskItemComponent } from './task-item.component';

@Component({
  selector: 'rw-planner',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, TaskItemComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <header class="page-header">
        <div><div class="eyebrow">{{ 'nav.plan' | t }}</div><h1>{{ 'plan.title' | t }}</h1><p>{{ 'plan.subtitle' | t }}</p></div>
        <div class="row">
          <a class="btn" routerLink="/focus"><rw-icon name="timer" [size]="16" /> {{ 'nav.focus' | t }}</a>
          <button class="btn primary" type="button" (click)="regenerate()" [disabled]="busy()"><rw-icon name="refresh" [size]="16" /> {{ 'plan.regenerate' | t }}</button>
        </div>
      </header>

      <section class="card settings">
        <div class="field inline">
          <label for="daily">{{ 'plan.dailyAvailability' | t }}</label>
          <input id="daily" type="range" min="30" max="480" step="15" [(ngModel)]="dailyMinutes" (change)="saveAvailability()" class="slider" />
          <strong class="num">{{ i18n.duration(dailyMinutes) }}</strong>
        </div>
        <p class="xsmall subtle">{{ 'plan.howItWorks' | t }}</p>
        @if (plan()?.breakMinutes) { <p class="xsmall subtle"><rw-icon name="clock" [size]="12" /> {{ 'plan.breakReminder' | t: { every: plan()!.breakEveryMinutes, minutes: plan()!.breakMinutes } }}</p> }
      </section>

      @if (plan(); as p) {
        @if (p.days.length === 0) {
          <div class="card"><rw-empty icon="calendar" [title]="'plan.emptyTitle' | t" [text]="'plan.emptyText' | t">
            <a class="btn primary" routerLink="/library">{{ 'nav.library' | t }}</a></rw-empty></div>
        }
        <div class="days">
          @for (day of p.days; track day.date) {
            <section class="card day" [class.today]="day.date === today">
              <header class="day-head">
                <div><strong>{{ day.date === today ? ('plan.today' | t) : i18n.date(day.date, 'weekday') }}</strong>
                  @if (day.date === today) { <span class="xsmall subtle"> · {{ i18n.date(day.date, 'short') }}</span> }</div>
                <span class="xsmall subtle num">{{ i18n.duration(day.completedMinutes) }} / {{ i18n.duration(day.plannedMinutes) }}</span>
              </header>
              <div class="bar"><span [style.width.%]="day.plannedMinutes ? (100 * day.completedMinutes) / day.plannedMinutes : 0"></span></div>
              @for (task of day.tasks; track task.id) {
                <div class="task-wrap">
                  <rw-task-item [task]="task" (toggle)="toggle($event)" (skip)="update($event, 'skipped')" />
                  @if (task.status === 'pending') {
                    <select class="select resched" [ngModel]="''" (ngModelChange)="reschedule(task, $event)" [attr.aria-label]="'plan.reschedule' | t">
                      <option value="" disabled>{{ 'plan.reschedule' | t }}</option>
                      @for (d of rescheduleDates(); track d) { @if (d !== task.date) { <option [value]="d">{{ i18n.date(d, 'short') }}</option> } }
                    </select>
                  }
                </div>
              } @empty { <p class="muted small">{{ 'plan.restDay' | t }}</p> }
            </section>
          }
        </div>
      } @else {
        <div class="skeleton" style="height: 300px"></div>
      }
    </div>
  `,
  styles: [`
    .settings { margin-bottom: 20px; display: flex; flex-direction: column; gap: 6px; }
    .field.inline { flex-direction: row; align-items: center; gap: 12px; flex-wrap: wrap; }
    .slider { flex: 1; min-width: 160px; max-width: 360px; accent-color: var(--brand); }
    .days { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 14px; }
    .day { display: flex; flex-direction: column; gap: 8px; }
    .day.today { border-color: var(--brand); box-shadow: 0 0 0 1px var(--brand), var(--shadow-2); }
    .day-head { display: flex; justify-content: space-between; align-items: baseline; }
    .task-wrap { position: relative; }
    .resched { position: absolute; inset-block-end: 2px; inset-inline-end: 0; width: auto; min-height: 22px; padding: 0 4px; font-size: 0.66rem; border-color: transparent; background: transparent; color: var(--ink-3); }
    .resched:hover { border-color: var(--line); }
  `]
})
export class PlannerComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  protected readonly plan = signal<Plan | null>(null);
  protected readonly busy = signal(false);
  protected readonly today = new Date().toISOString().slice(0, 10);
  protected dailyMinutes = this.auth.profile()?.academic.dailyStudyMinutes ?? 120;
  protected readonly rescheduleDates = computed(() => Array.from({ length: 10 }, (_, i) => {
    const d = new Date();
    d.setDate(d.getDate() + i);
    return d.toISOString().slice(0, 10);
  }));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.plan.set(await this.api.get<Plan>('/api/plan'));
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async regenerate(): Promise<void> {
    this.busy.set(true);
    try {
      this.plan.set(await this.api.post<Plan>('/api/plan/generate'));
      this.toast.success('plan.regenerated');
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveAvailability(): Promise<void> {
    try {
      const profile = await this.api.patch<Profile>('/api/me/academic', { dailyStudyMinutes: this.dailyMinutes });
      this.auth.setProfile(profile);
      await this.regenerate();
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected toggle(task: StudyTask): void {
    void this.update(task, task.status === 'completed' ? 'pending' : 'completed');
  }

  protected async update(task: StudyTask, status: StudyTask['status']): Promise<void> {
    try {
      await this.api.patch(`/api/plan/tasks/${task.id}`, { status });
      await this.load();
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async reschedule(task: StudyTask, date: string): Promise<void> {
    if (!date) return;
    try {
      await this.api.patch(`/api/plan/tasks/${task.id}`, { date });
      await this.load();
    } catch (e) {
      this.toast.error(e);
    }
  }
}
