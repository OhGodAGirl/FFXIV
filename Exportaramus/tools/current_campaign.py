"""A completed same-production campaign binding for exact independent replays."""
from pathlib import Path
import json
import sys
import repair_bridge as bridge
import report_set_review as report_set
from qualification_inputs import current_paths

TOOL_PATHS = frozenset('tools/' + name for name in (
    'current_campaign.py', 'test-current-campaign.py', 'individual-mutation.py',
    'test-individual-mutation.py', 'mutation_review.py', 'multi_stage_review.py',
    'report_set_review.py', 'test-report-set-review.py', 'test-multi-stage-review.py',
    'test-mutation-orchestration.py', 'suite_binding.py', 'test-suite-binding.py',
    'repair_bridge.py', 'test-repair-bridge.py', 'mutation_rendering.py',
    'mutation-renderer.cs', 'test-mutation-rendering.py'))


def tool_transition(before, after, changes):
    actual = {p for p in set(before) | set(after) if before.get(p) != after.get(p)}
    named = [c.get('path') for c in changes]
    report_set.require(len(named) == len(set(named)) and set(named) == actual, 'Current tool transition must name every exact changed input.')
    for change in changes:
        path = change['path']
        report_set.require(path in TOOL_PATHS and change.get('reviewed') is True and bool(change.get('reviewer', '').strip()), 'Only explicitly reviewed qualification-tool changes are allowed.')
        report_set.require(path in after and change.get('beforeSha256') == before.get(path) and change.get('afterSha256') == after.get(path), 'Tool transition hashes differ or tool was deleted.')
        review = report_set.read(change['reviewEvidence'])
        report_set.require(review.get('reviewed') is True and bool(review.get('reason', '').strip()) and review.get('reviewer') == change['reviewer'] and all(review.get(k) == change.get(k) for k in ('path','beforeSha256','afterSha256')), 'Tool transition lacks exact independent review evidence.')


def validate_input_transition(before, after, receipt, root):
    if receipt['schemaVersion'] == '1.0':
        tool_transition(before, after, receipt['toolChanges'])
        report_set.require(not receipt.get('testChanges'), 'Test changes require current campaign schema2.0.')
        return
    old_tools = {p:h for p,h in before.items() if p.startswith('tools/')}
    new_tools = {p:h for p,h in after.items() if p.startswith('tools/')}
    tool_transition(old_tools, new_tools, receipt['toolChanges'])
    changes = receipt['testChanges']
    for change in changes:
        path = change.get('path', '')
        report_set.require(path.startswith(('tests/Exportaramus.Tests/', 'tests/Exportaramus.ConsumerKit.Tests/', 'tests/Exportaramus.InventoryReader.Mutation.Tests/')) and path.endswith('.cs') and change.get('kind') == 'TestStrengthening', 'Current successor permits only reviewed test-source strengthening.')
    import multi_stage_review
    multi_stage_review.validate_strengthening({p:h for p,h in before.items() if not p.startswith('tools/')}, {p:h for p,h in after.items() if not p.startswith('tools/')}, changes, root, bridge, multi_suite=True)


def validate_baseline_preservation(original, successor):
    report_set.require(set(original) == set(successor) == {'provider','consumerkit12','inventoryreader'}, 'Every old and new canonical baseline is required.')
    report_set.require(all(bridge.preserves_cases(original[suite], successor[suite]) for suite in original), 'Current successor removed historical baseline cases.')


