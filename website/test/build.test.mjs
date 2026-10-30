import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const website = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dist = path.join(website, 'dist');
const read = file => fs.readFileSync(path.join(dist, file), 'utf8');
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const walk = directory => fs.readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name, 'en'))
  .flatMap(e => e.isDirectory() ? walk(path.join(directory, e.name)) : [path.join(directory, e.name)]);
const build = (...args) => execFileSync(process.execPath, ['build.mjs', ...args], { cwd: website, stdio: 'inherit' });
let entries;
before(() => { build(); entries = JSON.parse(read('reference-index.json')); });
// Leave the usual GitHub Pages build available for preview after testing other bases.
after(() => { if (JSON.parse(read('build-info.json')).basePath !== '/CopperSharp68k') build(); });

test('recovered publication files retain their original bytes', () => {
  const recovery = JSON.parse(fs.readFileSync(path.join(website, 'recovery.json'), 'utf8'));
  for (const file of recovery.files)
    assert.equal(hash(path.join(website, file.target)), file.sha256, file.source);
});

test('current SDK API, nested enums, XML summaries and fixed buffers are documented', () => {
  const openRaw = entries.find(e => e.type === 'Amiga.DOS' && e.kind === 'method' && e.name === 'OpenRaw');
  assert.ok(openRaw, 'resident OpenRaw API is present');
  assert.match(openRaw.signature, /BPTR OpenRaw\(CString name, DOS\.FileMode accessMode\)/);
  assert.match(openRaw.summary, /zero BPTR/);
  assert.equal(openRaw.library, 'dos.library');
  const flags = entries.find(e => e.type === 'Amiga.Exec.MemoryFlags' && e.kind === 'enum');
  assert.equal(flags.href, '/reference/exec/memory-flags', 'unambiguous deployed URL survives type nesting');
  assert.match(flags.signature, /: uint$/);
  assert.ok(!entries.some(e => e.type === flags.type && e.kind === 'constructor'), 'no synthetic enum constructors');
  assert.ok(entries.some(e => e.type === 'Amiga.AChain' && e.signature === 'public fixed byte Pattern[1]'));
  assert.ok(!entries.some(e => /MUISunflower|Program$|GuestCodecs|e__FixedBuffer/.test(e.type || '')), 'examples and compiler internals stay out');
});

test('reference preserves LVOs, argument and return registers, and source links', () => {
  const page = read('reference/dos/dos/index.html');
  const member = page.match(/<article class="reference-member" id="method-open-raw">([\s\S]*?)<\/article>/)?.[1];
  assert.ok(member);
  assert.match(member, /<dt>LVO<\/dt><dd>-30<\/dd>/);
  assert.match(member.replaceAll('&#183;', '·'), /name: D1 · accessMode: D2 · return: D0/);
  assert.ok(/\/Sdk\.Amiga\/DOS\/DOS\.cs#L\d+/.test(page), 'source link uses the tracked filename and line');
});

function checkLinks(base) {
  const htmlFiles = walk(dist).filter(f => f.endsWith('.html'));
  const documents = new Map(htmlFiles.map(file => [file, fs.readFileSync(file, 'utf8')]));
  const ids = new Map([...documents].map(([file, text]) => {
    const values = [...text.matchAll(/\bid="([^"]+)"/g)].map(m => m[1]);
    assert.equal(new Set(values).size, values.length, `duplicate id: ${file}`);
    return [file, new Set(values)];
  }));
  const resolve = (href, from) => {
    const url = new URL(href.replaceAll('&amp;', '&'), 'https://preview.invalid' + base + '/' +
      path.relative(dist, from).replaceAll('\\', '/'));
    if (url.origin !== 'https://preview.invalid') return;
    assert.ok(!base || url.pathname === base || url.pathname.startsWith(base + '/'), `base mismatch: ${href}`);
    let file = path.join(dist, decodeURIComponent(url.pathname.slice(base.length)));
    if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
    assert.ok(fs.existsSync(file), `missing ${href} in ${path.relative(dist, from)}`);
    if (url.hash && ids.has(file))
      assert.ok(ids.get(file).has(decodeURIComponent(url.hash.slice(1))), `missing anchor ${href}`);
  };
  for (const [file, text] of documents)
    for (const match of text.matchAll(/\b(?:href|src)="([^"]+)"/g)) resolve(match[1], file);
  const hrefs = new Set();
  for (const entry of JSON.parse(read('reference-index.json'))) {
    assert.ok(!hrefs.has(entry.href), `duplicate search target ${entry.href}`);
    hrefs.add(entry.href);
    resolve(base + entry.href, path.join(dist, 'reference/index.html'));
  }
  const sitemap = read('sitemap.xml');
  for (const match of sitemap.matchAll(/<loc>(.*?)<\/loc>/g)) {
    const url = new URL(match[1]);
    resolve(url.pathname, path.join(dist, 'index.html'));
  }
}

test('all local page assets, navigation, search targets and sitemap entries resolve', () => checkLinks('/CopperSharp68k'));

test('a repeated build produces identical files', () => {
  const first = new Map(walk(dist).map(file => [path.relative(dist, file), hash(file)]));
  build();
  const second = new Map(walk(dist).map(file => [path.relative(dist, file), hash(file)]));
  assert.deepEqual(second, first);
});

test('root and custom project paths build with working links', () => {
  for (const base of ['', '/docs/sdk']) {
    build('--base', base);
    checkLinks(base);
    assert.equal(JSON.parse(read('build-info.json')).basePath, base);
    assert.ok(!read('reference/index.html').includes('/CopperSharp68k/'));
    for (const file of ['index.html', 'reference/index.html'])
      assert.ok(read(file).includes('href="https://github.com/ilehtoranta/CopperSharp68k"'), 'repository links keep their original URL');
    const search = read('reference-search.js');
    assert.ok(search.includes(`? "${base}"`), 'search fetch and result prefix use the selected base');
  }
});
