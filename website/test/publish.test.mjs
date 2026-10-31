import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync, spawnSync } from 'node:child_process';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const artifacts = path.join(repository, 'artifacts/website-publisher-tests');
fs.mkdirSync(artifacts, { recursive: true });

// Real, isolated Git remotes exercise publication without contacting origin or
// touching the user's branches, indexes, recovery refs, or backup archives.
function fixture() {
  const root = fs.mkdtempSync(path.join(artifacts, 'publication-'));
  const checkout = path.join(root, 'checkout');
  const remote = path.join(root, 'origin.git');
  fs.mkdirSync(checkout);
  const git = (...args) => execFileSync('git', args, { cwd: checkout, encoding: 'utf8' }).trim();
  const write = (file, text) => {
    const target = path.join(checkout, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, text);
  };
  git('init', '--quiet', '--initial-branch=main');
  git('init', '--bare', '--quiet', '--initial-branch=main', remote);
  git('config', 'user.name', 'Publisher test');
  git('config', 'user.email', 'publisher@example.invalid');
  git('config', 'commit.gpgsign', 'false');
  git('config', 'core.autocrlf', 'false');
  git('remote', 'add', 'origin', remote);
  write('.gitignore', 'website/dist/\nwebsite/.build/\nartifacts/\n');
  write('source.txt', 'Validated source\n');
  write('website/publish.mjs', fs.readFileSync(path.join(repository, 'website/publish.mjs')));
  git('add', '.');
  git('commit', '--quiet', '-m', 'Validated main');
  const source = git('rev-parse', 'HEAD');
  const previous = git('commit-tree', source + '^{tree}', '-m', 'Existing publication');
  git('push', '--quiet', 'origin', 'main', previous + ':refs/heads/gh-pages');
  git('branch', 'codex/recovery-20261030', source);
  git('update-ref', 'refs/codex/recovery/test/working', previous);
  git('update-ref', 'refs/codex/backups/test', source);
  const refs = () => git('for-each-ref', '--format=%(refname) %(objectname)',
    'refs/heads/codex/recovery-20261030', 'refs/codex/backups', 'refs/codex/recovery');
  const protectedBefore = refs();
  write('artifacts/backup-sentinel.bin', Buffer.from([0, 42, 128, 255]));
  const build = revision => {
    write('website/dist/build-info.json', JSON.stringify({ sourceRevision: revision,
      basePath: '/CopperSharp68k', origin: 'https://ilehtoranta.github.io' }));
    write('website/dist/.coppersharp-generated', 'Generated fixture\n');
    write('website/dist/.nojekyll', '');
    write('website/dist/index.html', '<h1>Validated website</h1>\n');
    fs.mkdirSync(path.join(checkout, 'website/.build'), { recursive: true });
  };
  build(source);
  const heads = () => git('ls-remote', '--heads', 'origin');
  const before = heads();
  const index = fs.readFileSync(path.join(checkout, '.git/index'));
  const publish = (...args) => spawnSync(process.execPath, ['website/publish.mjs', ...args],
    { cwd: checkout, encoding: 'utf8' });
  const unchanged = () => {
    assert.equal(refs(), protectedBefore);
    assert.deepEqual(fs.readFileSync(path.join(checkout, 'artifacts/backup-sentinel.bin')), Buffer.from([0, 42, 128, 255]));
    assert.equal(git('status', '--porcelain'), '');
    assert.deepEqual(fs.readFileSync(path.join(checkout, '.git/index')), index);
  };
  return { root, checkout, source, previous, git, write, build, heads, before, publish, unchanged };
}

test('publication appends history, includes hidden hosting files, and preserves main and recovery data', () => {
  const f = fixture();
  const result = f.publish('--expected-main', f.source, '--expected-gh-pages', f.previous);
  assert.equal(result.status, 0, result.stderr);
  const plan = JSON.parse(fs.readFileSync(path.join(f.checkout, 'website/.build/publication-plan.json')));
  assert.equal(plan.source, f.source);
  assert.equal(plan.previous, f.previous);
  assert.equal(f.git('rev-parse', plan.publication + '^'), f.previous);
  assert.equal(f.git('rev-parse', 'HEAD'), f.source);
  assert.ok(f.heads().includes(f.source + '\trefs/heads/main'));
  assert.ok(f.heads().includes(plan.publication + '\trefs/heads/gh-pages'));
  assert.equal(f.git('show', plan.publication + ':index.html'), '<h1>Validated website</h1>');
  f.git('cat-file', '-e', plan.publication + ':.nojekyll');
  assert.ok(!f.git('ls-tree', '-r', '--name-only', plan.publication).includes('source.txt'));
  f.unchanged();
});

test('dry-run records a publication plan without updating either remote branch', () => {
  const f = fixture();
  const result = f.publish('--dry-run', '--expected-main', f.source);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(f.heads(), f.before);
  assert.equal(JSON.parse(fs.readFileSync(path.join(f.checkout, 'website/.build/publication-plan.json'))).dryRun, true);
  f.unchanged();
});

test('a different validated revision cannot publish the current checkout', () => {
  const f = fixture();
  const result = f.publish('--expected-main', '0'.repeat(40));
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /main no longer matches the CI-validated source/);
  assert.equal(f.heads(), f.before);
  f.unchanged();
});

test('main advancing during the build prevents publishing the superseded site', () => {
  const f = fixture();
  const next = f.git('commit-tree', f.source + '^{tree}', '-p', f.source, '-m', 'Newer main');
  f.git('push', '--quiet', 'origin', next + ':refs/heads/main');
  const advanced = f.heads();
  const result = f.publish('--expected-main', f.source);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /main no longer matches the CI-validated source/);
  assert.equal(f.heads(), advanced);
  f.unchanged();
});

test('a changed publication tip cannot be overwritten after review', () => {
  const f = fixture();
  const next = f.git('commit-tree', f.previous + '^{tree}', '-p', f.previous, '-m', 'Concurrent publication');
  f.git('push', '--quiet', 'origin', next + ':refs/heads/gh-pages');
  const advanced = f.heads();
  const result = f.publish('--expected-main', f.source, '--expected-gh-pages', f.previous);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /gh-pages changed since it was reviewed/);
  assert.equal(f.heads(), advanced);
  f.unchanged();
});
