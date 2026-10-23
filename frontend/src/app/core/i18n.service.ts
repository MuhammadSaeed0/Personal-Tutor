import { DOCUMENT } from '@angular/common';
import { Injectable, Pipe, PipeTransform, computed, inject, signal } from '@angular/core';
import { AR } from './i18n/ar';
import { EN } from './i18n/en';
import { Lang, Subject } from './models';

type Dict = Record<string, string>;
const DICTIONARIES: Record<Lang, Dict> = { ar: flatten(AR), en: flatten(EN) };

/**
 * Runtime localisation. Switching language swaps the dictionary and flips <html dir/lang>, so the whole
 * interface (layout included) mirrors between Arabic RTL and English LTR without a reload.
 * Textbook content is never translated by this service.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly document = inject(DOCUMENT);
  readonly lang = signal<Lang>(readStoredLang());
  readonly dir = computed(() => (this.lang() === 'ar' ? 'rtl' : 'ltr'));
  readonly locale = computed(() => (this.lang() === 'ar' ? 'ar-u-nu-latn' : 'en'));

  constructor() {
    this.apply(this.lang());
  }

  setLang(lang: Lang): void {
    this.lang.set(lang);
    this.apply(lang);
    try { localStorage.setItem('riwaq.lang', lang); } catch { /* storage unavailable */ }
  }

  t(key: string, params?: Record<string, unknown>): string {
    const dict = DICTIONARIES[this.lang()];
    let text = dict[key] ?? DICTIONARIES.en[key] ?? key;
    if (params) {
      for (const [name, value] of Object.entries(params)) text = text.split(`{${name}}`).join(isolate(value));
    }
    return text;
  }

  has(key: string): boolean {
    return key in DICTIONARIES[this.lang()];
  }

  subjectName(subject: Subject | undefined | null): string {
    if (!subject) return '';
    return this.lang() === 'ar' ? subject.nameAr : subject.nameEn;
  }

  number(value: number | null | undefined, digits = 0): string {
    if (value === null || value === undefined || Number.isNaN(value)) return '—';
    return new Intl.NumberFormat(this.locale(), { maximumFractionDigits: digits, minimumFractionDigits: 0 }).format(value);
  }

  date(value: string | Date | null | undefined, style: 'short' | 'medium' | 'long' | 'weekday' = 'medium'): string {
    if (!value) return '—';
    const d = typeof value === 'string' ? new Date(value.length === 10 ? value + 'T00:00:00' : value) : value;
    const options: Intl.DateTimeFormatOptions =
      style === 'short' ? { day: 'numeric', month: 'short' }
        : style === 'weekday' ? { weekday: 'long', day: 'numeric', month: 'long' }
          : style === 'long' ? { dateStyle: 'long' } : { dateStyle: 'medium' };
    return new Intl.DateTimeFormat(this.locale(), options).format(d);
  }

  relative(value: string | null | undefined): string {
    if (!value) return '—';
    const diff = (new Date(value).getTime() - Date.now()) / 1000;
    const rtf = new Intl.RelativeTimeFormat(this.locale(), { numeric: 'auto' });
    const abs = Math.abs(diff);
    if (abs < 60) return rtf.format(Math.round(diff), 'second');
    if (abs < 3600) return rtf.format(Math.round(diff / 60), 'minute');
    if (abs < 86400) return rtf.format(Math.round(diff / 3600), 'hour');
    return rtf.format(Math.round(diff / 86400), 'day');
  }

  duration(minutes: number): string {
    const h = Math.floor(minutes / 60);
    const m = Math.round(minutes % 60);
    if (h === 0) return this.t('common.minutesShort', { n: this.number(m) });
    return m === 0 ? this.t('common.hoursShort', { n: this.number(h) }) : this.t('common.hoursMinutesShort', { h: this.number(h), m: this.number(m) });
  }

  private apply(lang: Lang): void {
    const root = this.document.documentElement;
    root.lang = lang;
    root.dir = lang === 'ar' ? 'rtl' : 'ltr';
  }
}

/** `{{ 'key' | t }}` or `{{ 'key' | t: { n: 3 } }}`. Impure so it re-renders when the language signal changes. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(key: string, params?: Record<string, unknown>): string {
    return this.i18n.t(key, params);
  }
}

/**
 * Wraps inserted text that contains letters in Unicode "first strong isolate" marks so mixed Arabic/Latin
 * values (book titles, chapter names) keep their own direction inside a sentence of the other language.
 */
function isolate(value: unknown): string {
  const text = String(value ?? '');
  return /\p{L}/u.test(text) ? '\u2068' + text + '\u2069' : text;
}

function readStoredLang(): Lang {
  try {
    return localStorage.getItem('riwaq.lang') === 'en' ? 'en' : 'ar';
  } catch {
    return 'ar';
  }
}

export function flatten(obj: Record<string, unknown>, prefix = ''): Dict {
  const out: Dict = {};
  for (const [key, value] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === 'string') out[path] = value;
    else if (value && typeof value === 'object') Object.assign(out, flatten(value as Record<string, unknown>, path));
  }
  return out;
}
