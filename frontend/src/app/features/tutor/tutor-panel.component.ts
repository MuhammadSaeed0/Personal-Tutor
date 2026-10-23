import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import {
  ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, effect, inject, input, signal, untracked, viewChild
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiError, ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { ChatMessage, Conversation, ExplanationLang } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService, errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { CitationClick, MarkdownComponent } from '../../shared/markdown.component';
import { ConfirmService } from '../../shared/ui';
import { ReaderStore } from '../reader/reader-store';
import { TutorStreamService } from './tutor-stream';

interface Pending { action: string | null; question: string; selectedText: string | null; page: number | null; }

@Component({
  selector: 'rw-tutor-panel',
  imports: [FormsModule, TranslatePipe, IconComponent, MarkdownComponent, CdkMenuTrigger, CdkMenu, CdkMenuItem],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tutor-panel.component.html',
  styleUrl: './tutor-panel.component.scss'
})
export class TutorPanelComponent implements OnDestroy {
  /** Book the conversation is about (reader). */
  readonly bookId = input<string | null>(null);
  /** Subject-level conversations (tutor page) search all of the student's books in that subject. */
  readonly subjectId = input<number | null>(null);
  readonly conversationId = input<string | null>(null);

  protected readonly i18n = inject(I18nService);
  protected readonly app = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly streamer = inject(TutorStreamService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);
  protected readonly reader = inject(ReaderStore, { optional: true });

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly inputEl = viewChild<ElementRef<HTMLTextAreaElement>>('input');

  protected readonly conversations = signal<Conversation[]>([]);
  protected readonly active = signal<Conversation | null>(null);
  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly streaming = signal(false);
  protected readonly streamText = signal('');
  protected readonly loading = signal(false);
  protected readonly attachSelection = signal(true);
  protected readonly language = signal<ExplanationLang>('ar');
  protected readonly renaming = signal(false);
  protected draft = '';
  protected renameDraft = '';
  private abort?: AbortController;
  private lastRequest?: Pending;

  protected readonly selection = computed(() => (this.attachSelection() ? this.reader?.selection() ?? null : null));
  protected readonly aiConfigured = computed(() => this.app.system()?.ai?.configured !== false);
  protected readonly lastAssistant = computed(() => [...this.messages()].reverse().find(m => m.role === 'assistant') ?? null);

  constructor() {
    effect(() => {
      const profile = this.auth.profile();
      if (profile) untracked(() => this.language.set(profile.preferences.explanationLanguage));
    });
    effect(() => {
      const bookId = this.bookId();
      const subjectId = this.subjectId();
      const requested = this.conversationId();
      untracked(() => void this.loadConversations(bookId, subjectId, requested));
    });
    // Requests coming from the reader (selection toolbar, quick actions).
    effect(() => {
      const request = this.reader?.tutorRequest();
      if (request) untracked(() => void this.send({ action: request.action, question: request.question, selectedText: request.selectedText, page: request.page }));
    });
  }

  private async loadConversations(bookId: string | null, subjectId: number | null, requested: string | null): Promise<void> {
    if (!bookId && !subjectId && !requested) return;
    try {
      const list = await this.api.get<Conversation[]>('/api/tutor/conversations', { bookId, subjectId: bookId ? null : subjectId });
      this.conversations.set(list);
      const pick = list.find(c => c.id === requested) ?? list[0] ?? null;
      if (pick) await this.select(pick);
      else {
        this.active.set(null);
        this.messages.set([]);
      }
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async select(conversation: Conversation): Promise<void> {
    if (this.streaming()) this.stop();
    this.active.set(conversation);
    this.loading.set(true);
    try {
      this.messages.set(await this.api.get<ChatMessage[]>(`/api/tutor/conversations/${conversation.id}/messages`));
      this.scrollDown();
    } finally {
      this.loading.set(false);
    }
  }

  protected async newChat(): Promise<Conversation> {
    const conversation = await this.api.post<Conversation>('/api/tutor/conversations', { bookId: this.bookId(), subjectId: this.bookId() ? null : this.subjectId() });
    this.conversations.update(list => [conversation, ...list]);
    this.active.set(conversation);
    this.messages.set([]);
    setTimeout(() => this.inputEl()?.nativeElement.focus(), 0);
    return conversation;
  }

  protected async rename(): Promise<void> {
    const c = this.active();
    if (!c || !this.renameDraft.trim()) return;
    const updated = await this.api.patch<Conversation>(`/api/tutor/conversations/${c.id}`, { title: this.renameDraft.trim() });
    this.active.set({ ...c, title: updated.title });
    this.conversations.update(list => list.map(x => (x.id === c.id ? { ...x, title: updated.title } : x)));
    this.renaming.set(false);
  }

  protected async remove(): Promise<void> {
    const c = this.active();
    if (!c) return;
    const ok = await this.confirm.ask({ title: this.i18n.t('tutor.deleteTitle'), message: this.i18n.t('tutor.deleteMessage'), confirm: this.i18n.t('common.delete'), danger: true });
    if (!ok) return;
    await this.api.delete(`/api/tutor/conversations/${c.id}`);
    const rest = this.conversations().filter(x => x.id !== c.id);
    this.conversations.set(rest);
    if (rest[0]) await this.select(rest[0]);
    else { this.active.set(null); this.messages.set([]); }
  }

  protected submit(): void {
    const question = this.draft.trim();
    const sel = this.selection();
    if (!question && !sel) return;
    void this.send({ action: null, question, selectedText: sel?.text ?? null, page: sel?.page ?? this.reader?.currentPage() ?? null });
    this.draft = '';
    if (sel) this.reader?.selection.set(null);
  }

  protected quick(action: string): void {
    void this.send({ action, question: '', selectedText: null, page: this.reader?.currentPage() ?? null });
  }

  protected followUp(text: string): void {
    void this.send({ action: null, question: text, selectedText: null, page: this.reader?.currentPage() ?? null });
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  private async send(request: Pending, retryOf?: ChatMessage): Promise<void> {
    if (this.streaming()) return;
    const conversation = this.active() ?? (await this.newChat());
    this.lastRequest = request;
    this.streaming.set(true);
    this.streamText.set('');
    this.abort = new AbortController();

    const url = retryOf
      ? `/api/tutor/conversations/${conversation.id}/messages/${retryOf.id}/retry`
      : `/api/tutor/conversations/${conversation.id}/messages`;
    const body = retryOf ? undefined : {
      question: request.question, action: request.action, selectedText: request.selectedText,
      currentPage: request.page, language: this.language()
    };
    if (retryOf) this.messages.update(list => list.filter(m => m.id !== retryOf.id));

    let assistantId: string | null = null;
    try {
      await this.streamer.stream(url, body, this.abort.signal, event => {
        switch (event.type) {
          case 'start':
            assistantId = event.assistantMessageId;
            if (!retryOf) this.messages.update(list => [...list, event.userMessage]);
            this.scrollDown();
            break;
          case 'delta':
            this.streamText.update(t => t + event.text);
            this.scrollDown(true);
            break;
          case 'done':
            this.messages.update(list => [...list, event.message]);
            this.streamText.set('');
            this.touchConversation(conversation, request);
            break;
          case 'error':
            this.messages.update(list => [...list, this.errorMessage(event.assistantMessageId ?? 'local-' + Date.now(), event.code)]);
            this.streamText.set('');
            break;
        }
      });
    } catch (error) {
      if ((error as Error).name === 'AbortError') {
        // Stopped by the student: show what was generated as a stopped message.
        if (assistantId) {
          const partial = this.streamText();
          this.messages.update(list => [...list, { ...this.errorMessage(assistantId!, null), content: partial, status: 'stopped' }]);
        }
      } else {
        const code = error instanceof ApiError ? error.code : 'network_error';
        this.messages.update(list => [...list, this.errorMessage('local-' + Date.now(), code)]);
      }
      this.streamText.set('');
    } finally {
      this.streaming.set(false);
      this.abort = undefined;
      this.scrollDown();
    }
  }

  protected stop(): void {
    this.abort?.abort();
  }

  protected retry(message: ChatMessage): void {
    if (message.id.startsWith('local-')) {
      this.messages.update(list => list.filter(m => m.id !== message.id));
      if (this.lastRequest) void this.send(this.lastRequest);
      return;
    }
    void this.send(this.lastRequest ?? { action: null, question: '', selectedText: null, page: null }, message);
  }

  protected async copy(message: ChatMessage): Promise<void> {
    await navigator.clipboard.writeText(message.content.replace(/\[(\d+)\]/g, ''));
    this.toast.success('common.copied');
  }

  protected async feedback(message: ChatMessage, value: number): Promise<void> {
    const c = this.active();
    if (!c) return;
    const next = message.feedback === value ? null : value;
    await this.api.put(`/api/tutor/conversations/${c.id}/messages/${message.id}/feedback`, { value: next });
    this.messages.update(list => list.map(m => (m.id === message.id ? { ...m, feedback: next } : m)));
  }

  protected async saveToNotes(message: ChatMessage): Promise<void> {
    const c = this.active();
    if (!c) return;
    try {
      await this.api.post('/api/notes/from-message', { conversationId: c.id, messageId: message.id });
      this.toast.success('tutor.savedToNotes');
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected openCitation(click: CitationClick): void {
    const bookId = click.citation?.bookId ?? this.bookId();
    if (this.reader && bookId === this.reader.book()?.id) this.reader.navigate(click.page);
    else if (bookId) void this.router.navigate(['/read', bookId], { queryParams: { page: click.page } });
  }

  protected errorText(code: string | null): string {
    return errorMessage(this.i18n, new ApiError({ status: 0, code: code ?? 'unknown_error' }));
  }

  protected actionLabel(action: string | null): string {
    return action ? this.i18n.t('tutor.actions.' + action) : '';
  }

  private touchConversation(conversation: Conversation, request: Pending): void {
    const title = conversation.title || (request.question || request.selectedText || this.actionLabel(request.action)).slice(0, 80);
    const updated = { ...conversation, title, updatedAt: new Date().toISOString() };
    this.active.set(updated);
    this.conversations.update(list => [updated, ...list.filter(c => c.id !== conversation.id)]);
  }

  private errorMessage(id: string, code: string | null): ChatMessage {
    return {
      id, role: 'assistant', content: '', status: 'error', citations: [], followUps: [], grounded: false, contextPage: null,
      selectedText: null, action: null, feedback: null, errorCode: code, createdAt: new Date().toISOString()
    };
  }

  private scrollDown(onlyIfNear = false): void {
    setTimeout(() => {
      const el = this.scroller()?.nativeElement;
      if (!el) return;
      if (onlyIfNear && el.scrollHeight - el.scrollTop - el.clientHeight > 160) return;
      el.scrollTop = el.scrollHeight;
    }, 0);
  }

  ngOnDestroy(): void {
    this.abort?.abort();
  }
}
