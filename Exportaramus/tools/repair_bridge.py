"""Exact original-campaign to strengthened-test bridge. No production changes or status waivers."""
from pathlib import Path
import hashlib
import json
import importlib.util
import xml.etree.ElementTree as ET
from collections import Counter
import sys
sys.dont_write_bytecode = True

BRIDGE_TOOL_FILES = {
    "tools/individual-mutation.py", "tools/mutation_review.py", "tools/mutation-campaign.py",
    "tools/Complete-QualityRecord.ps1", "tools/repair_bridge.py", "tools/test-repair-bridge.py",
}
STAGED_BRIDGE_TOOL_FILES = BRIDGE_TOOL_FILES | {
    'tools/suite_binding.py', 'tools/test-suite-binding.py',
    "tools/multi_stage_review.py", "tools/test-multi-stage-review.py",
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def artifact(value, within=None):
    path = Path(value['path']).resolve()
    if within is not None and not path.is_relative_to(within.resolve()):
        raise ValueError('Bridge artifact escapes its approved root.')
    if not path.is_file() or digest(path) != value['sha256']:
        raise ValueError('Bridge artifact is missing or changed: ' + str(path))
    return path


def inventory(path):
    value = json.loads(path.read_bytes())
    rows = value['files']
    paths = {row['path']: row['sha256'] for row in rows}
    if len(paths) != len(rows) or any(Path(p).is_absolute() or '..' in Path(p).parts for p in paths):
        raise ValueError('Invalid or duplicate inventory path.')
    canonical = '\n'.join(row['sha256'] + '  ' + row['path'] for row in rows)
    if hashlib.sha256(canonical.encode('utf-8')).hexdigest() != value['sha256']:
        raise ValueError('Inventory aggregate hash is invalid.')
    return value, paths


def live_paths(root, policy='legacy'):
    if policy == 'current':
        from qualification_inputs import current_paths
        return current_paths(root)
    if policy != 'legacy':
        raise ValueError('Unknown qualification source inventory policy.')
    names = ('Exportaramus.csproj','Exportaramus.json','Exportaramus.slnx','Directory.Build.props',
             'DalamudPackager.targets','global.json','packages.lock.json','AGENTS.md','.editorconfig')
    paths = {root / name for name in names if (root / name).is_file()}
    for directory in ('src','tests','reference','data','images','tools','project'):
        paths.update(p for p in (root / directory).rglob('*') if p.is_file()
                     and not {'bin','obj','testresults','__pycache__'}.intersection(part.lower() for part in p.relative_to(root).parts))
    paths.update(root / name for name in ('docs/Exportaramus - Backend Specification.md',
        'docs/Exportaramus - Frontend Specification.md','docs/assets/Exportaramus - Frontend Mockup.png',
        'docs/assets/Exportaramus - Main Window.png'))
    return {p.relative_to(root).as_posix(): digest(p) for p in paths}


def case_names(trx, passing=False):
    document = ET.parse(trx).getroot()
    rows = document.findall('.//{*}UnitTestResult')
    if not rows or any(not row.get('testName') for row in rows):
        raise ValueError('Complete stable test names are required for repair evidence.')
    if passing:
        summaries = document.findall('.//{*}ResultSummary')
        if len(summaries) != 1 or summaries[0] not in list(document) or summaries[0].get('outcome') != 'Completed':
            raise ValueError('Original baseline summary is incomplete.')
        counters = summaries[0].findall('{*}Counters')
        if len(counters) != 1 or len(document.findall('.//{*}Counters')) != 1:
            raise ValueError('Original baseline counters are ambiguous.')
        counts = {key:int(counters[0].attrib[key]) for key in ('total','executed','passed','failed','notExecuted','aborted','timeout','error','notRunnable','disconnected','inconclusive','passedButRunAborted','warning','completed','inProgress','pending')}
        if any(row.get('outcome') != 'Passed' for row in rows) or any(counts[key] != len(rows) for key in ('total','executed','passed')) or any(value for key,value in counts.items() if key not in ('total','executed','passed')):
            raise ValueError('Original baseline is not entirely passing and complete.')
    return sorted(row.get('testName') for row in rows)


def preserves_cases(original, repaired):
    return not (Counter(original) - Counter(repaired))


def validate_differences(original, repaired, changes, root, tool_files=None):
    actual = {p for p in original.keys() | repaired.keys() if original.get(p) != repaired.get(p)}
    named = [entry['path'] for entry in changes]
    if len(set(named)) != len(named) or set(named) != actual:
        raise ValueError('Every changed input must appear exactly once in the reviewed allowlist.')
    evidence = []
    for entry in changes:
        path = entry['path']
        test = path.startswith('tests/Exportaramus.Tests/') and path.endswith('.cs')
        tool = path in (BRIDGE_TOOL_FILES if tool_files is None else tool_files)
        if not (test or tool) or path not in repaired:
            raise ValueError('Product/reference/resources/config/projects and deletions are immutable: ' + path)
        if entry.get('beforeSha256') != original.get(path) or entry.get('afterSha256') != repaired[path]:
            raise ValueError('Allowlisted change hashes do not match inventories.')
        if not entry.get('reason', '').strip() or entry.get('kind') != ('TestStrengthening' if test else 'Tooling'):
            raise ValueError('Each exact change requires a substantive review and correct kind.')
        if test and entry.get('preservesExistingAssertions') is not True:
            raise ValueError('Test changes must explicitly preserve existing assertions.')
        if not entry.get('evidence'):
            raise ValueError('Change review evidence is required.')
        for item in entry['evidence']:
            artifact(item, root)
            evidence.append(item)
    return evidence


def repaired_baseline(bridge, root, repaired, original_cases):
    repaired_coverage_path = artifact(bridge['repairedBaselineSummary'], root)
    repaired_coverage = json.loads(repaired_coverage_path.read_bytes())
    if repaired_coverage['sourceSha256'] != repaired['sha256'] or any(repaired_coverage[name+'Valid'] <= 0 or repaired_coverage[name+'Valid'] != repaired_coverage[name+'Covered'] for name in ('lines','branches')):
        raise ValueError('An unconditional full passing repaired baseline with strict coverage is required.')
    report = artifact({'path':repaired_coverage['report'],'sha256':repaired_coverage['reportSha256']}, root)
    coverage_xml = ET.parse(report).getroot()
    if coverage_xml.tag != 'coverage' or any(int(coverage_xml.attrib[name+'-'+kind.lower()]) != repaired_coverage[name+kind] for name in ('lines','branches') for kind in ('Valid','Covered')):
        raise ValueError('Repaired coverage summary differs from its exact Cobertura report.')
    repaired_trx = artifact({'path':repaired_coverage['trx'],'sha256':repaired_coverage['trxSha256']}, root)
    trx_xml = ET.parse(repaired_trx).getroot()
    attachments = [node for node in trx_xml.findall('.//{*}UriAttachment/{*}A') if node.get('href','').endswith('coverage.cobertura.xml')]
    deployments = trx_xml.findall('{*}TestSettings/{*}Deployment')
    if len(attachments) != 1 or len(deployments) != 1 or not deployments[0].get('runDeploymentRoot'):
        raise ValueError('Exactly one coverage attachment and deployment root are required.')
    attached_report = (repaired_trx.parent / deployments[0].get('runDeploymentRoot') / 'In' / attachments[0].get('href')).resolve()
    if not attached_report.is_relative_to(repaired_trx.parent.resolve()) or attached_report != report:
        raise ValueError('Coverage attachment must resolve to this exact report within the unique TRX result directory.')
    repaired_cases = case_names(repaired_trx,passing=True)
    if not preserves_cases(original_cases,repaired_cases):
        raise ValueError('Repaired baseline removed original test cases.')
    return repaired_cases, [{'path':str(report),'sha256':digest(report)},bridge['repairedBaselineSummary'],{'path':str(repaired_trx),'sha256':digest(repaired_trx)}]


def validate_bridge(path, root):
    root = root.resolve()
    bridge = json.loads(path.read_bytes())
    if bridge.get('format') != 'exportaramus-mutation-repair-bridge' or bridge.get('schemaVersion') not in ('1.0', '2.0') or not bridge.get('reviewer','').strip():
        raise ValueError('An identified reviewer and supported repair bridge are required.')
    original = bridge['original']
    run = Path(original['runDirectory']).resolve()
    bindings = {'source-before.json':'inventorySha256','source-after.json':'afterInventorySha256','input-manifest.json':'inputManifestSha256',
        'mutation-campaign-summary.json':'summarySha256','constituent-campaigns.json':'constituentManifestSha256',
        'combined/mutation-report.json':'reportSha256','after-scope/coverage-manifest.json':'scopeSha256'}
    evidence = []
    for name, field in bindings.items():
        item = {'path':str(run / name),'sha256':original[field]}
        artifact(item, run)
        evidence.append(item)
    before, before_paths = inventory(run / 'source-before.json')
    after, after_paths = inventory(run / 'source-after.json')
    if after['sha256'] != before['sha256'] or after_paths != before_paths:
        raise ValueError('Original before/after inventories differ.')
    inputs = json.loads((run / 'input-manifest.json').read_bytes())
    summary = json.loads((run / 'mutation-campaign-summary.json').read_bytes())
    if inputs['sourceSha256'] != before['sha256'] or summary['sourceSha256'] != before['sha256'] or not summary['inputsUnchanged'] or summary['afterSourceSha256'] != before['sha256']:
        raise ValueError('Original complete campaign inputs are not unchanged and bound.')
    spec = importlib.util.spec_from_file_location('bridge_campaign', root / 'tools/mutation-campaign.py')
    campaign = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(campaign)
    constituents = campaign.validate_combined_evidence(run)
    if summary['reportSha256'] != original['reportSha256'] or summary['constituentManifestSha256'] != original['constituentManifestSha256'] or summary['exitCode'] not in (0,2):
        raise ValueError('Original summary does not bind both complete constituent reports.')
    for item in constituents['campaigns']:
        console = (run / item['console']).read_text(encoding='utf-8-sig')
        if item['exitCode'] not in (0,2) or 'The final mutation score is ' not in console or (item['exitCode'] == 2 and 'The mutation score is lower than the configured break threshold of 100%' not in console):
            raise ValueError('Every original constituent must be complete, not aborted or partial.')
        evidence.extend({'path':str(run / item[key]),'sha256':item[key+'Sha256']} for key in ('report','console'))
    scope = json.loads((run / 'after-scope/coverage-manifest.json').read_bytes())
    audit = scope['mutationInstrumentationAudit']
    if any(audit.get(k) for k in ('notRepresented','unexpectedPaths','duplicatePaths','sourceMismatches')):
        raise ValueError('Original instrumentation must reconcile the entire source scope.')
    scope_hashes = scope.get('instrumentationSourceHashes', scope['handwrittenSourceHashes'])
    if any(before_paths.get(p) != sha for p,sha in scope_hashes.items()):
        raise ValueError('Instrumented source is not bound to the original inventory.')
    quality_spec = importlib.util.spec_from_file_location('bridge_scope', root / 'tools/contract-quality-scope.py')
    quality = importlib.util.module_from_spec(quality_spec)
    quality_spec.loader.exec_module(quality)
    project = root / 'tests/Exportaramus.Testable/Exportaramus.Testable.csproj'
    compiled = quality.compile_sources(project)
    for reference in ET.parse(project).findall('.//ProjectReference'):
        compiled.update(quality.compile_sources((project.parent / reference.attrib['Include']).resolve()))
    if scope.get('mutationSourceScope') == 'complete':
        compiled.update(quality.project_sources(root / 'project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj'))
        compiled.add(root / 'examples/CollectionExample/InventoryItemReader.cs')
    if {p.relative_to(root).as_posix():digest(p) for p in compiled} != scope_hashes:
        raise ValueError('Current complete production/reference scope differs from the original instrumentation scope.')
    repaired_path = artifact(bridge['repairedInventory'], root)
    repaired, repaired_paths = inventory(repaired_path)
    inventory_policy = bridge.get('inventoryPolicy', 'legacy')
    if live_paths(root, inventory_policy) != repaired_paths:
        raise ValueError('Current repair inputs differ from the exact repaired inventory.')
    allowed_tools = STAGED_BRIDGE_TOOL_FILES if bridge['schemaVersion'] == '2.0' else BRIDGE_TOOL_FILES
    evidence.extend(validate_differences(before_paths,repaired_paths,bridge['changes'],root,allowed_tools))
    if bridge['schemaVersion'] == '2.0':
        for change in bridge['changes']:
            if change.get('reviewed') is not True or not change.get('reviewer', '').strip():
                raise ValueError('Each v2 bridge change requires an identified independent review.')
            if change['kind'] == 'TestStrengthening' and not all(change.get(key) is True for key in
                    ('preservesExistingAssertions', 'preservesFixtureSemantics', 'noRelaxedDeadlines', 'noNewSkips')):
                raise ValueError('V2 test changes must preserve assertion/fixture/deadline/skip obligations.')
    evidence.append(bridge['repairedInventory'])
    coverage_path = artifact(bridge['originalBaselineSummary'])
    coverage = json.loads(coverage_path.read_bytes())
    if coverage['sourceSha256'] != before['sha256']:
        raise ValueError('Original baseline must match the completed campaign source.')
    baseline_trx = artifact({'path':coverage['trx'],'sha256':coverage['trxSha256']})
    original_cases = case_names(baseline_trx,passing=True)
    repaired_cases, baseline_evidence = repaired_baseline(bridge, root, repaired, original_cases)
    evidence.extend(baseline_evidence)
    evidence.extend([bridge['originalBaselineSummary'],{'path':str(baseline_trx),'sha256':digest(baseline_trx)},
                     {'path':str(path.resolve()),'sha256':digest(path)}])
    return {'bridge':bridge,'bridgePath':str(path.resolve()),'bridgeSha256':digest(path),
        'run':run,'before':inputs,'originalInventory':before,'repairedInventory':repaired,'inventoryPolicy':inventory_policy,
        'repairedPaths':repaired_paths,'reportHash':original['reportSha256'],'scope':scope,
        'originalCases':original_cases,'repairedCases':repaired_cases,'evidence':evidence}
