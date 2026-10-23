// Browser smoke test for the running app (see e2e/README.md).
// Drives the real Riwaq app in Microsoft Edge: register → onboarding → upload → process → reader → language switch.
import { chromium } from 'playwright-core';
import { writeFileSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const BASE = process.env.RIWAQ_URL ?? 'http://localhost:4200';
const OUT = fileURLToPath(new URL('./shots/', import.meta.url));
mkdirSync(OUT, { recursive: true });
const errors = [];

const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });

// 1. Build a real Arabic textbook PDF (embedded text) by printing HTML.
const maker = await browser.newPage();
const para = 'تُعرَّف القوة بأنها مؤثر خارجي يغيّر أو يحاول أن يغيّر من الحالة الحركية للجسم. وينص قانون نيوتن الثاني على أن القوة المحصلة تساوي حاصل ضرب الكتلة في العجلة.';
await maker.setContent(`<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><style>
  body{font-family:'Segoe UI',Tahoma,Arial;font-size:15px;line-height:1.9;margin:48px} h1{font-size:30px} .pb{page-break-after:always}
  .eq{direction:ltr;text-align:center;font-size:18px}</style></head><body>
  <section class="pb"><h1>الفصل الأول: القوة والحركة</h1><p>${para}</p><p class="eq">F = m × a</p><p>وحدة قياس القوة في النظام الدولي هي النيوتن (N)، وهو القوة التي إذا أثرت في جسم كتلته واحد كيلوغرام أكسبته عجلة مقدارها واحد متر لكل ثانية تربيع.</p></section>
  <section class="pb"><p>عند تحليل القوى على مستوى مائل تكون المركبة الموازية للمستوى mg sinθ والمركبة العمودية mg cosθ. والاحتكاك قوة تعاكس الحركة النسبية بين سطحين متلامسين.</p><p>${para}</p></section>
  <section class="pb"><h1>الفصل الثاني: الطاقة</h1><p>طاقة الحركة هي الطاقة التي يمتلكها الجسم بسبب حركته وتساوي نصف الكتلة في مربع السرعة.</p><p class="eq">KE = ½ m v²</p><p>وطاقة الوضع قرب سطح الأرض تساوي mgh. والطاقة في النظام المعزول محفوظة؛ فهي تتحول من صورة إلى أخرى ولا تفنى ولا تُستحدث.</p></section>
  <section><p>الشغل يُبذل عندما تحرّك قوةٌ جسمًا مسافةً ما، والقدرة هي المعدل الزمني لبذل الشغل وتقاس بالواط.</p></section>
  </body></html>`);
const pdfPath = OUT + 'physics-ar.pdf';
writeFileSync(pdfPath, await maker.pdf({ format: 'A4' }));
await maker.close();

const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'ar' });
const page = await context.newPage();
page.setDefaultTimeout(60_000);
const origGoto = page.goto.bind(page);
page.goto = (url, opts) => origGoto(url, { waitUntil: 'domcontentloaded', ...opts });
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
page.on('pageerror', e => errors.push(String(e)));
const shot = async name => { await page.screenshot({ path: OUT + name + '.png' }); console.log('shot', name); };

// 2. Login page (Arabic, RTL by default)
await page.goto(BASE);
await page.waitForSelector('text=أهلًا بعودتك');
console.log('dir =', await page.evaluate(() => document.documentElement.dir));
await shot('01-login-ar');

// 3. Register
await page.click('text=إنشاء حساب');
await page.fill('#name', 'سارة');
await page.fill('#email', `sara-${Date.now()}@example.com`);
await page.fill('#password', 'Passw0rd-123');
await page.click('button[type=submit]');
await page.waitForURL('**/onboarding');
await page.fill('#grade', 'الصف الثالث الثانوي');
await shot('02-onboarding');
for (let i = 0; i < 5; i++) await page.click('button.btn.primary');
await page.click('text=ابدأ المذاكرة');
await page.waitForSelector('text=ارفع أول كتاب');
await shot('03-dashboard-empty');

