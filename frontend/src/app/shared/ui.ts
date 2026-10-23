import { Dialog, DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, Injectable, computed, inject, input } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslatePipe } from '../core/i18n.service';
import { IconComponent } from './icon.component';

/** Circular progress indicator (SVG). */
@Component({
  selector: 'rw-ring',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 44 44" role="img" [attr.aria-label]="label()">
      <circle cx="22" cy="22" r="18" fill="none" stroke="var(--surface-sunken)" stroke-width="4"></circle>
      <circle cx="22" cy="22" r="18" fill="none" [attr.stroke]="color()" stroke-width="4" stroke-linecap="round"
        [attr.stroke-dasharray]="113.1" [attr.stroke-dashoffset]="offset()" transform="rotate(-90 22 22)"
        style="transition: stroke-dashoffset 600ms var(--ease)"></circle>
    </svg>
    <span class="ring-label"><ng-content /></span>
  `,
  styles: [`
    :host { position: relative; display: inline-grid; place-items: center; flex: none; }
    .ring-label { position: absolute; inset: 0; display: grid; place-items: center; font-size: 0.72rem; font-weight: 600; font-variant-numeric: tabular-nums; }
  `]
})
export class RingComponent {
  readonly value = input(0);
  readonly size = input(44);
  readonly color = input('var(--brand)');
  readonly label = input('');
  protected readonly offset = computed(() => 113.1 * (1 - Math.min(100, Math.max(0, this.value())) / 100));
}

/** Empty / onboarding state with an icon, text and optional actions. */
@Component({
  selector: 'rw-empty',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty">
      <div class="empty-icon"><rw-icon [name]="icon()" [size]="24" /></div>
      <h3>{{ title() }}</h3>
      @if (text()) { <p>{{ text() }}</p> }
      <div class="row wrap" style="justify-content:center"><ng-content /></div>
    </div>
  `
})
export class EmptyStateComponent {
  readonly icon = input('sparkles');
  readonly title = input.required<string>();
  readonly text = input('');
}

export interface ConfirmData {
  title: string;
  message: string;
  confirm: string;
  danger?: boolean;
}

@Component({
  selector: 'rw-confirm',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dialog" role="alertdialog" aria-modal="true" [attr.aria-labelledby]="'confirm-title'">
      <div class="dialog-header"><div><h2 id="confirm-title">{{ data.title }}</h2><p>{{ data.message }}</p></div></div>
      <div class="dialog-footer">
        <button class="btn ghost" type="button" (click)="ref.close(false)">{{ 'common.cancel' | t }}</button>
        <button class="btn" [class.danger]="data.danger" [class.primary]="!data.danger" type="button" (click)="ref.close(true)" cdkFocusInitial>{{ data.confirm }}</button>
      </div>
    </div>
  `
})
export class ConfirmDialogComponent {
  protected readonly data = inject<ConfirmData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(Dialog);

  async ask(data: ConfirmData): Promise<boolean> {
    const ref = this.dialog.open<boolean, ConfirmData>(ConfirmDialogComponent, { data, autoFocus: 'dialog', restoreFocus: true });
    return (await firstValueFrom(ref.closed)) === true;
  }
}

/** Formats seconds as m:ss or h:mm:ss. */
export function clock(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return h > 0 ? `${h}:${pad(m)}:${pad(sec)}` : `${m}:${pad(sec)}`;
}
