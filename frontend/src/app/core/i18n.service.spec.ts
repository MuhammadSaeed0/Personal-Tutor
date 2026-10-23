import { TestBed } from '@angular/core/testing';
import { AR } from './i18n/ar';
import { EN } from './i18n/en';
import { I18nService, flatten } from './i18n.service';

describe('I18nService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  it('defaults to Arabic with right-to-left layout', () => {
    const i18n = TestBed.inject(I18nService);
    expect(i18n.lang()).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
    expect(i18n.t('nav.home')).toBe('الرئيسية');
  });

  it('switches the whole document to English left-to-right and remembers the choice', () => {
    const i18n = TestBed.inject(I18nService);
    i18n.setLang('en');
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.documentElement.lang).toBe('en');
    expect(i18n.t('nav.home')).toBe('Home');
    expect(localStorage.getItem('riwaq.lang')).toBe('en');
    i18n.setLang('ar');
    expect(document.documentElement.dir).toBe('rtl');
  });

  it('interpolates parameters and falls back to the key for unknown entries', () => {
    const i18n = TestBed.inject(I18nService);
    i18n.setLang('en');
    expect(i18n.t('common.pageN', { n: 42 })).toBe('p. 42');
    expect(i18n.t('plan.titles.read', { target: 'الفصل الأول' })).toBe('Read \u2068الفصل الأول\u2069');
    expect(i18n.t('does.not.exist')).toBe('does.not.exist');
  });

  it('has identical key sets for Arabic and English', () => {
    expect(Object.keys(flatten(AR)).sort()).toEqual(Object.keys(flatten(EN)).sort());
  });

  it('defines every enum-driven key the UI builds dynamically', () => {
    const keys = new Set(Object.keys(flatten(EN)));
    const required = [
      ...['queued', 'validating', 'extractingText', 'recognizingScannedPages', 'detectingStructure', 'processingTablesAndFigures', 'preparingSearch', 'generatingResources', 'completed', 'uploading'].map(s => `processing.stages.${s}`),
      ...['multipleChoice', 'trueFalse', 'fillBlank', 'shortAnswer', 'matching', 'ordering', 'numerical', 'structured'].map(s => `questionTypes.${s}`),
      ...['quick', 'simple', 'detailed', 'keyPoints', 'definitions', 'formulas', 'comparisons', 'causesEffects', 'examQuestions', 'revisionSheet'].map(s => `summaryStyles.${s}`),
      ...['notStarted', 'inProgress', 'completed', 'alreadyKnown'].map(s => `chapterStatus.${s}`),
      ...['new', 'learning', 'review', 'relearning'].map(s => `flashcards.states.${s}`),
      ...['again', 'hard', 'good', 'easy'].map(s => `review.ratings.${s}`),
      ...['cards_due', 'weak_topic', 'check_understanding', 'exam_soon', 'next_chapter'].map(s => `plan.reasons.${s}`),
      ...['scanned_page_no_ocr', 'ocr_low_confidence', 'ocr_failed', 'no_structure_detected', 'structure_estimated', 'figures_not_interpreted', 'tables_flattened', 'embeddings_unavailable', 'no_text_extracted'].map(s => `warnings.${s}`),
      ...['pages_without_text', 'ocr_pages_included', 'source_truncated'].map(s => `summaryWarnings.${s}`),
      ...['weak_topic', 'weak_topicNoPage', 'weak_concept_in_strong_chapter', 'weak_concept_in_strong_chapterNoPage', 'cards_due', 'exam_upcoming', 'quiz_read_chapter', 'upload_first_book', 'continue_reading', 'keep_reviewing'].map(s => `recommendations.${s}`),
      ...['first_book', 'first_chapter', 'chapters_10', 'streak_3', 'streak_7', 'streak_30', 'reviews_100', 'reviews_1000', 'first_quiz', 'quiz_perfect', 'focus_10h', 'notes_10'].flatMap(s => [`achievements.${s}.title`, `achievements.${s}.text`]),
      ...['ai_not_configured', 'ai_unavailable', 'ai_rate_limited', 'ai_refused', 'ai_quota_exceeded', 'duplicate_book', 'invalid_credentials', 'network_error'].map(s => `errors.${s}`),
      ...['arabic', 'english', 'french', 'physics', 'chemistry', 'biology', 'geology'].map(s => `subject.focus.${s}`)
    ];
    expect(required.filter(k => !keys.has(k))).toEqual([]);
  });
});
