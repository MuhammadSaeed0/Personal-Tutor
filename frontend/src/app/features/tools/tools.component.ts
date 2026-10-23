import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, ApiError } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, Conversation } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { TutorStreamService } from '../tutor/tutor-stream';
import { ELEMENTS, Element, balance, formatEquation, molarMass, parseFormula } from './chemistry';

interface Unit { name: string; factor: number; }
const UNIT_GROUPS: Record<string, Unit[]> = {
  length: [{ name: 'm', factor: 1 }, { name: 'km', factor: 1000 }, { name: 'cm', factor: 0.01 }, { name: 'mm', factor: 0.001 }, { name: 'µm', factor: 1e-6 }, { name: 'nm', factor: 1e-9 }],
  mass: [{ name: 'kg', factor: 1 }, { name: 'g', factor: 0.001 }, { name: 'mg', factor: 1e-6 }, { name: 't', factor: 1000 }],
  time: [{ name: 's', factor: 1 }, { name: 'min', factor: 60 }, { name: 'h', factor: 3600 }, { name: 'ms', factor: 0.001 }],
  energy: [{ name: 'J', factor: 1 }, { name: 'kJ', factor: 1000 }, { name: 'cal', factor: 4.184 }, { name: 'kcal', factor: 4184 }, { name: 'eV', factor: 1.602176634e-19 }, { name: 'kWh', factor: 3.6e6 }],
  pressure: [{ name: 'Pa', factor: 1 }, { name: 'kPa', factor: 1000 }, { name: 'atm', factor: 101325 }, { name: 'bar', factor: 1e5 }, { name: 'mmHg', factor: 133.322 }],
  volume: [{ name: 'm³', factor: 1 }, { name: 'L', factor: 0.001 }, { name: 'mL', factor: 1e-6 }, { name: 'cm³', factor: 1e-6 }]
};

const SI_BASE = [
  { q: 'length', unit: 'metre', symbol: 'm' }, { q: 'mass', unit: 'kilogram', symbol: 'kg' }, { q: 'time', unit: 'second', symbol: 's' },
  { q: 'current', unit: 'ampere', symbol: 'A' }, { q: 'temperature', unit: 'kelvin', symbol: 'K' }, { q: 'amount', unit: 'mole', symbol: 'mol' },
  { q: 'luminous', unit: 'candela', symbol: 'cd' }
];
const DERIVED = [
  { q: 'force', unit: 'newton', symbol: 'N', si: 'kg·m/s²' }, { q: 'energy', unit: 'joule', symbol: 'J', si: 'N·m' }, { q: 'power', unit: 'watt', symbol: 'W', si: 'J/s' },
  { q: 'pressure', unit: 'pascal', symbol: 'Pa', si: 'N/m²' }, { q: 'charge', unit: 'coulomb', symbol: 'C', si: 'A·s' }, { q: 'voltage', unit: 'volt', symbol: 'V', si: 'W/A' },
  { q: 'resistance', unit: 'ohm', symbol: 'Ω', si: 'V/A' }, { q: 'frequency', unit: 'hertz', symbol: 'Hz', si: '1/s' }
];

@Component({
  selector: 'rw-tools',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, MarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tools.component.html',
  styleUrl: './tools.component.scss'
})
export class ToolsComponent {
  readonly tool = input.required<string>();
  readonly subject = input<string>('');
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly streamer = inject(TutorStreamService);

  // Periodic table
  protected readonly elements = ELEMENTS;
  protected readonly selectedElement = signal<Element | null>(ELEMENTS[5]);
  protected readonly mainTable = ELEMENTS.filter(e => e.group > 0);
  protected readonly lanthanides = ELEMENTS.filter(e => e.category === 'lanthanide' && e.group === 0);
  protected readonly actinides = ELEMENTS.filter(e => e.category === 'actinide' && e.group === 0);
  protected formula = 'H2SO4';
  protected readonly molar = computed(() => this.safeMolar(this.formulaSig()));
  private readonly formulaSig = signal('H2SO4');

  // Balancer
  protected equation = 'Fe + O2 -> Fe2O3';
  protected readonly balanced = signal<string | null>(null);
  protected readonly balanceError = signal<string | null>(null);
  protected readonly balancedMarkdown = computed(() => (this.balanced() ? `$$\\ce{${this.balanced()}}$$` : null));
  protected attemptCoefficients = '';
  protected readonly attemptResult = signal<boolean | null>(null);

  // Units
  protected readonly unitGroups = Object.keys(UNIT_GROUPS);
  protected unitGroup = 'length';
  protected fromUnit = 'km';
  protected toUnit = 'm';
  protected amount = 1;
  protected celsius = 25;
  protected readonly siBase = SI_BASE;
  protected readonly derived = DERIVED;

