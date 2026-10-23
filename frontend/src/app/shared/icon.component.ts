import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, input } from '@angular/core';
import {
  AlarmClock, ArrowLeft, ArrowRight, Atom, Award, BarChart3, Bell, BookA, BookMarked, BookOpen, BookOpenCheck, Bookmark, BookmarkPlus,
  Brain, Calendar, CalendarClock, Check, CheckCircle2, ChevronDown, ChevronLeft, ChevronRight, ChevronUp, CircleAlert, CircleHelp, Clock,
  Columns3, Copy, Dna, Download, Earth, Eye, EyeOff, FileText, FileUp, Flag, FlaskConical, Flame, Focus, GraduationCap, Hash, Highlighter,
  History, House, Info, Languages, Layers, Lightbulb, ListChecks, ListTree, LoaderCircle, LogOut, Maximize, Menu, MessageCircle,
  MessageSquarePlus, Minimize, Minus, Moon, MoreHorizontal, NotebookPen, PanelLeft, PanelRight, Pause, Pencil, Pin, Play, Plus, RefreshCw,
  RotateCcw, Save, ScrollText, Search, Send, Settings, Shield, Sigma, Sparkles, Square, Star, StickyNote, Sun, Target, ThumbsDown, ThumbsUp,
  Timer, Trash2, TrendingUp, TriangleAlert, Trophy, Upload, User, Wand2, X, ZoomIn, ZoomOut, Monitor, Mountain, Shuffle, Undo2, Image as ImageIcon
} from 'lucide';
import type { IconNode } from 'lucide';

const ICONS: Record<string, IconNode> = {
  'alarm-clock': AlarmClock, 'arrow-left': ArrowLeft, 'arrow-right': ArrowRight, atom: Atom, award: Award, 'bar-chart': BarChart3,
  bell: Bell, 'book-a': BookA, 'book-marked': BookMarked, 'book-open': BookOpen, 'book-open-check': BookOpenCheck, bookmark: Bookmark,
  'bookmark-plus': BookmarkPlus, brain: Brain, calendar: Calendar, 'calendar-clock': CalendarClock, check: Check, 'check-circle': CheckCircle2,
  'chevron-down': ChevronDown, 'chevron-left': ChevronLeft, 'chevron-right': ChevronRight, 'chevron-up': ChevronUp, 'alert-circle': CircleAlert,
  help: CircleHelp, clock: Clock, columns: Columns3, copy: Copy, dna: Dna, download: Download, earth: Earth, eye: Eye, 'eye-off': EyeOff,
  'file-text': FileText, 'file-up': FileUp, flag: Flag, 'flask-conical': FlaskConical, flame: Flame, focus: Focus, 'graduation-cap': GraduationCap,
  hash: Hash, highlighter: Highlighter, history: History, home: House, info: Info, languages: Languages, layers: Layers, lightbulb: Lightbulb,
  'list-checks': ListChecks, 'list-tree': ListTree, loader: LoaderCircle, 'log-out': LogOut, maximize: Maximize, menu: Menu,
  'message-circle': MessageCircle, 'message-plus': MessageSquarePlus, minimize: Minimize, minus: Minus, moon: Moon, more: MoreHorizontal,
  'notebook-pen': NotebookPen, 'panel-left': PanelLeft, 'panel-right': PanelRight, pause: Pause, pencil: Pencil, pin: Pin, play: Play,
  plus: Plus, refresh: RefreshCw, 'rotate-ccw': RotateCcw, save: Save, 'scroll-text': ScrollText, search: Search, send: Send,
  settings: Settings, shield: Shield, sigma: Sigma, sparkles: Sparkles, square: Square, star: Star, 'sticky-note': StickyNote, sun: Sun,
  target: Target, 'thumbs-down': ThumbsDown, 'thumbs-up': ThumbsUp, timer: Timer, trash: Trash2, 'trending-up': TrendingUp,
  'alert-triangle': TriangleAlert, trophy: Trophy, upload: Upload, user: User, wand: Wand2, x: X, 'zoom-in': ZoomIn, 'zoom-out': ZoomOut,
  monitor: Monitor, mountain: Mountain, shuffle: Shuffle, undo: Undo2, image: ImageIcon
};

/** Directional icons that mirror in right-to-left layouts. */
const MIRRORED = new Set(['arrow-left', 'arrow-right', 'chevron-left', 'chevron-right', 'send', 'log-out', 'undo']);

/**
 * Lucide icon rendered as inline SVG (built with DOM APIs, no innerHTML).
 * Decorative by default; pass `label` to expose it to screen readers.
 */
@Component({
  selector: 'rw-icon',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'rw-icon', '[style.--icon-size.px]': 'size()', '[class.mirror]': 'mirrored()', '[attr.role]': "label() ? 'img' : null", '[attr.aria-label]': 'label() || null', '[attr.aria-hidden]': "label() ? null : 'true'" },
  styles: [`
    :host { display: inline-flex; width: var(--icon-size); height: var(--icon-size); flex: none; }
    :host ::ng-deep svg { width: 100%; height: 100%; }
    :host-context([dir='rtl']).mirror { transform: scaleX(-1); }
  `]
})
export class IconComponent {
  readonly name = input.required<string>();
  readonly size = input(18);
  readonly stroke = input(1.8);
  readonly label = input<string>('');
  private readonly host = inject(ElementRef<HTMLElement>);

  protected mirrored(): boolean {
    return MIRRORED.has(this.name());
  }

  constructor() {
    effect(() => {
      const node = ICONS[this.name()] ?? ICONS['help'];
      const ns = 'http://www.w3.org/2000/svg';
      const svg = document.createElementNS(ns, 'svg');
      svg.setAttribute('viewBox', '0 0 24 24');
      svg.setAttribute('fill', 'none');
      svg.setAttribute('stroke', 'currentColor');
      svg.setAttribute('stroke-width', String(this.stroke()));
      svg.setAttribute('stroke-linecap', 'round');
      svg.setAttribute('stroke-linejoin', 'round');
      for (const [tag, attrs] of node) {
        const el = document.createElementNS(ns, tag);
        for (const [k, v] of Object.entries(attrs as Record<string, unknown>)) if (v !== undefined) el.setAttribute(k, String(v));
        svg.appendChild(el);
      }
      const host = this.host.nativeElement as HTMLElement;
      host.replaceChildren(svg);
    });
  }
}
