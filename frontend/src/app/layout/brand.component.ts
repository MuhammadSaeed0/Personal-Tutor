import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** The Riwaq mark: two nested arches (a riwaq is the arcaded study hall of a historic mosque-university). */
@Component({
  selector: 'rw-brand',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 64 64" aria-hidden="true">
      <rect width="64" height="64" rx="16" [attr.fill]="dark() ? '#0F1B2A' : 'var(--brand-soft)'"/>
      <path d="M14 50V31c0-9 8-15 18-21 10 6 18 12 18 21v19" fill="none" [attr.stroke]="dark() ? '#2BB5A8' : 'var(--brand)'" stroke-width="4" stroke-linecap="round"/>
      <path d="M23 50V35c0-5 4-8.5 9-12 5 3.5 9 7 9 12v15" fill="none" [attr.stroke]="dark() ? '#E9DFC9' : 'var(--gold)'" stroke-width="3.5" stroke-linecap="round"/>
    </svg>
    @if (showName()) {
      <span class="names">
        <span class="ar">رِواق</span>
        <span class="en">Riwaq Learning</span>
      </span>
    }
  `,
  styles: [`
    :host { display: inline-flex; align-items: center; gap: 10px; }
    .names { display: flex; flex-direction: column; gap: 3px; line-height: 1.25; }
    .ar { font-family: var(--font-brand); font-size: 1.3rem; font-weight: 600; letter-spacing: 0; line-height: 1.1; }
    .en { font-family: var(--font-latin); font-size: 0.66rem; letter-spacing: 0.14em; text-transform: uppercase; opacity: 0.65; }
  `]
})
export class BrandComponent {
  readonly size = input(34);
  readonly dark = input(true);
  readonly showName = input(true);
}
