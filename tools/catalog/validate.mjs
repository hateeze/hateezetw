#!/usr/bin/env node
// Проверка *.tweaks.json по формату v1 (docs/catalog-format-v1.md).
// Без аргументов проверяет docs/research/*.tweaks.json. Код выхода 1 — есть ошибки.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateCatalogs } from './rules.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = process.argv.slice(2);
const targets = args.length ? args.map(a => resolve(a)) : [join(root, 'docs', 'research')];

const paths = [...new Set(targets.flatMap(p => (statSync(p).isDirectory()
  ? readdirSync(p).filter(f => f.endsWith('.tweaks.json')).sort().map(f => join(p, f))
  : [p])))];

const files = paths.map(p => ({ name: relative(process.cwd(), p) || p, text: readFileSync(p, 'utf8') }));
const issues = validateCatalogs(files);

for (const f of files) {
  const own = issues.filter(i => i.file === f.name);
  console.log(`\n${f.name}: ${count(own, 'error')} ошиб., ${count(own, 'warning')} предупр.`);
  for (const i of own) console.log(`  ${i.level === 'error' ? 'E' : 'W'} ${i.code.padEnd(18)} ${i.id ?? '-'}: ${i.message}`);
}

const byCode = {};
for (const i of issues) byCode[i.code] = (byCode[i.code] ?? 0) + 1;
console.log(`\nИтого: ${files.length} файл., ${count(issues, 'error')} ошиб., ${count(issues, 'warning')} предупр.`);
for (const [code, n] of Object.entries(byCode).sort()) console.log(`  ${code}: ${n}`);

process.exitCode = count(issues, 'error') > 0 ? 1 : 0;

function count(list, level) {
  return list.filter(i => i.level === level).length;
}
