# CopperSharp website

The site and its build tools are versioned in `main`. `gh-pages` remains the
independent publication branch; building or testing this directory never checks
out, commits to, or pushes that branch.

## Recovery provenance

The original Vinext/React authoring checkout and SDK reference generator could
not be located in this repository's history, retained local Git objects, recovery
archive inventories, local project folders, or available session records. The
old `.gitignore` described `website/` as an independent Git repository, but that
checkout is absent on this machine. The publication manifest identifies Vinext
0.0.50, but contains no original TypeScript/TSX or source maps.

This directory therefore contains **recovered static publication files and a
reconstructed build and reference generator**, rather than the missing original
authoring project. If that project is recovered later, retain it and reconcile
it with these sources rather than overwriting either copy.

- `src/pages/`: editable HTML recovered from the published homepage,
  getting-started guide, compatibility matrix, and 404 page.
- `public/`: the published CSS, JavaScript, fonts, graphics, search script,
  verification file, and hosting configuration. Compiled framework bundles are
  retained as publication dependencies; they are not original React source.
- `src/reference-shell.html`: an editable template extracted from the published
  reference, with explicit content, metadata, and deployment-path placeholders.
- `src/published-routes.json`: module/type routing metadata recovered from the
  published search index. The generator retains unambiguous existing type URLs,
  including types that subsequently moved into library classes. Newly qualified
  or ambiguous nested types receive distinct routes.
- `ReferenceGenerator/`: the reconstructed C# generator. It reads **the current
  built SDK assembly and XML documentation**, not the old published API index.
- `recovery.json`: the original publication commit
  `0f0ed8d30ca8b454fdc5ec3615e122f5be62d8cf`, SHA-256 checksums of recovered files,
  and the distinction between copied and reconstructed material.
- `recovery/publication-manifest.json`: the original build manifest, retained
  for provenance rather than served to visitors.
- `recovery/originals/`: unchanged copies of recovered editorial files that
  have since been updated. Their original checksums remain in `recovery.json`;
  the `editedFiles` list connects each current source file to its original copy.

The recovery copied from Git objects; it did not modify `gh-pages`, the recovery
branch, or any recovery backups. The old generated SDK HTML remains on
`gh-pages`; it is regenerated here rather than duplicated in `main`.

## Build and preview

Requirements: Git, Node.js 22 or newer, and the .NET SDK pinned by the repository's
`global.json`. No npm packages, global website framework, or external font
downloads are needed. Roslyn comes from the pinned .NET SDK. The generator builds
`Sdk.Amiga` and its compiler dependency in Release configuration.

From the repository root:

```powershell
node website/build.mjs
node --test website/test/build.test.mjs
node website/serve.mjs
```

Or run `npm run build`, `npm test`, and `npm run serve` from this directory.
Preview at <http://127.0.0.1:4173/CopperSharp68k/>. `--port 4174` selects a
different preview port. If the pinned SDK is installed outside `PATH`, set
`DOTNET` to its executable path before building/testing.

Builds go to the ignored `website/dist/` directory. Intermediate builds use the
ignored `website/.build/` directory. A failed build preserves the previous
completed site. The build refuses to replace an existing `dist/` without its
generated-output marker or one that is a symlink.

The default base path is `/CopperSharp68k` and the default canonical origin is
`https://ilehtoranta.github.io`. Root and other project deployments are supported:

```powershell
node website/build.mjs --base / --origin https://coppersharp.dev
node website/build.mjs --base /docs/sdk --origin https://example.com
```

The build adjusts local asset/navigation URLs, canonical URLs, search prefixes,
robots.txt and the sitemap. GitHub repository/source links retain their URLs.

## Reference generation

Public types and declared members come from the compiled `CopperSharp.Sdk.Amiga`
assembly through Roslyn symbols. Source declarations locate their SDK module and
source-file/line links. XML documentation supplies summaries, remarks, parameter
descriptions and returns. The generated reference includes signatures, overload
anchors, constants, nested types, fixed buffers, library names, LVO offsets, and
argument/return register assignments. Example source files and compiler-generated
implementation types are excluded, preserving the packaged-example exclusion
from the last publication.

An unresolved exported source type or conflicting type route fails the build.
All output is deterministic for the same source revision and build options.
The search index, module/type pages, coverage counts and sitemap are regenerated
together. `build-info.json` records the source revision used for links.
Legacy published type URLs remain available; ambiguous shortened type names
redirect into current reference search instead of displaying outdated API data.

`test/build.test.mjs` validates recovery checksums, current SDK declarations and
ABI details, every local navigation/asset/search target and anchor, sitemap
targets, current manifest counts, published type URLs, repeatable output, and root/custom project paths. CI performs these
checks and retains a preview artifact with read-only repository permissions.

## Content and publishing

The compatibility matrix was reviewed against `main` on 31 October 2026. It
describes current software/native floating point, 64-bit arithmetic and ABI,
managed runtime, Amiga PAL, exact framework bindings, and separately audited
CoreLib StringBuilder admission. It distinguishes the current source build from
published preview packages. Editorial capability claims should be reviewed
against code and execution contracts when those capabilities change.

`content.mjs` reads the exact profile manifest at build time for binding/status,
type-pattern and feature-group counts. The homepage's SDK module count follows
the regenerated reference. `compatibility-profile.json` records the source
revision, last editorial review date and machine-readable profile summary.

There is no automatic deployment. To publish an explicitly requested website
update, commit the sources to `main`, build and validate the default GitHub Pages
site, then run:

```powershell
node website/publish.mjs --dry-run --expected-gh-pages <reviewed-remote-tip>
node website/publish.mjs --expected-gh-pages <reviewed-remote-tip>
```

The publisher requires a clean `main` checkout and a build from that exact
commit. It stages only `dist/` through a temporary isolated index, creates a
publication commit whose parent is the existing remote `gh-pages` tip, and
atomically pushes `main` and `gh-pages` as fast-forwards. It does not create or
switch worktrees, force-push, push tags, or modify recovery branches/backups.
The dry run records a reviewable publication tree and summary in
`website/.build/publication-plan.json`. Keep `main`'s authoring files separate
from the `gh-pages` publication history.