// 4. Upload the PDF
await page.click('.sidebar .upload-cta');
await page.setInputFiles('input[type=file]', pdfPath);
await page.selectOption('#subject', { label: 'الفيزياء' });
await page.fill('#title', 'الفيزياء — الصف الثالث');
await shot('04-upload');
await page.click('.dialog-footer .btn.primary');
await page.waitForSelector('text=كتابك جاهز', { timeout: 90_000 });
await shot('05-processed');
await page.click('text=ابدأ المذاكرة');

// 5. Reader
await page.waitForSelector('.page.loaded canvas', { timeout: 30_000 });
await page.waitForTimeout(1200);
await shot('06-reader');
console.log('diag', JSON.stringify(await page.evaluate(() => ({
  pages: document.querySelectorAll('.page').length, loaded: document.querySelectorAll('.page.loaded').length,
  layers: document.querySelectorAll('.textLayer').length, spans: document.querySelectorAll('.textLayer span').length,
  sample: [...document.querySelectorAll('.textLayer span')].slice(0, 5).map(s => s.textContent)
}))));
console.log('errors so far', JSON.stringify(errors));
// Select a run of glyphs in the first page's text layer, as a mouse drag would.
const selected = await page.evaluate(() => {
  const spans = [...document.querySelector('.page[data-page="1"] .textLayer').querySelectorAll('span')];
  const range = document.createRange();
  range.setStart(spans[30].firstChild, 0);
  range.setEnd(spans[90].firstChild, spans[90].textContent.length);
  const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
  document.querySelector('.scroller').dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
  return sel.toString();
});
await page.waitForSelector('.selection-bar', { timeout: 5000 }).catch(() => console.log('no selection bar'));
await page.waitForTimeout(800);
console.log('tutor context shows:', await page.locator('.composer .context span.grow').textContent().catch(() => '(none)'));
await shot('07-selection');
await page.click('.selection-bar .swatch.sw-yellow').catch(() => {});
await page.waitForTimeout(600);
// Tutor panel (AI not configured here → honest notice)
await shot('08-reader-highlight');
// Contents panel search
await page.click('.left button[role=tab] >> nth=3');
await page.fill('.left .search input', 'الطاقة');
await page.press('.left .search input', 'Enter');
await page.waitForSelector('.left .hit', { timeout: 10_000 }).catch(() => console.log('no search hits'));
await shot('09-reader-search');

// 6. Flashcard from the reader tools, then switch to English (LTR)
await page.goto(BASE + '/');
await page.waitForSelector('.hero');
await shot('10-dashboard-ar');
await page.click('.lang-btn');
await page.waitForSelector('text=Today\'s plan');
console.log('dir =', await page.evaluate(() => document.documentElement.dir));
await shot('11-dashboard-en');
await page.goto(BASE + '/library');
await page.waitForSelector('.book');
await shot('12-library-en');
await Promise.all([page.waitForResponse(r => r.url().includes('/api/me/preferences')), page.click('.lang-btn')]);
await page.goto(BASE + '/flashcards');
await page.waitForTimeout(800);
await shot('13-flashcards-ar');
await page.goto(BASE + '/tools/equation-balancer');
await page.click('button:has-text("اعرض المعادلة الموزونة")');
await page.waitForSelector('.balanced .katex', { timeout: 8000 }).catch(async () => {
  console.log('balancer DOM:', (await page.locator('.page').first().innerText()).slice(0, 600));
});
await shot('14-balancer');
await page.goto(BASE + '/plan');
await page.waitForTimeout(800);
await shot('15-plan');
await page.setViewportSize({ width: 390, height: 844 });
await page.goto(BASE + '/');
await page.waitForSelector('.hero');
await shot('16-mobile-dashboard');

console.log('console errors:', JSON.stringify(errors.filter(e => !e.includes('favicon')), null, 1));
console.log('note: one 401 is the expected session-restore probe before login');
await browser.close();
