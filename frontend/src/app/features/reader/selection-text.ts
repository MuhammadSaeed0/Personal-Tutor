/**
 * Text selected in pdf.js text layers often comes back in *visual* order for Arabic (glyph by glyph,
 * right-to-left reversed) and in presentation forms (ﺗﻮﻴﻧ). These helpers turn it back into logical,
 * searchable text by comparing both orientations with the page text extracted on the server.
 */

const ARABIC = /[\u0600-\u06FF\u0750-\u077F\uFB50-\uFDFF\uFE70-\uFEFF]/;
const LTR_RUN = /[A-Za-z0-9\u00B2\u00B3\u00B9\u2070-\u209F.,=+\-×÷%/^]+/g;
const MIRROR: Record<string, string> = { '(': ')', ')': '(', '[': ']', ']': '[', '{': '}', '}': '{', '«': '»', '»': '«' };
const CLOSING = new Set([')', ']', '}', '»']);

/** Converts Arabic presentation forms (and Latin ligatures) to normal letters without touching m², H₂O etc. */
export function normalizeForms(text: string): string {
  let out = '';
  for (const ch of text) {
    const code = ch.codePointAt(0)!;
    out += (code >= 0xfb50 && code <= 0xfdff) || (code >= 0xfe70 && code <= 0xfeff) || (code >= 0xfb00 && code <= 0xfb06) ? ch.normalize('NFKC') : ch;
  }
  return out;
}

/** Reverses a visually-ordered RTL string while keeping embedded left-to-right runs (numbers, Latin, formulas) intact. */
export function reverseVisual(text: string): string {
  return [...text].reverse().join('').replace(LTR_RUN, run => [...run].reverse().join(''));
}

/** A word whose first bracket is a closing one was drawn with mirrored glyphs (same rule as the server). */
export function fixMirroredBrackets(text: string): string {
  return text.split(' ').map(word => {
    const first = [...word].find(c => c in MIRROR);
    return first && CLOSING.has(first) ? [...word].map(c => MIRROR[c] ?? c).join('') : word;
  }).join(' ');
}

/** Folds text for comparison: strips diacritics, tatweel, punctuation, whitespace; unifies letter variants. */
export function fold(text: string): string {
  return text
    .normalize('NFKD')
    .replace(/[\u064B-\u065F\u0670\u0640\u0300-\u036F]/g, '')
    .replace(/[أإآٱ]/g, 'ا').replace(/ى/g, 'ي').replace(/ة/g, 'ه').replace(/ؤ/g, 'و').replace(/ئ/g, 'ي')
    .replace(/[^\p{L}\p{N}]/gu, '')
    .toLowerCase();
}

/** Folds the page text while remembering, for each folded character, its index in the original text. */
function foldWithMap(text: string): { folded: string; map: number[] } {
  let folded = '';
  const map: number[] = [];
  for (let i = 0; i < text.length; i++) {
    for (const ch of fold(text[i])) {
      folded += ch;
      map.push(i);
    }
  }
  return { folded, map };
}

/**
 * Finds the passage of the server-extracted page text that the selection covers, using the densest
 * cluster of matching 6-character windows from both reading directions. This repairs spacing and
 * line-order artefacts that pdf.js text layers introduce. Returns null when the match is not reliable.
 */
export function snapToPage(raw: string, pageText: string): string | null {
  const { folded, map } = foldWithMap(pageText);
  const candidates = [fold(normalizeForms(raw)), fold(normalizeForms(reverseVisual(raw)))];
  const length = Math.max(...candidates.map(c => c.length));
  if (length < 8 || folded.length === 0) return null;

  const positions: number[] = [];
  let windows = 0;
  for (const c of candidates) {
    for (let i = 0; i + 6 <= c.length; i += 2) {
      windows++;
      const w = c.slice(i, i + 6);
      for (let idx = folded.indexOf(w); idx >= 0; idx = folded.indexOf(w, idx + 1)) positions.push(idx);
    }
  }
  if (positions.length < 3) return null;
  positions.sort((a, b) => a - b);

  let best: [number, number] = [0, 0];
  let bestCount = 0;
  for (let i = 0, j = 0; i < positions.length; i++) {
    while (positions[i] - positions[j] > length * 1.3) j++;
    if (i - j + 1 > bestCount) {
      bestCount = i - j + 1;
      best = [positions[j], positions[i]];
    }
  }
  // Each direction contributes half the windows; require a solid share of one direction to match.
  if (bestCount < windows / 2 * 0.4) return null;

  let start = map[best[0]];
  let end = map[Math.min(map.length - 1, best[1] + 5)] + 1;
  while (start > 0 && !/\s/.test(pageText[start - 1])) start--;
  while (end < pageText.length && !/[\s.،,؛!?]/.test(pageText[end])) end++;
  return pageText.slice(start, end).replace(/\s+/g, ' ').trim();
}

/**
 * Returns the selection in logical reading order. When the page text is known, the orientation whose
 * folded form occurs in the page wins; otherwise the original (form-normalised) text is kept.
 */
export function orientSelection(raw: string, pageText: string | null): string {
  const forward = fixMirroredBrackets(normalizeForms(raw).replace(/\s+/g, ' ').trim());
  if (!ARABIC.test(forward)) return forward;
  const backward = fixMirroredBrackets(normalizeForms(reverseVisual(raw)).replace(/\s+/g, ' ').trim());
  if (!pageText) return forward;

  const snapped = snapToPage(raw, pageText);
  if (snapped) return snapped;

  const page = fold(pageText);
  const probe = (s: string) => {
    const f = fold(s);
    if (f.length < 4) return 0;
    // Score by how many 6-character windows of the selection occur in the page text.
    let hits = 0, total = 0;
    for (let i = 0; i + 6 <= f.length; i += 3) {
      total++;
      if (page.includes(f.slice(i, i + 6))) hits++;
    }
    return total === 0 ? (page.includes(f) ? 1 : 0) : hits / total;
  };
  return probe(backward) > probe(forward) ? backward : forward;
}
