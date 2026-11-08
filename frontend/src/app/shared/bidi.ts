import { Pipe, PipeTransform } from '@angular/core';

/**
 * Mixed Arabic/Latin text (an English term, a formula or a unit inside an Arabic sentence) is laid out by the browser's
 * bidi algorithm, which scrambles Latin phrases that contain spaces, numbers or symbols and takes a block's direction
 * from its first letter, so an Arabic question that starts with "DNA" turns left-to-right. These helpers give a block
 * the direction of most of its letters and isolate each run of the other script so it stays in order as one unit.
 */

const ARABIC = /[؀-ۿݐ-ݿﭐ-﷿ﹰ-﻿]/g;
const LATIN = /[A-Za-zÀ-ɏ]/g;

const LRI = '⁦';
const RLI = '⁧';
const PDI = '⁩';

/** Runs of Latin text: letters/digits joined by the spaces and symbols found inside terms, formulas and units. */
const LATIN_RUN = /[A-Za-z0-9À-ɏ](?:[ \t.,'’\-+=/×÷%°:_^()[\]]*[A-Za-z0-9À-ɏ)\]])*/g;
/** Runs of Arabic text, joined by spaces and Arabic punctuation inside them. */
const ARABIC_RUN = /[؀-ۿݐ-ݿﭐ-﷿ﹰ-﻿](?:[ \t،؛]*[؀-ۿݐ-ݿﭐ-﷿ﹰ-﻿])*/g;

/** 'rtl' when most letters are Arabic, 'ltr' when most are Latin, null when there are no letters. */
export function textDirection(text: string | null | undefined): 'rtl' | 'ltr' | null {
  if (!text) return null;
  const arabic = text.match(ARABIC)?.length ?? 0;
  const latin = text.match(LATIN)?.length ?? 0;
  if (arabic === 0 && latin === 0) return null;
  return arabic >= latin ? 'rtl' : 'ltr';
}

/** Wraps each run of the other script in isolate marks so it is laid out as one left-to-right (or right-to-left) unit. */
export function isolateRuns(text: string, direction: 'rtl' | 'ltr'): string {
  if (direction === 'rtl') {
    return text.replace(LATIN_RUN, run => {
      if (!/[A-Za-zÀ-ɏ]/.test(run)) return run; // numbers alone already read correctly
      const kept = balanced(run);
      return LRI + kept + PDI + run.slice(kept.length);
    });
  }
  return text.replace(ARABIC_RUN, run => RLI + run + PDI);
}

/** Leaves out closing brackets the run does not open itself ("(DNA)" isolates "DNA"; the brackets stay outside). */
function balanced(run: string): string {
  let text = run;
  while (/[)\]]$/.test(text) && count(text, /[([]/g) < count(text, /[)\]]/g)) text = text.slice(0, -1);
  return text;
}

function count(text: string, pattern: RegExp): number {
  return text.match(pattern)?.length ?? 0;
}

/** Plain text laid out by its own majority direction: `{{ value | bidi }}` with `[attr.dir]="value | textDir"`. */
@Pipe({ name: 'bidi' })
export class BidiPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '';
    const direction = textDirection(value);
    return direction ? isolateRuns(value, direction) : value;
  }
}

@Pipe({ name: 'textDir' })
export class TextDirPipe implements PipeTransform {
  transform(value: string | null | undefined): 'rtl' | 'ltr' | 'auto' {
    return textDirection(value) ?? 'auto';
  }
}
