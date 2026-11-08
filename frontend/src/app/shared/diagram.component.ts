import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import DOMPurify from 'dompurify';

/**
 * A question's diagram. The SVG was already rebuilt from an allowlist on the server; it is sanitised again here
 * (SVG profile, no foreign content) and always drawn on a white card so dark strokes stay visible in dark mode.
 */
@Component({
  selector: 'rw-diagram',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="diagram" role="img" [attr.aria-label]="alt()">
      <div class="drawing" [innerHTML]="html()"></div>
      @if (alt()) { <figcaption class="visually-hidden">{{ alt() }}</figcaption> }
    </figure>
  `,
  styles: [`
    .diagram { margin: 12px 0; display: flex; justify-content: center; }
    .drawing { background: #fff; color: #111; border: 1px solid var(--line); border-radius: var(--radius); padding: 10px; width: 100%; max-width: 460px; }
    .drawing ::ng-deep svg { display: block; width: 100%; height: auto; direction: ltr; }
  `]
})
export class DiagramComponent {
  readonly svg = input.required<string>();
  readonly alt = input<string | null>(null);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly html = computed<SafeHtml>(() => {
    const clean = DOMPurify.sanitize(this.svg(), {
      USE_PROFILES: { svg: true, svgFilters: false },
      FORBID_TAGS: ['foreignObject', 'image', 'use', 'script', 'style', 'a'],
      FORBID_ATTR: ['href', 'xlink:href', 'style']
    }) as string;
    return this.sanitizer.bypassSecurityTrustHtml(clean);
  });
}