def validate_prior_campaign(receipt, root, measured_paths, report_path):
    prior = receipt['priorCampaign']
    prior_root = Path(prior['root']).resolve()
    prior_file = report_set.bound(prior['receipt'])
    prior_inventory = report_set.bound(prior['inventory'])
    old_receipt = json.loads(prior_file.read_bytes())
    report_set.require(old_receipt.get('schemaVersion') == '1.0', 'Historical cases require a verified tool-only campaign receipt.')
    _, paths = bridge.inventory(prior_inventory)
    report_set.require(current_paths(prior_root) == paths, 'Prior campaign inputs changed.')
    old_measured, old_paths = bridge.inventory(report_set.bound(old_receipt['beforeInventory']))
    report_set.require(old_paths == measured_paths and report_set.bound(old_receipt['measurement']['report']) == Path(report_path).resolve(), 'Prior campaign belongs to another measurement.')
    for path, sha in paths.items():
        if path.startswith('tools/'):
            report_set.require(report_set.digest(prior_root / path) == sha, 'Prior validator closure changed.')
    import uuid
    code = "import sys,json;from pathlib import Path;sys.path.insert(0,sys.argv[1]);import current_campaign as c;import repair_bridge as b;r=json.loads(Path(sys.argv[2]).read_bytes());v=c.validate_current_campaign(sys.argv[2],sys.argv[3],sys.argv[4],sys.argv[5]);print(json.dumps({'cases':{k:b.canonical_trx(c.report_set.bound(a),k) for k,a in r['baselines'].items()},'evidence':v['evidence']}))"
    run, evidence = report_set.recorded_run([sys.executable, '-c', code, str(prior_root / 'tools'), str(prior_file), str(prior_root), str(prior_inventory), str(Path(report_path).resolve())], root, Path(root) / 'development/current-campaign-validation' / uuid.uuid4().hex / 'prior-campaign')
    report_set.require(run.returncode == 0, 'Prior campaign validation failed; historical cases are not accepted.')
    result = json.loads(run.stdout)
    report_set.require(set(result['cases']) == {'provider','consumerkit12','inventoryreader'}, 'Prior campaign omitted a canonical suite.')
    for artifact in result['evidence']:
        report_set.bound(artifact)
    return result['cases'], evidence + result['evidence']


def validate_tool_identity(paths, directory):
    directory = Path(directory).resolve()
    expected = {Path(p).name: sha for p, sha in paths.items() if p.startswith('tools/') and p.endswith('.py')}
    required = {'individual-mutation.py', 'current_campaign.py', 'report_set_review.py', 'repair_bridge.py', 'suite_binding.py', 'qualification_inputs.py'}
    report_set.require(required <= set(expected), 'Current validator closure is missing required tools.')
    for name, sha in expected.items():
        report_set.require(report_set.digest(directory / name) == sha, 'Current validator/executor tool differs from frozen inputs: ' + name)
    for module in tuple(sys.modules.values()):
        file = getattr(module, '__file__', None)
        if file and Path(file).name in expected:
            report_set.require(report_set.digest(Path(file)) == expected[Path(file).name], 'Loaded validator closure differs from frozen inputs.')


def validate_runner_version(item, root):
    return report_set.validate_runner_version(item, root)


def validate_report_identity(item, report_path):
    measured = report_set.bound(item)
    report_set.require(measured == Path(report_path).resolve(), 'Executor report differs from measured report.')
    return measured


def validate_selection(binding, selected):
    report_set.require(bool(selected) and {row[0] for row in selected} <= binding['sourcePaths'], 'Selected mutant lies outside the complete measured source domain.')


def validate_cases(binding, cases):
    report_set.require(cases == binding['cases'], 'Current campaign replay omitted or changed complete baseline cases.')


