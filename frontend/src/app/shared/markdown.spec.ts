import { renderMarkdown } from './markdown.component';
import { SourceReference } from '../core/models';

const citation: SourceReference = {
  marker: 1, bookId: 'b1', bookTitle: 'Physics', chunkId: 10, chapterTitle: 'Forces', pageStart: 42, pageEnd: 42, quote: 'F = ma'
};

describe('renderMarkdown', () => {
  it('turns verified citation markers into page buttons and leaves unknown markers as text', () => {
    const html = renderMarkdown('Force equals mass times acceleration [1]. Unverified [7].', [citation], 'en');
    const doc = new DOMParser().parseFromString(html, 'text/html');
    const button = doc.querySelector('button.cite');
    expect(button?.getAttribute('data-marker')).toBe('1');
    expect(button?.getAttribute('title')).toContain('p. 42');
    expect(doc.querySelectorAll('button.cite').length).toBe(1);
    expect(doc.body.textContent).toContain('[7]');
  });

  it('renders page references as clickable chips', () => {
    const html = renderMarkdown('See [p:12] and [p:3-4]', [], 'ar');
    const doc = new DOMParser().parseFromString(html, 'text/html');
    const pages = Array.from(doc.querySelectorAll('button.cite.page')).map(b => b.getAttribute('data-page'));
    expect(pages).toEqual(['12', '3']);
    expect(doc.body.textContent).toContain('ص 12');
  });

  it('renders inline and display math and chemistry with KaTeX', () => {
    const html = renderMarkdown('Inline $E = mc^2$ and $$\\frac{a}{b}$$ and \\ce{H2O}', [], 'en');
    expect(html).toContain('katex');
    expect(html).toContain('katex-display');
    expect(html).not.toContain('$E');
    expect(html).not.toContain('katex-error'); // \ce needs the mhchem extension to be registered
  });

  it('removes scripts, event handlers and styles from model output', () => {
    const html = renderMarkdown('Hello <script>alert(1)</script><img src=x onerror="alert(2)"><span style="color:red" onclick="x()">x</span>', [], 'en');
    expect(html).not.toContain('<script');
    expect(html).not.toContain('onerror');
    expect(html).not.toContain('onclick');
    expect(html).not.toContain('<img');
    expect(html).not.toContain('color:red');
  });

  it('marks paragraphs the tutor labels as beyond the textbook', () => {
    const html = renderMarkdown('**Beyond the textbook:** extra context.', [], 'en');
    expect(html).toContain('beyond-textbook');
  });
});
