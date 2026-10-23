import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { ExplanationLang, Profile } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { BrandComponent } from '../../layout/brand.component';
import { IconComponent } from '../../shared/icon.component';

interface ExamRow { title: string; date: string; subjectId: number | null; }

@Component({
  selector: 'rw-onboarding',
  imports: [FormsModule, TranslatePipe, IconComponent, BrandComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './onboarding.component.html',
  styleUrl: './onboarding.component.scss'
})
export class OnboardingComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly steps = ['grade', 'curriculum', 'exams', 'time', 'difficult', 'goal'] as const;
  protected readonly step = signal(0);
  protected readonly busy = signal(false);

  protected grade = '';
  protected academicYear = '';
  protected curriculum = '';
  protected exams: ExamRow[] = [{ title: '', date: '', subjectId: null }];
  protected dailyMinutes = 120;
  protected readonly difficult = signal<number[]>([]);
  protected targetScore = 85;
  protected explanationLanguage: ExplanationLang = this.i18n.lang();

  protected readonly curricula = ['national', 'igcse', 'american', 'ib', 'french', 'other'];

  protected next(): void {
    if (this.step() < this.steps.length - 1) this.step.update(s => s + 1);
    else void this.finish();
  }

  protected back(): void {
    this.step.update(s => Math.max(0, s - 1));
  }

  protected pickCurriculum(key: string): void {
    this.curriculum = this.i18n.t('onboarding.curricula.' + key);
  }

  protected toggleSubject(id: number): void {
    this.difficult.update(list => (list.includes(id) ? list.filter(x => x !== id) : [...list, id]));
  }

  protected addExam(): void {
    this.exams = [...this.exams, { title: '', date: '', subjectId: null }];
  }

  protected removeExam(index: number): void {
    this.exams = this.exams.filter((_, i) => i !== index);
  }

  protected async skip(): Promise<void> {
    await this.save(false);
  }

  protected async finish(): Promise<void> {
    await this.save(true);
  }

  private async save(withAnswers: boolean): Promise<void> {
    this.busy.set(true);
    try {
      const body = withAnswers
        ? {
          grade: this.grade || null, curriculum: this.curriculum || null, academicYear: this.academicYear || null,
          targetScore: this.targetScore, dailyStudyMinutes: this.dailyMinutes, difficultSubjectIds: this.difficult(),
          exams: this.exams.filter(e => e.title.trim() && e.date).map(e => ({ title: e.title.trim(), date: e.date, subjectId: e.subjectId, notes: null })),
          completed: true
        }
        : { completed: true };
      const profile = await this.api.put<Profile>('/api/me/onboarding', body);
      if (withAnswers && this.explanationLanguage !== profile.preferences.explanationLanguage) {
        await this.store.updatePreferences({ explanationLanguage: this.explanationLanguage });
        profile.preferences.explanationLanguage = this.explanationLanguage;
      }
      this.auth.setProfile(profile);
      await this.router.navigateByUrl('/');
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }
}
