import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { I18nService, TranslatePipe } from '../core/i18n.service';
import { Recommendation } from '../core/models';
import { StoreService } from '../core/store.service';
import { IconComponent } from './icon.component';

const ICONS: Record<string, string> = {
  weak_topic: 'target', weak_concept_in_strong_chapter: 'target', cards_due: 'layers', exam_upcoming: 'calendar-clock',
  quiz_read_chapter: 'list-checks', upload_first_book: 'upload', continue_reading: 'book-open', keep_reviewing: 'sparkles'
};

/** Renders a data-derived recommendation with a localized sentence and a direct action. */
@Component({
  selector: 'rw-recommendation',
  imports: [TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="rec">
      <span class="icon-tile"><rw-icon [name]="icon()" [size]="18" /></span>
      <p class="grow">{{ text() }}</p>
      @if (action()) { <button class="btn soft sm" type="button" (click)="go()">{{ action()! | t }}</button> }
    </div>
  `,
  styles: [`
    .rec { display: flex; gap: 12px; align-items: center; padding: 12px 0; border-bottom: 1px solid var(--line); }
    :host:last-child .rec { border-bottom: 0; }
    p { margin: 0; font-size: var(--text-sm); line-height: 1.6; }
  `]
})
export class RecommendationComponent {
  readonly rec = input.required<Recommendation>();
  private readonly i18n = inject(I18nService);
  private readonly store = inject(StoreService);
  private readonly router = inject(Router);

  protected readonly icon = computed(() => ICONS[this.rec().code] ?? 'lightbulb');

  protected readonly text = computed(() => {
    const r = this.rec();
    const p = { ...r.params } as Record<string, unknown>;
    if (typeof p['subjectId'] === 'number') p['subject'] = this.store.subjectName(p['subjectId'] as number);
    if (r.code === 'exam_upcoming' && !p['subject']) p['subject'] = this.i18n.t('common.allSubjects');
    let key = `recommendations.${r.code}`;
    if ((r.code === 'weak_topic' || r.code === 'weak_concept_in_strong_chapter') && p['page'] == null) key += 'NoPage';
    return this.i18n.t(key, p);
  });

  protected readonly action = computed<string | null>(() => {
    switch (this.rec().code) {
      case 'weak_topic':
      case 'weak_concept_in_strong_chapter': return this.rec().params['bookId'] ? 'recommendations.actions.review' : null;
      case 'cards_due': return 'recommendations.actions.reviewCards';
      case 'quiz_read_chapter': return 'recommendations.actions.quiz';
      case 'upload_first_book': return null;
      case 'exam_upcoming': return 'recommendations.actions.plan';
      case 'continue_reading': return 'recommendations.actions.open';
      default: return null;
    }
  });

  protected go(): void {
    const p = this.rec().params;
    switch (this.rec().code) {
      case 'weak_topic':
      case 'weak_concept_in_strong_chapter':
        void this.router.navigate(['/read', p['bookId']], { queryParams: { page: p['page'] ?? undefined } });
        break;
      case 'cards_due': void this.router.navigateByUrl('/flashcards/review'); break;
      case 'quiz_read_chapter': void this.router.navigate(['/quizzes/new'], { queryParams: { bookId: p['bookId'], chapterId: p['chapterId'] } }); break;
      case 'exam_upcoming': void this.router.navigateByUrl('/plan'); break;
      case 'continue_reading': {
        const code = this.store.subject(p['subjectId'] as number)?.code;
        if (code) void this.router.navigate(['/subjects', code]);
        break;
      }
    }
  }
}
