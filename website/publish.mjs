import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { parseArgs } from 'node:util';

const website = path.dirname(fileURLToPath(import.meta.url));
const repository = path.dirname(website);
const dist = path.join(website, 'dist');
const scratch = path.join(website, '.build');
const { values } = parseArgs({ options: { 'dry-run': { type: 'boolean', default: false },
  'expected-gh-pages': { type: 'string' }, 'expected-main': { type: 'string' } } });
const git = (...args) => execFileSync('git', args, { cwd: repository, encoding: 'utf8' }).trim();
const protectedRefs = () => git('for-each-ref', '--format=%(refname) %(objectname)',
  'refs/heads/codex/recovery-20261030', 'refs/codex/backups', 'refs/codex/recovery');
if (git('branch', '--show-current') !== 'main') throw new Error('Publish from the main checkout.');
if (git('status', '--porcelain')) throw new Error('Commit the source changes before publishing.');
const source = git('rev-parse', 'refs/heads/main');
const info = JSON.parse(fs.readFileSync(path.join(dist, 'build-info.json'), 'utf8'));
if (!fs.existsSync(path.join(dist, '.coppersharp-generated')) || info.sourceRevision !== source)
  throw new Error('Build website/dist from the current main commit before publishing.');
if (info.basePath !== '/CopperSharp68k' || info.origin !== 'https://ilehtoranta.github.io')
  throw new Error('gh-pages requires the repository GitHub Pages origin and base path.');
if (fs.lstatSync(dist).isSymbolicLink() || fs.lstatSync(scratch).isSymbolicLink())
  throw new Error('Publication directories must not be symlinks.');
const heads = new Map(git('ls-remote', '--heads', 'origin', 'main', 'gh-pages').split('\n').map(line => {
  const [sha, ref] = line.split(/\s+/); return [ref, sha];
}));
const previous = heads.get('refs/heads/gh-pages');
const remoteMain = heads.get('refs/heads/main');
if (!previous || !remoteMain) throw new Error('Both remote branches must already exist.');
if (values['expected-main'] && (source !== values['expected-main'] || remoteMain !== values['expected-main']))
  throw new Error('main no longer matches the CI-validated source; wait for the current main CI run.');
if (values['expected-gh-pages'] && values['expected-gh-pages'] !== previous)
  throw new Error('gh-pages changed since it was reviewed; inspect the new publication first.');
git('merge-base', '--is-ancestor', remoteMain, source);
git('cat-file', '-e', previous + '^{commit}');
const before = protectedRefs();
const index = path.join(scratch, 'publish-' + randomUUID() + '.index');
const env = { ...process.env, GIT_DIR: git('rev-parse', '--absolute-git-dir'), GIT_WORK_TREE: dist, GIT_INDEX_FILE: index };
const stagedGit = (...args) => execFileSync('git', args, { cwd: dist, env, encoding: 'utf8' }).trim();
try {
  stagedGit('read-tree', '--empty');
  stagedGit('-c', 'core.autocrlf=false', 'add', '--all', '--force', '--', '.');
  const tree = stagedGit('write-tree');
  const publication = git('commit-tree', tree, '-p', previous,
    '-m', 'Publish current compatibility information and regenerated SDK reference',
    '-m', `Source main: ${source}\nPrevious publication: ${previous}\nBuilt and validated with the versioned website tooling.`);
  const plan = { source, previous, publication, tree, dryRun: values['dry-run'],
    summary: git('diff', '--shortstat', previous, publication), protectedRefs: before };
  fs.writeFileSync(path.join(scratch, 'publication-plan.json'), JSON.stringify(plan, null, 2) + '\n');
  console.log(`Source: ${source}\nPublication: ${publication}\nParent: ${previous}\n${plan.summary}`);
  const args = ['-c', 'push.followTags=false', '-c', 'remote.origin.mirror=false', 'push', '--atomic'];
  if (values['dry-run']) args.push('--dry-run');
  args.push('origin', 'refs/heads/main:refs/heads/main', publication + ':refs/heads/gh-pages');
  execFileSync('git', args, { cwd: repository, stdio: 'inherit' });
  if (protectedRefs() !== before) throw new Error('A protected recovery reference changed.');
  if (!values['dry-run']) {
    const after = git('ls-remote', '--heads', 'origin', 'main', 'gh-pages');
    if (!after.includes(source + '\trefs/heads/main') || !after.includes(publication + '\trefs/heads/gh-pages'))
      throw new Error('Remote publication verification failed.');
    console.log('Verified main and gh-pages; all protected recovery refs are unchanged.');
  }
} finally {
  // The sole file removed is this invocation's temporary, isolated Git index.
  if (fs.existsSync(index)) fs.unlinkSync(index);
}