  // Language practice
  protected readonly books = signal<Book[]>([]);
  protected practiceBookId: string | null = null;
  protected writing = '';
  protected readonly feedback = signal('');
  protected readonly feedbackBusy = signal(false);
  protected readonly feedbackError = signal<string | null>(null);

  protected readonly languageBooks = computed(() => {
    const codes = this.subject() ? [this.subject()] : ['arabic', 'english', 'french'];
    const ids = this.store.subjects().filter(s => codes.includes(s.code)).map(s => s.id);
    return this.books().filter(b => ids.includes(b.subjectId) && (b.status === 'ready' || b.status === 'readyWithWarnings'));
  });

  constructor() {
    void this.api.get<Book[]>('/api/books').then(b => {
      this.books.set(b);
      this.practiceBookId = this.languageBooks()[0]?.id ?? null;
    });
  }

  protected units(): Unit[] {
    return UNIT_GROUPS[this.unitGroup];
  }

  protected setGroup(group: string): void {
    this.unitGroup = group;
    this.fromUnit = UNIT_GROUPS[group][1]?.name ?? UNIT_GROUPS[group][0].name;
    this.toUnit = UNIT_GROUPS[group][0].name;
  }

  protected converted(): string {
    const list = UNIT_GROUPS[this.unitGroup];
    const from = list.find(u => u.name === this.fromUnit);
    const to = list.find(u => u.name === this.toUnit);
    if (!from || !to || this.amount === null) return '—';
    const value = (this.amount * from.factor) / to.factor;
    return Math.abs(value) >= 1e6 || (Math.abs(value) < 1e-3 && value !== 0) ? value.toExponential(4) : String(Math.round(value * 1e6) / 1e6);
  }

  protected kelvin(): number {
    return Math.round((this.celsius + 273.15) * 100) / 100;
  }

  protected fahrenheit(): number {
    return Math.round((this.celsius * 9 / 5 + 32) * 100) / 100;
  }

  protected updateFormula(value: string): void {
    this.formula = value;
    this.formulaSig.set(value);
  }

  private safeMolar(formula: string): { mass: number; parts: [string, number][] } | null {
    try {
      const parts = [...parseFormula(formula).entries()];
      return parts.length ? { mass: molarMass(formula), parts } : null;
    } catch {
      return null;
    }
  }

  protected runBalance(): void {
    this.balanceError.set(null);
    this.attemptResult.set(null);
    try {
      this.balanced.set(formatEquation(balance(this.equation)));
    } catch (e) {
      this.balanced.set(null);
      const code = (e as Error).message.split(':')[0];
      this.balanceError.set(this.i18n.t('tools.balanceErrors.' + (['no_arrow', 'impossible', 'ambiguous', 'element', 'unbalanced'].includes(code) ? code : 'invalid'), { el: (e as Error).message.split(':')[1] ?? '' }));
    }
  }

  /** Checks the student's own coefficients without revealing the answer first. */
  protected checkAttempt(): void {
    try {
      const result = balance(this.equation);
      const given = this.attemptCoefficients.split(/[,\s]+/).filter(Boolean).map(Number);
      if (given.length !== result.coefficients.length || given.some(n => !Number.isInteger(n) || n <= 0)) { this.attemptResult.set(false); return; }
      // Any positive multiple of the reduced solution is also balanced.
      const ratio = given[0] / result.coefficients[0];
      this.attemptResult.set(result.coefficients.every((c, i) => Math.abs(given[i] - c * ratio) < 1e-9));
    } catch {
      this.attemptResult.set(false);
    }
  }

  protected async getWritingFeedback(): Promise<void> {
    if (!this.practiceBookId || this.writing.trim().length < 10) return;
    this.feedbackBusy.set(true);
    this.feedback.set('');
    this.feedbackError.set(null);
    try {
      const conversation = await this.api.post<Conversation>('/api/tutor/conversations', { bookId: this.practiceBookId, title: this.i18n.t('tools.writingFeedbackTitle') });
      const question = `${this.i18n.t('tools.writingPrompt')}\n\n"""\n${this.writing.trim()}\n"""`;
      await this.streamer.stream(`/api/tutor/conversations/${conversation.id}/messages`, { question }, new AbortController().signal, event => {
        if (event.type === 'delta') this.feedback.update(t => t + event.text);
        if (event.type === 'done') this.feedback.set(event.message.content);
        if (event.type === 'error') this.feedbackError.set(errorMessage(this.i18n, new ApiError({ status: 0, code: event.code })));
      });
    } catch (e) {
      this.feedbackError.set(errorMessage(this.i18n, e));
    } finally {
      this.feedbackBusy.set(false);
    }
  }

  protected elementName(e: Element): string {
    return this.i18n.lang() === 'ar' ? e.ar : e.en;
  }

  protected gridColumn(e: Element): number {
    return e.group;
  }
}
