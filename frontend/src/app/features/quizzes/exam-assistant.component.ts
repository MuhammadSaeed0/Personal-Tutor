import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { SourceReference } from '../../core/models';
import { errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { TutorStreamService } from '../tutor/tutor-stream';

interface AssistantMessage { role: 'user' | 'assistant'; content: string; citations: SourceReference[]; error?: string; streaming?: boolean; }

type AssistantEvent =
  | { type: 'delta'; text: string }
  | { type: 'done'; content: string; citations: SourceReference[]; uses: number }
  | { type: 'error'; code: string; message: string };

/**
 * Side chat during a quiz or exam: as many questions as the student likes about meanings, terms and concepts.
 * The server tells the model which question is on screen but never gives it the answers, and it will not reveal them.
 */
@Component({
  selector: 'rw-exam-assistant',
  imports: [FormsModule, TranslatePipe, IconComponent, MarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="head">
      <div class="grow">
        <strong><rw-icon name="sparkles" [size]="16" /> {{ 'examAssistant.title' | t }}</strong>
        <p class="xsmall subtle">{{ 'examAssistant.lead' | t }}</p>
      </div>
      <button class="btn ghost icon sm" type="button" (click)="closed.emit()" [attr.aria-label]="'common.close' | t"><rw-icon name="x" /></button>
    </header>

    <div class="messages" #list aria-live="polite">
      @if (messages().length === 0) {
        <div class="empty small muted">
          <p>{{ 'examAssistant.empty' | t }}</p>
          @for (s of suggestions; track s) {
            <button class="chip" type="button" (click)="ask(i18n.t(s))">{{ s | t }}</button>
          }
        </div>
      }
      @for (m of messages(); track $index) {
        <div class="msg" [class.user]="m.role === 'user'">
          @if (m.role === 'user') { <p dir="auto">{{ m.content }}</p> }
          @else if (m.error) { <p class="error small" role="alert">{{ m.error }}</p> }
          @else { <rw-markdown [content]="m.content" [citations]="m.citations" [streaming]="!!m.streaming" />
            @if (m.streaming && !m.content) { <span class="typing small muted"><span class="spinner"></span> {{ 'examAssistant.typing' | t }}</span> } }
        </div>
      }
    </div>

    <form class="composer" (ngSubmit)="ask(draft)">
      <textarea class="textarea" name="draft" [(ngModel)]="draft" rows="2" dir="auto" [placeholder]="'examAssistant.placeholder' | t"
        (keydown.enter)="$event.preventDefault(); ask(draft)" [attr.aria-label]="'examAssistant.placeholder' | t"></textarea>
      @if (busy()) {
        <button class="btn icon" type="button" (click)="stop()" [attr.aria-label]="'tutor.stop' | t"><rw-icon name="square" [size]="16" /></button>
      } @else {
        <button class="btn primary icon" type="submit" [disabled]="!draft.trim()" [attr.aria-label]="'tutor.send' | t"><rw-icon name="send" [size]="16" /></button>
      }
    </form>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; height: 100%; background: var(--surface); }
    .head { display: flex; gap: 8px; align-items: flex-start; padding: 14px 14px 10px; border-bottom: 1px solid var(--line); }
    .head strong { display: inline-flex; align-items: center; gap: 6px; }
    .head p { margin: 2px 0 0; }
    .messages { flex: 1; overflow: auto; padding: 12px 14px; display: flex; flex-direction: column; gap: 10px; }
    .empty { display: flex; flex-direction: column; gap: 8px; align-items: flex-start; }
    .empty .chip { text-align: start; }
    .msg { max-width: 100%; }
    .msg.user { align-self: flex-end; background: var(--brand-soft); border-radius: 12px; padding: 8px 12px; max-width: 88%; }
    .msg.user p { margin: 0; }
    .composer { display: flex; gap: 8px; padding: 10px; border-top: 1px solid var(--line); align-items: flex-end; }
    .composer .textarea { flex: 1; min-height: 44px; resize: none; }
    .error { color: var(--danger); margin: 0; }
    .typing { display: inline-flex; align-items: center; gap: 8px; }
  `]
})
export class ExamAssistantComponent implements OnDestroy {
  readonly attemptId = input.required<string>();
  /** The question on screen, so explanations relate to it (its answer is never shared). */
  readonly questionId = input<string | null>(null);
  readonly language = input<string>('ar');
  readonly closed = output<void>();
  readonly used = output<number>();

  private readonly streamer = inject(TutorStreamService);
  protected readonly i18n = inject(I18nService);
  private readonly list = viewChild.required<ElementRef<HTMLElement>>('list');

  protected readonly messages = signal<AssistantMessage[]>([]);
  protected readonly busy = signal(false);
  protected draft = '';
  protected readonly suggestions = ['examAssistant.suggestMeaning', 'examAssistant.suggestLaw', 'examAssistant.suggestFigure'];
  private controller?: AbortController;

  protected async ask(text: string): Promise<void> {
    const question = text.trim();
    if (!question || this.busy()) return;
    this.draft = '';
    const history = this.messages().filter(m => !m.error && !m.streaming).slice(-8).map(m => ({ role: m.role, content: m.content }));
    this.messages.update(list => [...list, { role: 'user', content: question, citations: [] }, { role: 'assistant', content: '', citations: [], streaming: true }]);
    this.busy.set(true);
    this.scroll();
    this.controller = new AbortController();

    const update = (patch: Partial<AssistantMessage>) =>
      this.messages.update(list => list.map((m, i) => (i === list.length - 1 ? { ...m, ...patch } : m)));
    try {
      await this.streamer.stream(`/api/attempts/${this.attemptId()}/assistant`,
        { question, questionId: this.questionId(), history, language: this.language() }, this.controller.signal, raw => {
          const e = raw as unknown as AssistantEvent;
          if (e.type === 'delta') { update({ content: this.messages().at(-1)!.content + e.text }); this.scroll(); }
          else if (e.type === 'done') { update({ content: e.content, citations: e.citations, streaming: false }); this.used.emit(e.uses); }
          else if (e.type === 'error') update({ error: errorMessage(this.i18n, new ApiError({ status: 503, code: e.code })), streaming: false });
        });
      update({ streaming: false });
    } catch (e) {
      if (!(e instanceof DOMException && e.name === 'AbortError')) {
        update({ error: errorMessage(this.i18n, e instanceof ApiError ? e : new ApiError({ status: 0, code: 'network_error' })), streaming: false });
      } else {
        update({ streaming: false });
      }
    } finally {
      this.busy.set(false);
      this.scroll();
    }
  }

  protected stop(): void {
    this.controller?.abort();
  }

  private scroll(): void {
    setTimeout(() => { const el = this.list().nativeElement; el.scrollTop = el.scrollHeight; });
  }

  ngOnDestroy(): void {
    this.controller?.abort();
  }
}
