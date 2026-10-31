import fs from 'node:fs';
import path from 'node:path';

export function summarizeContract(manifest) {
  const statuses = Object.groupBy(manifest.bindings, binding => binding.status);
  return {
    targetFramework: manifest.targetFramework,
    referencePackVersion: manifest.referencePackVersion,
    bindings: manifest.bindings.length,
    implemented: statuses.Implemented?.length ?? 0,
    intrinsics: statuses.Intrinsic?.length ?? 0,
    platform: statuses.Platform?.length ?? 0,
    typePatterns: new Set(manifest.bindings.map(binding => binding.type)).size,
    featureGroups: new Set(manifest.bindings.flatMap(binding => binding.features)).size,
  };
}

export function renderContent(text, contract, revision, sdkModules) {
  const replacements = {
    FRAMEWORK_TARGET: contract.targetFramework,
    REFERENCE_PACK_VERSION: contract.referencePackVersion,
    BOUND_MEMBERS: contract.bindings,
    IMPLEMENTED: contract.implemented,
    INTRINSICS: contract.intrinsics,
    PLATFORM: contract.platform,
    TRACKED_TYPES: contract.typePatterns,
    FEATURE_GROUPS: contract.featureGroups,
    SOURCE_REVISION: revision,
    SOURCE_SHORT: revision.slice(0, 7),
    SDK_MODULES: sdkModules,
  };
  return text.replace(/\{\{([A-Z_]+)\}\}/g, (_, name) => {
    if (!(name in replacements)) throw new Error(`Unknown website content token ${name}`);
    return String(replacements[name]).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;');
  });
}

export function renderEditorialPages(website, stage, revision) {
  const contract = summarizeContract(JSON.parse(fs.readFileSync(path.join(website, '../Compiler/Framework/net10.0-10.0.9.json'), 'utf8')));
  const entries = JSON.parse(fs.readFileSync(path.join(stage, 'reference-index.json'), 'utf8'));
  const sdkModules = entries.filter(entry => entry.kind === 'module').length;
  const renderDirectory = (source, destination) => {
    for (const entry of fs.readdirSync(source, { withFileTypes: true })) {
      if (entry.isDirectory()) renderDirectory(path.join(source, entry.name), path.join(destination, entry.name));
      else if (entry.name.endsWith('.html')) {
        const file = path.join(destination, entry.name);
        fs.writeFileSync(file, renderContent(fs.readFileSync(file, 'utf8'), contract, revision, sdkModules));
      }
    }
  };
  renderDirectory(path.join(website, 'src/pages'), stage);
  const llms = path.join(stage, 'llms.txt');
  fs.writeFileSync(llms, renderContent(fs.readFileSync(llms, 'utf8'), contract, revision, sdkModules));
  fs.writeFileSync(path.join(stage, 'compatibility-profile.json'), JSON.stringify({
    sourceRevision: revision, reviewedOn: '2026-10-31', scope: 'main-source',
    manifest: 'Compiler/Framework/net10.0-10.0.9.json', ...contract,
  }, null, 2) + '\n');
}
