import { Injectable, signal } from '@angular/core';

/** Minimal typing for the Web Speech recognition API (not in the TypeScript DOM library everywhere). */
interface SpeechRecognitionLike {
  lang: string; interimResults: boolean; continuous: boolean; maxAlternatives: number;
  onresult: ((e: { resultIndex: number; results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal: boolean }> }) => void) | null;
  onerror: ((e: { error: string }) => void) | null;
  onend: (() => void) | null;
  start(): void; stop(): void; abort(): void;
}

const VOICES: Record<string, string> = { ar: 'ar-EG', en: 'en-US', fr: 'fr-FR' };

/**
 * Speaking to the tutor and listening to its answers, with the browser's own speech services
 * (nothing is sent to Riwaq's server). The buttons hide themselves where the browser has no support.
 */
@Injectable({ providedIn: 'root' })
export class VoiceService {
  private readonly Recognition: (new () => SpeechRecognitionLike) | undefined =
    (window as unknown as Record<string, new () => SpeechRecognitionLike>)['SpeechRecognition']
    ?? (window as unknown as Record<string, new () => SpeechRecognitionLike>)['webkitSpeechRecognition'];

  readonly canListen = !!this.Recognition;
  readonly canSpeak = typeof window.speechSynthesis !== 'undefined' && typeof SpeechSynthesisUtterance !== 'undefined';

  /** The id of whatever is being read aloud now (a message id), or null. */
  readonly speaking = signal<string | null>(null);
  readonly listening = signal(false);
  private recognition: SpeechRecognitionLike | null = null;

  /** Listens until the student stops talking; calls onText with the growing transcript. */
  listen(language: string, onText: (text: string, final: boolean) => void, onError: (code: string) => void): void {
    if (!this.Recognition || this.listening()) return;
    const r = new this.Recognition();
    r.lang = VOICES[language] ?? language;
    r.interimResults = true;
    r.continuous = false;
    r.maxAlternatives = 1;
    let finalText = '';
    r.onresult = e => {
      let interim = '';
      for (let i = e.resultIndex; i < e.results.length; i++) {
        const piece = e.results[i][0].transcript;
        if (e.results[i].isFinal) finalText += piece; else interim += piece;
      }
      onText((finalText + interim).trim(), interim === '');
    };
    r.onerror = e => { if (e.error !== 'aborted' && e.error !== 'no-speech') onError(e.error); };
    r.onend = () => { this.listening.set(false); this.recognition = null; };
    this.recognition = r;
    this.listening.set(true);
    try {
      r.start();
    } catch {
      this.listening.set(false);
      this.recognition = null;
      onError('voice_unavailable');
    }
  }

  stopListening(): void {
    this.recognition?.stop();
  }

  /** Reads an answer aloud (Markdown, citations and LaTeX are cleaned up first); calling it again stops it. */
  speak(id: string, markdown: string, language: string): void {
    if (!this.canSpeak) return;
    const wasSpeaking = this.speaking() === id;
    window.speechSynthesis.cancel();
    this.speaking.set(null);
    if (wasSpeaking) return;
    const utterance = new SpeechSynthesisUtterance(speakable(markdown));
    utterance.lang = VOICES[language] ?? language;
    const voice = window.speechSynthesis.getVoices().find(v => v.lang.toLowerCase().startsWith(utterance.lang.slice(0, 2).toLowerCase()));
    if (voice) utterance.voice = voice;
    utterance.onend = utterance.onerror = () => { if (this.speaking() === id) this.speaking.set(null); };
    this.speaking.set(id);
    window.speechSynthesis.speak(utterance);
  }

  stopSpeaking(): void {
    if (this.canSpeak) window.speechSynthesis.cancel();
    this.speaking.set(null);
  }
}

const FENCE = String.fromCharCode(96).repeat(3);

/** Plain text for speech: no Markdown marks, citation numbers, code blocks or LaTeX commands. */
export function speakable(markdown: string): string {
  return markdown
    .split(FENCE).filter((_, i) => i % 2 === 0).join(' ')
    .replace(/\$\$([\s\S]*?)\$\$|\$([^$\n]+)\$/g, (_, a, b) => latexToText(a ?? b))
    .replace(/\\\(([\s\S]*?)\\\)|\\\[([\s\S]*?)\\\]/g, (_, a, b) => latexToText(a ?? b))
    .replace(/\[(?:\d+(?:\s*[,،]\s*\d+)*|p:\d+)\]/g, '')
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/^\s{0,3}(#{1,6}|>|[-*+]|\d+[.)])\s+/gm, '')
    .replace(/[*_~|]+/g, '')
    .replace(/\u0060/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

function latexToText(tex: string): string {
  return ' ' + tex
    .replace(/\\frac\{([^}]*)\}\{([^}]*)\}/g, '$1 / $2')
    .replace(/\\(?:times|cdot)/g, ' × ')
    .replace(/\\(?:text|mathrm|mathbf|ce)\{([^}]*)\}/g, '$1')
    .replace(/\\[a-zA-Z]+/g, ' ')
    .replace(/[{}^_\\]/g, ' ') + ' ';
}
