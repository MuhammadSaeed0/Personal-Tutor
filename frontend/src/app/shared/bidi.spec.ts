import { BidiPipe, isolateRuns, textDirection } from './bidi';
import { renderMarkdown } from './markdown.component';

const LRI = '⁦';
const RLI = '⁧';
const PDI = '⁩';

describe('bidi', () => {
  it('takes the direction of most letters, not the first one', () => {
    expect(textDirection('DNA هو الحمض النووي الموجود في نواة الخلية')).toBe('rtl');
    expect(textDirection('The cell nucleus contains الحمض النووي')).toBe('ltr');
    expect(textDirection('3.14 + 2')).toBeNull();
  });

  it('isolates English terms, formulas and units inside Arabic as single runs', () => {
    expect(isolateRuns('يعمل إنزيم DNA polymerase على نسخ الحمض', 'rtl')).toBe(`يعمل إنزيم ${LRI}DNA polymerase${PDI} على نسخ الحمض`);
    expect(isolateRuns('درجة الحرارة 37 °C تقريبًا', 'rtl')).toBe(`درجة الحرارة ${LRI}37 °C${PDI} تقريبًا`);
    expect(isolateRuns('اسم المركب Ca(OH)2 هو', 'rtl')).toBe(`اسم المركب ${LRI}Ca(OH)2${PDI} هو`);
  });

  it('keeps brackets, punctuation and plain numbers outside the isolated run', () => {
    expect(isolateRuns('الحمض النووي (DNA) مهم.', 'rtl')).toBe(`الحمض النووي (${LRI}DNA${PDI}) مهم.`);
    expect(isolateRuns('ما قيمة pH؟', 'rtl')).toBe(`ما قيمة ${LRI}pH${PDI}؟`);
    expect(isolateRuns('في عام 2024 م', 'rtl')).toBe('في عام 2024 م');
  });

  it('isolates Arabic inside English text', () => {
    expect(isolateRuns('The term الخلية means cell', 'ltr')).toBe(`The term ${RLI}الخلية${PDI} means cell`);
  });

  it('formats plain text with the pipe', () => {
    expect(new BidiPipe().transform('ATP مصدر الطاقة في الخلية')).toBe(`${LRI}ATP${PDI} مصدر الطاقة في الخلية`);
    expect(new BidiPipe().transform(null)).toBe('');
  });

  it('gives each Markdown block its own direction and isolates English runs in Arabic ones', () => {
    const html = renderMarkdown('DNA هو الحمض النووي في نواة الخلية\n\nThe answer is correct.', [], 'ar');
    const doc = new DOMParser().parseFromString(html, 'text/html');
    const [arabic, english] = Array.from(doc.querySelectorAll('p'));
    expect(arabic.getAttribute('dir')).toBe('rtl');
    expect(arabic.textContent).toContain(`${LRI}DNA${PDI}`);
    expect(english.getAttribute('dir')).toBe('ltr');
    expect(english.textContent).not.toContain(LRI);
  });

  it('leaves math to KaTeX and does not count it towards the direction', () => {
    const html = renderMarkdown('احسب القوة $F = m a$ للجسم', [], 'ar');
    const doc = new DOMParser().parseFromString(html, 'text/html');
    expect(doc.querySelector('p')?.getAttribute('dir')).toBe('rtl');
    expect(html).toContain('katex');
    expect(html).not.toContain(LRI + 'F');
  });
});
