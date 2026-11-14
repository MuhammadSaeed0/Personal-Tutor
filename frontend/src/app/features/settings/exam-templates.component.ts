import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { ExamTemplate } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';

/** Editable Ministry exam shape per subject (question counts, marks, time). */
@Component({
  selector: 'rw-exam-templates',
  imports: [FormsModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="muted small">{{ 'examTemplates.lead' | t }}</p>
    @for (t of templates(); track t.subjectId) {
      <section class="template">
        <div class="row between head">
          <strong><span class="subject-dot" [style.background]="store.subject(t.subjectId)?.color"></span> {{ store.subjectName(t.subjectId) }}</strong>
          <span class="badge" [class.brand]="t.isDefault && t.isOfficial">{{ (t.isDefault ? (t.isOfficial ? 'examTemplates.official' : 'examTemplates.general') : 'examTemplates.custom') | t }}</span>
        </div>
        <div class="grid">
          <label class="field"><span class="xsmall">{{ 'examTemplates.mcq' | t }}</span><input class="input num" type="number" min="0" max="60" [(ngModel)]="t.multipleChoiceCount" /></label>
          <label class="field"><span class="xsmall">{{ 'examTemplates.twoMark' | t }}</span><input class="input num" type="number" min="0" [(ngModel)]="t.twoMarkChoiceCount" /></label>
          <label class="field"><span class="xsmall">{{ 'examTemplates.essay' | t }}</span><input class="input num" type="number" min="0" max="20" [(ngModel)]="t.essayCount" /></label>
          <label class="field"><span class="xsmall">{{ 'examTemplates.essayPoints' | t }}</span><input class="input num" type="number" min="0.5" step="0.5" [(ngModel)]="t.essayPoints" /></label>
          <label class="field"><span class="xsmall">{{ 'examTemplates.minutes' | t }}</span><input class="input num" type="number" min="5" max="300" [(ngModel)]="t.durationMinutes" /></label>
          <label class="field"><span class="xsmall">{{ 'examTemplates.diagrams' | t }}</span><input class="input num" type="number" min="0" max="20" [(ngModel)]="t.diagramCount" /></label>
        </div>
        <div class="row between foot">
          <span class="small muted">{{ 'examTemplates.total' | t: { n: total(t) } }}</span>
          <span class="row">
            @if (!t.isDefault) { <button class="btn ghost sm" type="button" (click)="reset(t)" [disabled]="busy()">{{ 'examTemplates.reset' | t }}</button> }
            <button class="btn primary sm" type="button" (click)="save(t)" [disabled]="busy()">{{ 'examTemplates.save' | t }}</button>
          </span>
        </div>
      </section>
    } @empty { <span class="spinner"></span> }
  `,
  styles: [`
    .template { border: 1px solid var(--line); border-radius: var(--radius); padding: 14px; margin-top: 12px; display: flex; flex-direction: column; gap: 10px; }
    .head strong { display: flex; align-items: center; gap: 8px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(130px, 1fr)); gap: 10px; }
    .field { display: flex; flex-direction: column; gap: 4px; }
    .foot { flex-wrap: wrap; gap: 8px; }
    .foot .row { gap: 8px; }
  `]
})
export class ExamTemplatesComponent {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  protected readonly templates = signal<ExamTemplate[]>([]);
  protected readonly busy = signal(false);

  constructor() {
    this.api.get<ExamTemplate[]>('/api/exam-templates').then(t => this.templates.set(t)).catch(e => this.toast.error(e));
  }

  protected total(t: ExamTemplate): number {
    return Number(t.multipleChoiceCount) + Number(t.twoMarkChoiceCount) + Number(t.essayCount) * Number(t.essayPoints);
  }

  protected async save(t: ExamTemplate): Promise<void> {
    await this.run(() => this.api.put<ExamTemplate>(`/api/exam-templates/${t.subjectId}`, {
      multipleChoiceCount: +t.multipleChoiceCount, twoMarkChoiceCount: +t.twoMarkChoiceCount, essayCount: +t.essayCount,
      essayPoints: +t.essayPoints, durationMinutes: +t.durationMinutes, diagramCount: +t.diagramCount
    }), true);
  }

  protected reset(t: ExamTemplate): Promise<void> {
    return this.run(() => this.api.delete<ExamTemplate>(`/api/exam-templates/${t.subjectId}`), false);
  }

  private async run(call: () => Promise<ExamTemplate>, announce: boolean): Promise<void> {
    this.busy.set(true);
    try {
      const updated = await call();
      this.templates.update(list => list.map(x => (x.subjectId === updated.subjectId ? updated : x)));
      if (announce) this.toast.success('examTemplates.saved');
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }
}
