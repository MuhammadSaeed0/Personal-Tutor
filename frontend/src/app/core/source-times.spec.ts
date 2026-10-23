import { describe, expect, it } from 'vitest';
import { youTubeId } from '../features/library/upload-dialog.component';
import { renderMarkdown } from '../shared/markdown.component';
import { formatClock } from './source-times.service';

describe('video sources', () => {
  it('formats clock times', () => {
    expect(formatClock(0)).toBe('0:00');
    expect(formatClock(75)).toBe('1:15');
    expect(formatClock(3725)).toBe('1:02:05');
  });

  it('recognises YouTube links like the server', () => {
    expect(youTubeId('https://www.youtube.com/watch?v=Jc1nugDYX2c&t=4')).toBe('Jc1nugDYX2c');
    expect(youTubeId('youtu.be/Jc1nugDYX2c?si=x')).toBe('Jc1nugDYX2c');
    expect(youTubeId('https://m.youtube.com/shorts/Jc1nugDYX2c')).toBe('Jc1nugDYX2c');
    expect(youTubeId('https://vimeo.com/123')).toBeNull();
    expect(youTubeId('https://www.youtube.com/@channel')).toBeNull();
  });

  it('shows page references of videos as times', () => {
    const times: Record<number, number> = { 3: 250 };
    const html = renderMarkdown('see [p:3] and [p:9]', [], 'ar', (_, page) => times[page] ?? null);
    expect(html).toContain('▶ 4:10');
    expect(html).toContain('ص 9');
  });
});