def validate_current_campaign(path, root, inventory_path, report_path, target='provider'):
    root = Path(root).resolve()
    path = Path(path).resolve()
    receipt = json.loads(path.read_bytes())
    report_set.require(receipt.get('format') == 'exportaramus-current-campaign' and receipt.get('schemaVersion') in ('1.0', '2.0'), 'Unsupported current campaign receipt.')
    report_set.require(target == 'provider' and receipt.get('reviewer', '').strip(), 'Current campaign replay currently requires reviewed provider scope.')
    frozen, frozen_paths = bridge.inventory(Path(inventory_path).resolve())
    report_set.require(current_paths(root) == frozen_paths, 'Current replay inputs changed.')
    validate_tool_identity(frozen_paths, Path(__file__).parent)
    campaign_root = Path(receipt['campaignRoot']).resolve()
    before_file = report_set.bound(receipt['beforeInventory'])
    after_file = report_set.bound(receipt['afterInventory'])
    before, before_paths = bridge.inventory(before_file)
    after, after_paths = bridge.inventory(after_file)
    report_set.require(before_paths == after_paths and before['sha256'] == after['sha256'], 'Completed campaign input inventories differ.')
    report_set.require(current_paths(campaign_root) == before_paths, 'Measured campaign inputs changed after completion.')
    validate_input_transition(before_paths, frozen_paths, receipt, root)
    source = receipt['source']
    item = receipt['measurement']
    report_file = validate_report_identity(item['report'], report_path)
    canonical = json.loads((campaign_root / 'tests/stryker-config.json').read_bytes())
    selected = report_set.read(item['executionConfig'])
    report_set.replacement_config(canonical, selected, source)
    version = report_set.canonical_mutation_version((campaign_root / 'tools/mutation-campaign.py').read_text(encoding='utf-8'))
    report_set.require(item['canonicalToolVersion'].split('+', 1)[0] == version, 'Runner version differs from canonical policy.')
    report_set.validate_execution(report_set.read(item['execution']), item, before['sha256'], campaign_root)
    runner_evidence = validate_runner_version(item, root)
    report = report_set.read(item['report'])
    normalized = report_set.scoped_report(report, source, campaign_root, before_paths)
    report_set.replacement_inventory(normalized, source, (root / source).read_bytes())
    coverage_root = Path(receipt['coverageRoot']).resolve()
    coverage_inventory, coverage_paths = bridge.inventory(report_set.bound(receipt['coverageInventory']))
    coverage_expected = before_paths if receipt['schemaVersion'] == '1.0' else frozen_paths
    coverage_source = before['sha256'] if receipt['schemaVersion'] == '1.0' else frozen['sha256']
    report_set.require(coverage_paths == coverage_expected and current_paths(coverage_root) == coverage_expected, 'Coverage inputs differ from exact current test inputs.')
    summary_path = report_set.bound(receipt['coverageSummary'])
    summary = json.loads(summary_path.read_bytes())
    report_set.require(summary['sourceSha256'] == coverage_source, 'Coverage summary source differs.')
    instrumented_cases = bridge.case_names(report_set.bound({'path': summary['trx'], 'sha256': summary['trxSha256']}), passing=True)
    _, coverage_evidence = bridge.repaired_baseline({'repairedBaselineSummary': receipt['coverageSummary']}, coverage_root, coverage_inventory, instrumented_cases)
    coverage_evidence.extend(bridge.component_coverage(summary, coverage_root, coverage_paths))
    report_set.require(set(receipt['baselines']) == {'provider', 'consumerkit12', 'inventoryreader'}, 'All canonical full-suite baselines are required.')
    cases = {suite: bridge.canonical_trx(report_set.bound(value), suite) for suite, value in receipt['baselines'].items()}
    prior_evidence = []
    original_cases = cases
    if receipt['schemaVersion'] == '2.0':
        original_cases, prior_evidence = validate_prior_campaign(receipt, root, before_paths, report_path)
        validate_baseline_preservation(original_cases, cases)
    performance = summary['performance']
    performance_cases = bridge.canonical_trx(report_set.bound({'path': performance['trx'], 'sha256': performance['trxSha256']}), 'provider')
    report_set.case_coverage(cases['provider'], instrumented_cases, performance_cases)
    report_set.require(bridge.canonical_trx(report_set.bound({'path': summary['consumerClient']['trx'], 'sha256': summary['consumerClient']['trxSha256']}), 'consumerkit12') == cases['consumerkit12'], 'Consumer coverage and complete baseline cases differ.')
    evidence = list(report_set.artifacts(receipt)) + prior_evidence + coverage_evidence + runner_evidence + [{'path': str(path), 'sha256': report_set.digest(path)}, {'path': str(Path(inventory_path).resolve()), 'sha256': report_set.digest(inventory_path)}]
    for artifact in evidence:
        report_set.bound(artifact)
    return {'receiptPath': str(path), 'receiptSha256': report_set.digest(path), 'reportPath': str(report_file), 'reportHash': report_set.digest(report_file), 'sourcePaths': {source}, 'cases': cases[target], 'evidence': evidence, 'inventorySha256': frozen['sha256'], 'inventory': frozen, 'inventoryPaths': frozen_paths, 'inventoryArtifact': {'path': str(Path(inventory_path).resolve()), 'sha256': report_set.digest(inventory_path)}, 'campaignInventory': before, 'campaignInventoryArtifact': receipt['beforeInventory'], 'campaignCases': original_cases[target], 'campaignSourceSha256': before_paths[source], 'campaignInventorySha256': before['sha256']}
