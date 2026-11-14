import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { ApiError } from '../core/api.service';
import { TranslatePipe } from '../core/i18n.service';
import { ToastService } from '../core/toast.service';
import { VoiceService } from '../core/voice.service';
import { IconComponent } from './icon.component';

/** Microphone button: dictates a question into the composer. Hidden where the browser cannot recognise speech. */
@Component({
  selector: 'rw-mic-button',
  imports: [TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (voice.canListen) {
      <button class="btn icon" type="button" [class.listening]="voice.listening()" [disabled]="disabled()" (click)="toggle()"
        [attr.aria-pressed]="voice.listening()" [attr.aria-label]="(voice.listening() ? 'voice.stopListening' : 'voice.speak') | t"
        [title]="(voice.listening() ? 'voice.stopListening' : 'voice.speak') | t">
        <rw-icon [name]="voice.listening() ? 'square' : 'mic'" [size]="16" />
      </button>
    }
  `,
  styles: [`
    .listening { color: var(--danger); border-color: var(--danger); animation: pulse 1.2s ease-in-out infinite; }
    @keyframes pulse { 50% { opacity: .55; } }
    @media (prefers-reduced-motion: reduce) { .listening { animation: none; } }
  `]
})
export class MicButtonComponent {
  readonly language = input('ar');
  readonly disabled = input(false);
  /** The transcript so far; final = the student stopped talking. */
  readonly heard = output<{ text: string; final: boolean }>();
  protected readonly voice = inject(VoiceService);
  private readonly toast = inject(ToastService);

  protected toggle(): void {
    if (this.voice.listening()) { this.voice.stopListening(); return; }
    this.voice.listen(this.language(), (text, final) => this.heard.emit({ text, final }),
      code => this.toast.error(new ApiError({ status: 0, code: code === 'not-allowed' ? 'voice_denied' : 'voice_unavailable' })));
  }
}

/** Listen button for one answer. Hidden where the browser cannot speak. */
@Component({
  selector: 'rw-speak-button',
  imports: [TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (voice.canSpeak && text()) {
      <button class="btn ghost icon sm" type="button" (click)="voice.speak(id(), text(), language())" [class.on]="voice.speaking() === id()"
        [attr.aria-pressed]="voice.speaking() === id()" [attr.aria-label]="(voice.speaking() === id() ? 'voice.stopReading' : 'voice.listen') | t"
        [title]="(voice.speaking() === id() ? 'voice.stopReading' : 'voice.listen') | t">
        <rw-icon [name]="voice.speaking() === id() ? 'square' : 'volume-2'" [size]="14" />
      </button>
    }
  `
})
export class SpeakButtonComponent {
  readonly id = input.required<string>();
  readonly text = input('');
  readonly language = input('ar');
  protected readonly voice = inject(VoiceService);
}
