// Verifies that every translation key referenced literally in the app exists in both dictionaries.
// Usage: node scripts/check-i18n.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../src/app', import.meta.url));

function loadDictionary(file) {
  const source = readFileSync(join(root, 'core', 'i18n', file), 'utf8')
    .replace(/^import .*$/gm, '')
    .replace(/^\/\*\*.*\*\/$/gm, '')
    .replace(/export const \w+(?::\s*typeof \w+)?\s*=/, 'return ');
  return new Function(source)();
}

function flatten(obj, prefix = '', out = {}) {
  for (const [k, v] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${k}` : k;
    if (typeof v === 'string') out[path] = v;
    else flatten(v, path, out);
  }
  return out;
}

function walk(dir, files = []) {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) walk(full, files);
    else if (/\.(ts|html)$/.test(name) && !name.endsWith('.spec.ts') && !full.includes('i18n')) files.push(full);
  }
  return files;
}

const patterns = [
  /'([a-zA-Z]\w*(?:\.[\w-]+)+)'\s*\|\s*t\b/g,
  /\bt\(\s*'([a-zA-Z]\w*(?:\.[\w-]+)+)'/g,
  /\b(?:success|has)\(\s*'([a-zA-Z]\w*(?:\.[\w-]+)+)'/g,
  /label: '([a-z]+\.[\w.]+)'/g
];

const used = new Set();
for (const file of walk(root)) {
  const text = readFileSync(file, 'utf8');
  for (const p of patterns) for (const m of text.matchAll(p)) if (!m[1].endsWith('.')) used.add(m[1]);
}

let failed = false;
for (const [name, file] of [['en', 'en.ts'], ['ar', 'ar.ts']]) {
  const keys = new Set(Object.keys(flatten(loadDictionary(file))));
  const missing = [...used].filter(k => !keys.has(k)).sort();
  if (missing.length) {
    failed = true;
    console.error(`Missing ${name} keys (${missing.length}):\n  ${missing.join('\n  ')}`);
  }
}
console.log(`Checked ${used.size} literal keys.`);
process.exit(failed ? 1 : 0);
