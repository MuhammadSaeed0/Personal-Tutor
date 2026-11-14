import { speakable } from './voice.service';

describe('speakable', () => {
  it('drops Markdown marks, citation numbers and links', () => {
    expect(speakable('## قانون نيوتن\n\n- **القوة** تساوي الكتلة في العجلة [1]\n- انظر [الكتاب](https://example.com) [p:3]'))
      .toBe('قانون نيوتن القوة تساوي الكتلة في العجلة انظر الكتاب');
  });

  it('turns LaTeX into readable symbols', () => {
    expect(speakable('القانون $F = m \\times a$ والسرعة $\\frac{d}{t}$')).toBe('القانون F = m × a والسرعة d / t');
  });

  it('skips code blocks', () => {
    const fence = '`'.repeat(3);
    expect(speakable(`قبل\n${fence}\nx = 1\n${fence}\nبعد`)).toBe('قبل بعد');
  });
});
