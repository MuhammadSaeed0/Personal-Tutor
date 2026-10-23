import { fold, normalizeForms, orientSelection, reverseVisual } from './selection-text';

const pageText = 'تعرف القوة بأنها مؤثر خارجي يغير من الحالة الحركية للجسم. وينص قانون نيوتن الثاني على أن القوة المحصلة تساوي حاصل ضرب الكتلة في العجلة. وحدة القوة هي النيوتن (N).';

describe('selection text orientation', () => {
  it('normalises presentation forms but keeps superscripts', () => {
    expect(normalizeForms('ﺗﻮﻴﻧ m²')).toBe('توين m²');
  });

  it('reverses visual order while keeping numbers and Latin runs readable', () => {
    expect(normalizeForms(reverseVisual('ﺔﻗﺎﻄﻟا 12 KE'))).toBe('KE 12 الطاقة');
    expect(orientSelection('نتوينلا )N(', null)).toContain('(N)');
  });

  it('picks the reversed reading when it matches the page text (real pdf.js selection)', () => {
    const raw = 'ﺗﻮﻴﻧ نﻮﻧﺎﻗ ﺺﻨﻳ. وﻢﺴﺠﻠﻟ ﺔﻴﻛﺮﺤﻟا ﺔﻟﺎﺤﻟا ﻦﻣ';
    const oriented = orientSelection(raw, pageText);
    expect(fold(pageText)).toContain(fold(oriented).slice(0, 12));
    expect(oriented.startsWith('من الحالة')).toBe(true);
  });

  it('snaps a garbled multi-line pdf.js selection to the exact page passage', () => {
    // Real selection captured from Edge's pdf.js text layer across two lines.
    const raw = 'ﺗﻮﻴﻧ نﻮﻧﺎﻗ ﺺﻨﻳ. وﻢﺴﺠﻠﻟ ﺔﻴﻛﺮﺤﻟا ﺔﻟﺎﺤﻟا ﻦﻣ ﺮّﻴﻐﻳ ول أنﺎﺤﻳ أو ﺮّ ﻴﻐﻳ ﻲﺟ';
    const page = 'تعرف القوة بأنها مؤثر خارجي يغيّر أو يحاول أن يغيّر من الحالة الحركية للجسم. وينص قانون نيوتن الثاني على أن القوة المحصلة تساوي حاصل ضرب الكتلة في العجلة.';
    expect(orientSelection(raw, page)).toBe('خارجي يغيّر أو يحاول أن يغيّر من الحالة الحركية للجسم. وينص قانون نيوتن');
  });

  it('keeps already-logical text unchanged', () => {
    expect(orientSelection('قانون نيوتن الثاني', pageText)).toBe('قانون نيوتن الثاني');
    expect(orientSelection('Newton second law', null)).toBe('Newton second law');
  });
});
