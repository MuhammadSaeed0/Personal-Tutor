import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { BrandComponent } from '../../layout/brand.component';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'rw-auth-layout',
  imports: [RouterOutlet, TranslatePipe, BrandComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth">
      <section class="art" aria-hidden="true">
        <div class="arches">
          @for (i of [0,1,2,3,4]; track i) { <span class="arch" [style.animation-delay.ms]="i * 90"></span> }
        </div>
        <div class="art-content">
          <rw-brand [size]="44" />
          <h1>{{ 'auth.heroTitle' | t }}</h1>
          <p>{{ 'auth.heroText' | t }}</p>
          <ul>
            <li><rw-icon name="message-circle" /> {{ 'auth.point1' | t }}</li>
            <li><rw-icon name="layers" /> {{ 'auth.point2' | t }}</li>
            <li><rw-icon name="target" /> {{ 'auth.point3' | t }}</li>
          </ul>
        </div>
      </section>
      <section class="panel">
        <div class="panel-top">
          <span class="mobile-brand"><rw-brand [size]="32" [dark]="false" /></span>
          <button class="btn ghost sm" type="button" (click)="i18n.setLang(i18n.lang() === 'ar' ? 'en' : 'ar')">
            <rw-icon name="languages" [size]="16" /> {{ i18n.lang() === 'ar' ? 'English' : 'العربية' }}
          </button>
        </div>
        <div class="form-wrap"><router-outlet /></div>
        <p class="legal subtle xsmall">{{ 'auth.privacyNote' | t }}</p>
      </section>
    </div>
  `,
  styles: [`
    .auth { display: grid; grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr); min-height: 100vh; }
    .art {
      position: relative; overflow: hidden; color: #e8eef5;
      background: radial-gradient(120% 90% at 85% 0%, #17304a 0%, #0f1b2a 55%, #0a131e 100%);
      display: flex; align-items: flex-end; padding: 56px;
    }
    .arches { position: absolute; inset: 0; display: flex; justify-content: center; align-items: flex-end; gap: 18px; padding: 0 40px; opacity: 0.55; }
    .arch {
      width: 17%; height: 74%; border: 2px solid rgba(43, 181, 168, 0.35); border-bottom: 0;
      border-radius: 50% 50% 0 0 / 34% 34% 0 0; animation: rise 900ms var(--ease) both;
    }
    .arch:nth-child(odd) { height: 62%; border-color: rgba(233, 223, 201, 0.22); }
    @keyframes rise { from { transform: translateY(40px); opacity: 0; } }
    .art-content { position: relative; max-width: 460px; display: flex; flex-direction: column; gap: 18px;
      background: linear-gradient(to top, rgba(10, 19, 30, 0.85), rgba(10, 19, 30, 0)); padding-top: 60px; }
    h1 { font-size: 2rem; line-height: 1.25; color: #fff; }
    p { color: #b8c4d2; margin: 0; font-size: 1rem; }
    ul { list-style: none; padding: 0; margin: 6px 0 0; display: grid; gap: 10px; }
    li { display: flex; align-items: center; gap: 10px; color: #d7e0ea; font-size: 0.92rem; }
    li rw-icon { color: #2bb5a8; }
    .panel { display: flex; flex-direction: column; padding: 24px 32px; background: var(--bg); }
    .panel-top { display: flex; justify-content: space-between; align-items: center; }
    .mobile-brand { visibility: hidden; }
    .form-wrap { flex: 1; display: grid; place-items: center; padding: 24px 0; }
    .legal { text-align: center; margin: 0; }
    @media (max-width: 900px) {
      .auth { grid-template-columns: 1fr; }
      .art { display: none; }
      .mobile-brand { visibility: visible; }
      .panel { padding: 16px; }
    }
  `]
})
export class AuthLayoutComponent {
  protected readonly i18n = inject(I18nService);
}
