"""Reconcile one immutable mutation inventory against explicitly reviewed execution stages."""
from pathlib import Path
from collections import Counter
import argparse
import hashlib
import importlib.util
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
TOOL_ROOT = Path(__file__).resolve().parent
FIELDS = ('id', 'status', 'statusReason', 'mutatorName', 'location', 'replacement')
EXECUTED = ('IndependentlyKilled', 'IndividuallyNonViable')
CAUSAL = {'CausalAssertion', 'CausalContractFailure', 'CausalOperationDeadline'}
PASSIVE = {'Unrelated', 'NotUsedForAdmission'}
TOOL_FILES = {
    'tools/repair_bridge.py', 'tools/mutation_review.py', 'tools/mutation-campaign.py',
    'tools/individual-mutation.py', 'tools/Complete-QualityRecord.ps1',
    'tools/multi_stage_review.py', 'tools/test-multi-stage-review.py',
}


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def text_digest(value):
    return hashlib.sha256(value.encode('utf-8')).hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def bound(item, root=None, approved=None):
    path = Path(item['path']).resolve()
    require(root is None or path.is_relative_to(Path(root).resolve()), 'Evidence escapes its declared root.')
    require(path.is_file() and digest(path) == item['sha256'], 'Evidence is missing or changed: ' + str(path))
    if approved is not None:
        require(approved.get(str(path)) == item['sha256'], 'External evidence was not explicitly approved.')
    return path


def relative_artifact(root, item, inventory=None):
    relative = Path(item['path'])
    require(not relative.is_absolute() and '..' not in relative.parts, 'A stage-relative evidence path is required.')
    path = bound({'path': str(root / relative), 'sha256': item['sha256']}, root)
    if inventory is not None:
        require(inventory.get(relative.as_posix()) == item['sha256'], 'Evidence differs from its stage input inventory.')
    return path


def exact_mutation(entry, mutant):
    require(all(entry.get(key) == mutant.get(key) for key in FIELDS), 'Mutation ID/status/span/replacement is not exact.')


def verify_command(execution, verb, snapshot, dotnet):
    project = snapshot / 'tests/Exportaramus.Tests/Exportaramus.Tests.csproj'
    command = execution['command']
    require(len(command) >= 3 and Path(command[0]).resolve() == dotnet and command[1] == verb
            and Path(command[2]).resolve() == project and Path(execution['workingDirectory']).resolve() == snapshot,
            'Execution command does not use its exact toolchain/project/snapshot.')
    if verb == 'restore':
        require(command[3:] == ['--locked-mode', '--nologo'], 'Restore command differs from the frozen policy.')
    elif verb == 'build':
        require(command[3:] == ['-c', 'Release', '--no-restore', '--nologo', '-v:minimal'], 'Build command differs from the frozen policy.')
    else:
        require(len(command) == 14 and command[3:8] == ['-c', 'Release', '--no-restore', '--nologo', '--settings']
                and Path(command[8]).resolve() == snapshot / 'tests/coverage.runsettings'
                and command[9:11] == ['--collect:XPlat Code Coverage', '--results-directory']
                and Path(command[11]).resolve() == Path(execution['trx']).resolve().parent
                and command[12:] == ['--logger', 'trx;LogFileName=tests.trx'],
                'Only the exact full configured test command and runsettings are allowed.')


def failure_rows(trx):
    rows = []
    for row in ET.parse(trx).findall('.//{*}UnitTestResult'):
        if row.get('outcome') == 'Failed':
            message = row.findtext('./{*}Output/{*}ErrorInfo/{*}Message')
            require(row.get('testName') and message is not None, 'Failed row lacks its exact name/message.')
            rows.append((row.get('testName'), text_digest(message), message))
    return rows


def validate_causality(review, entry, result, result_path, stage_id, root, paths):
    require(review.get('format') == 'exportaramus-mutation-causal-review' and review.get('schemaVersion') == '1.0'
            and review.get('reviewed') is True and review.get('reviewer', '').strip(), 'An identified causal review is required.')
    require(review.get('stageId') == stage_id and review.get('file') == entry['file']
            and review.get('sourceSha256') == entry['sourceSha256'], 'Causal review belongs to different stage/source.')
    exact_mutation(review['mutant'], result['mutant'])
    require(bound(review['result'], root) == result_path and review['result']['sha256'] == digest(result_path),
            'Causal review does not bind the actual result.')
    trx = bound(review['trx'], root)
    require(trx == Path(result['tests']['trx']).resolve() and review['trx']['sha256'] == result['tests']['trxSha256'],
            'Causal review does not bind the actual TRX.')
    actual = failure_rows(trx)
    failures = review.get('failures', [])
    require(Counter((x['testName'], x['messageSha256']) for x in failures) == Counter((n, h) for n, h, _ in actual),
            'Every actual failed row must be retained exactly once, with its exact message hash.')
    messages = {(name, sha): message for name, sha, message in actual}
    causal_count = 0
    for item in failures:
        classification = item.get('classification')
        require(classification in CAUSAL | PASSIVE and item.get('reason', '').strip(), 'Failure classification/reason is missing.')
        if classification in PASSIVE:
            continue
        causal_count += 1
        path = item['testSource']['path']
        require(path.startswith('tests/Exportaramus.Tests/') and path.endswith('.cs'), 'Causal test source must be a stage-owned test.')
        relative_artifact(root, item['testSource'], paths)
        if classification == 'CausalAssertion':
            require(re.search(r'\bAssert\.', messages[(item['testName'], item['messageSha256'])]),
                    'CausalAssertion must name an actual assertion failure; use contract-failure review for exceptions.')
        else:
            require(item.get('evidence'), 'Contract/deadline causality requires exact caller/fixture evidence.')
        for evidence in item.get('evidence', []):
            relative_artifact(root, evidence, paths)
    require(causal_count > 0, 'Unrelated or unreviewed failures alone cannot establish a kill.')
    return [{'testName': name, 'messageSha256': sha, 'classification': item['classification']}
            for item in failures for name, sha in [(item['testName'], item['messageSha256'])]]


def stage_shape(manifest, final_root):
    require(manifest.get('format') == 'exportaramus-mutation-stage-composition' and manifest.get('schemaVersion') == '1.0'
            and manifest.get('reviewed') is True and manifest.get('reviewer', '').strip(), 'Reviewed stage-composition schema required.')
    stages = manifest['stages']
    require(stages and len({s['id'] for s in stages}) == len(stages), 'Stage IDs must be unique and nonempty.')
    roots = [Path(s['root']).resolve() for s in stages]
    require(len(set(roots)) == len(roots), 'Stage roots must be unique.')
    for i, stage in enumerate(stages):
        require(re.fullmatch(r'[A-Za-z0-9_-]+', stage['id']), 'Invalid stage ID.')
        require(stage.get('parent') == (None if i == 0 else stages[i - 1]['id']), 'Stages must form one ordered acyclic predecessor chain.')
        require(roots[i].parent.parent == final_root.parent.parent and roots[i].name == final_root.name,
                'Stage root is outside the bounded sibling-stage workspace.')
        require(not any(roots[i].is_relative_to(other) or other.is_relative_to(roots[i]) for other in roots[:i]),
                'Nested/ambiguous stage roots are not allowed.')
    require(manifest['finalStage'] == stages[-1]['id'] and roots[-1] == final_root, 'Final stage does not identify this exact root.')
    return stages


def validate_strengthening(original, successor, changes, root, bridge_module):
    bridge_module.validate_differences(original, successor, changes, root, bridge_module.STAGED_BRIDGE_TOOL_FILES)
    for change in changes:
        require(change.get('reviewed') is True and change.get('reviewer', '').strip(), 'Every predecessor diff needs independent review.')
        if change['kind'] == 'TestStrengthening':
            require(all(change.get(key) is True for key in ('preservesExistingAssertions', 'preservesFixtureSemantics',
                    'noRelaxedDeadlines', 'noNewSkips')), 'Test obligation, fixture, deadline and skip preservation must be reviewed explicitly.')


def local_module(name, tools):
    spec = importlib.util.spec_from_file_location(name, tools / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def verify_stage_pins(stage, approved):
    root = Path(stage['root']).resolve()
    bindings = stage['validators']
    require(len({item['path'] for item in bindings}) == len(bindings), 'Duplicate stage validator pins.')
    actual = {p.relative_to(root).as_posix(): digest(p) for p in (root / 'tools').glob('*.py')}
    require({item['path']: item['sha256'] for item in bindings} == actual, 'Every stage Python validator must be pinned exactly.')
    for item in bindings:
        relative_artifact(root, item)
        bound({'path': str(root / item['path']), 'sha256': item['sha256']}, approved=approved)
    return actual


def verify_summary(entry, repair, review_module, root, checked_snapshots=None, summary_cache=None):
    proof = entry['independentExecution']
    path = (root / proof['summary']).resolve()
    require(path.name == 'independent-mutation-summary.json', 'Execution must identify the final canonical shard summary.')
    cache_key = (str(path), proof['sha256'])
    cached = summary_cache is not None and cache_key in summary_cache
    if cached:
        summary = summary_cache[cache_key]
    else:
        path = review_module.verified_artifact(root, proof['summary'], proof['sha256'])
        summary = json.loads(path.read_bytes())
    require(summary.get('format') == 'exportaramus-independent-mutation-shard' and summary.get('schemaVersion') == '1.1',
            'Only explicit stage-bound repaired execution summaries are supported.')
    require(summary['sourceSha256'] == repair['repairedInventory']['sha256']
            and summary['inventorySha256'] == repair['bridge']['repairedInventory']['sha256']
            and summary['reportSha256'] == repair['reportHash']
            and summary['originalSourceSha256'] == repair['originalInventory']['sha256']
            and summary['executorSha256'] == repair['repairedPaths']['tools/individual-mutation.py']
            and summary['repairBridgeSha256'] == repair['bridgeSha256'], 'Execution was relabeled or belongs to another stage/campaign.')
    require(summary.get('restoredInputsUnchanged') is True and summary.get('changedInputs') == [], 'Execution inputs were not restored exactly.')
    for field in (() if cached else ('inventory', 'executor', 'repairBridge', 'report')):
        if field == 'report':
            require(Path(summary[field]).resolve() == repair['run'] / 'combined/mutation-report.json', 'Execution references a different original report.')
            require(digest(summary[field]) == summary[field + 'Sha256'], 'Original report changed.')
        else:
            review_module.verified_artifact(root, summary[field], summary[field + 'Sha256'])
    snapshot = Path(summary['snapshot']).resolve()
    require(snapshot.is_relative_to(root / 'development') and snapshot.parent == path.parent,
            'Execution snapshot does not belong to its summary directory.')
    if checked_snapshots is None or snapshot not in checked_snapshots:
        for relative, sha in repair['repairedPaths'].items():
            require(digest(snapshot / relative) == sha, 'Copied execution inputs are no longer restored: ' + relative)
        if checked_snapshots is not None:
            checked_snapshots.add(snapshot)
    results = summary['results']
    require(len({(r['file'], r['mutant']['id']) for r in results}) == len(results), 'Duplicate exact execution results.')
    matches = [r for r in results if r['file'] == entry['file'] and r['mutant']['id'] == entry['id']]
    require(len(matches) == 1, 'The requested mutant must occur exactly once in the summary.')
    result = matches[0]
    require(result['sourceSha256'] == entry['sourceSha256'], 'Executed mutant source differs.')
    exact_mutation(entry, result['mutant'])
    result_path = path.parent / ('mutant-' + entry['id']) / 'result.json'
    require(json.loads(result_path.read_bytes()) == result, 'Standalone result and final shard summary disagree.')
    source = review_module.verified_artifact(root, entry['file'], entry['sourceSha256']).read_bytes().decode('utf-8')
    executor = local_module('individual-mutation', root / 'tools')
    mutated, span = executor.replace_span(source, entry['location'], entry['replacement'])
    require(result.get('originalSpan') == span, 'Executed original span differs.')
    mutant_path = review_module.verified_artifact(root, result['mutatedSource'], result['mutatedSourceSha256'])
    require(mutant_path.parent == result_path.parent and mutant_path.read_bytes() == mutated.encode('utf-8'), 'Mutated bytes are not the exact replacement.')
    dotnet = bound(summary['toolchain']['dotnet'])
    if not cached:
        baseline = summary['baseline']
        require(baseline['restore']['exitCode'] == 0 and not baseline['restore']['timedOut']
                and baseline['build']['exitCode'] == 0 and not baseline['build']['timedOut'], 'Stage execution baseline did not restore/build normally.')
        for execution in (baseline['restore'], baseline['build']):
            review_module.verified_artifact(root, execution['log'], execution['logSha256'])
        counts = review_module.execution_counts(root, baseline['tests'])
        from repair_bridge import case_names
        require(baseline['tests']['exitCode'] == 0 and counts['failed'] == 0 and counts['passed'] == counts['executed']
                and case_names(Path(baseline['tests']['trx']), passing=True) == repair['repairedCases'], 'Execution baseline does not contain the complete stage test multiset.')
        bound(summary['toolchain']['csc'])
        for execution, verb in ((baseline['restore'], 'restore'), (baseline['build'], 'build'), (baseline['tests'], 'test')):
            verify_command(execution, verb, snapshot, dotnet)
        if summary_cache is not None:
            summary_cache[cache_key] = summary
    review_module.verified_artifact(root, result['build']['log'], result['build']['logSha256'])
    verify_command(result['build'], 'build', snapshot, dotnet)
    if result.get('tests') is not None:
        verify_command(result['tests'], 'test', snapshot, dotnet)
    return summary, result, result_path


def validate_kill(result, repair, review_module, root):
    from repair_bridge import case_names
    require(result.get('outcome') == 'Killed' and result['build']['exitCode'] == 0 and not result['build']['timedOut']
            and result.get('tests') is not None and result['tests']['exitCode'] == 1,
            'A kill requires normal successful compilation and a normal failing test process.')
    counts = review_module.execution_counts(root, result['tests'])
    require(counts['failed'] > 0 and counts['passed'] + counts['failed'] == counts['executed']
            and case_names(Path(result['tests']['trx'])) == repair['repairedCases'],
            'A kill requires the complete stage test-case multiset with actual failures.')


def validate_nonviability(result, root):
    build = result['build']
    require(result.get('outcome') == 'Error' and result.get('errorKind') == 'CompilerRejected'
            and build['exitCode'] != 0 and not build['timedOut'] and result.get('tests') is None,
            'Nonviability needs an ordinary compiler rejection, not test failure/timeout.')
    log = bound({'path': build['log'], 'sha256': build['logSha256']}, root).read_text(encoding='utf-8')
    diagnostics = sorted(set(re.findall(r'\berror (CS\d+):', log)))
    require(diagnostics and diagnostics == build['compilerDiagnosticCodes'], 'Exact ordinary C# compiler diagnostics are required.')
    target = str((Path(build['workingDirectory']) / result['file']).resolve()).casefold()
    require(any(line.casefold().startswith(target + '(') and re.search(r'\(\d+,\d+\): error CS\d+:', line)
                for line in log.splitlines()), 'Compiler rejection needs a source diagnostic in the exact mutated file, not only infrastructure errors.')
    return diagnostics


def validate_environment(review, summary, summary_path, stage, approved):
    require(review.get('format') == 'exportaramus-execution-environment-review' and review.get('schemaVersion') == '1.0'
            and review.get('reviewed') is True and review.get('reviewer', '').strip(), 'Explicit execution-environment review required.')
    require(bound(review['summary'], Path(stage['root'])) == summary_path, 'Environment review binds another shard.')
    require(review.get('sourceSha256') == summary['sourceSha256'] and review.get('stageId') == stage['id'], 'Environment belongs to another stage.')
    require(review.get('buildTimeoutSeconds') == 120 and review.get('testTimeoutSeconds') == 120,
            'Configured execution deadlines may not be relaxed.')
    require(review.get('evidence') and review.get('reason', '').strip(), 'Environment provenance requires reviewed evidence.')
    for item in review['evidence']:
        bound(item, approved=approved)
    mode = review.get('mode')
    if mode == 'DefaultInheritedEnvironment':
        require(review.get('actualChildRuntimeProcessorCount') is None and review.get('environment', {}).get('DOTNET_PROCESSOR_COUNT') is None,
                'Historical default child count must remain unobserved; CPU overrides require a probe.')
        return
    require(mode == 'ProcessorCountOverride', 'Unsupported execution-environment mode.')
    receipt = json.loads(bound(review['launcherReceipt'], approved=approved).read_bytes())
    for key in ('launcher', 'executor', 'probe', 'probeSource', 'probeRuntimeConfig', 'dotnet'):
        bound(receipt[key], approved=approved)
    require(receipt['executor']['sha256'] == summary['executorSha256'] and receipt['status'] == 'CompletedSupplementalEvidence'
            and int(receipt['environment']['DOTNET_PROCESSOR_COUNT']) == receipt['actualChildRuntimeProcessorCount'] > 0
            and receipt['actualChildRuntimeProcessorCount'] == review['actualChildRuntimeProcessorCount']
            and receipt['environment'] == review['environment'], 'CPU environment/probe/launcher binding is incomplete.')
    require(bound(receipt['summary'], Path(stage['root'])) == summary_path, 'CPU launcher does not bind this final summary.')
    for key in ('selection', 'ids', 'log'):
        bound(receipt[key], approved=approved)
    command = receipt['command']
    def option(name):
        require(command.count(name) == 1, 'Launcher option is missing or ambiguous.')
        return command[command.index(name) + 1]
    require(Path(option('--root')).resolve() == Path(stage['root']).resolve()
            and Path(option('--output')).resolve() == summary_path.parent
            and Path(option('--inventory')).resolve() == Path(summary['inventory']).resolve()
            and Path(option('--repair-bridge')).resolve() == Path(summary['repairBridge']).resolve()
            and int(option('--expected-test-count')) == summary['expectedTestCount']
            and set(option('--ids').split(',')) == set(summary['selectedIds']), 'Launcher command differs from executed shard.')
    require(receipt['buildTimeoutSeconds'] == review['buildTimeoutSeconds'] and receipt['testTimeoutSeconds'] == review['testTimeoutSeconds'],
            'Launcher deadlines differ from the reviewed policy.')


def stage_child(request):
    stage = request['stage']
    root = Path(stage['root']).resolve()
    approved = request['approvedArtifacts']
    verify_stage_pins(stage, approved)
    sys.path.insert(0, str(root / 'tools'))
    bridge_module = local_module('repair_bridge', root / 'tools')
    sys.modules['repair_bridge'] = bridge_module
    review_module = local_module('mutation_review', root / 'tools')
    sys.modules['mutation_review'] = review_module
    repair = bridge_module.validate_bridge(bound(stage['bridge'], root), root)
    require({p: sha for p, sha in repair['repairedPaths'].items() if p.startswith('tools/') and p.endswith('.py')}
            == {x['path']: x['sha256'] for x in stage['validators']}, 'Stage validators differ from its frozen inventory.')
    report = json.loads((repair['run'] / 'combined/mutation-report.json').read_bytes())
    findings = []
    checked_snapshots = set()
    summary_cache = {}
    execution_evidence = []
    for entry in request.get('entries', []):
        require(entry['executionStage'] == stage['id'], 'Execution points to another stage.')
        mutant = next((m for m in report['files'][entry['file']]['mutants'] if m['id'] == entry['id']), None)
        require(mutant is not None, 'Exact original mutation is absent.')
        exact_mutation(entry, mutant)
        require(mutant['status'] in ('Survived', 'Timeout', 'CompileError')
                or mutant['status'] == 'Ignored' and mutant.get('statusReason') == review_module.BLOCK_REASON,
                'This raw status is not eligible for supplemental execution.')
        summary, result, result_path = verify_summary(entry, repair, review_module, root, checked_snapshots, summary_cache)
        summary_path = Path(entry['independentExecution']['summary']).resolve()
        final_root = Path(request['finalRoot']).resolve()
        env = json.loads(bound(entry['environmentReview'], final_root).read_bytes())
        validate_environment(env, summary, summary_path, stage, approved)
        for tool in summary['toolchain'].values():
            if isinstance(tool, dict):
                bound(tool, approved=approved)
                execution_evidence.append(tool)
        execution_evidence.extend([{'path': str(result_path), 'sha256': digest(result_path)},
            {'path': result['mutatedSource'], 'sha256': result['mutatedSourceSha256']},
            {'path': str(summary_path), 'sha256': digest(summary_path)}])
        for operation in [*summary['baseline'].values(), result['build'], result.get('tests')]:
            if operation:
                for key in ('log', 'trx'):
                    if key in operation:
                        execution_evidence.append({'path': operation[key], 'sha256': operation[key + 'Sha256']})
        if entry['classification'] == 'IndependentlyKilled':
            validate_kill(result, repair, review_module, root)
            causal = json.loads(bound(entry['causalReview'], final_root).read_bytes())
            failures = validate_causality(causal, entry, result, result_path, stage['id'], root, repair['repairedPaths'])
            findings.append({'file': entry['file'], 'id': entry['id'], 'classification': entry['classification'], 'failedRows': failures})
        else:
            require(entry['classification'] == 'IndividuallyNonViable' and mutant['status'] in ('CompileError', 'Ignored', 'Survived', 'Timeout'),
                    'Unsupported actual compiler-rejection disposition.')
            diagnostics = validate_nonviability(result, root)
            findings.append({'file': entry['file'], 'id': entry['id'], 'classification': entry['classification'], 'compilerDiagnosticCodes': diagnostics})
    verify_stage_pins(stage, approved)
    for (path, sha), summary in summary_cache.items():
        bound({'path': path, 'sha256': sha}, root)
        for key in ('inventory', 'executor', 'repairBridge'):
            bound({'path': summary[key], 'sha256': summary[key + 'Sha256']}, root)
    require(bridge_module.live_paths(root, repair.get('inventoryPolicy', 'legacy')) == repair['repairedPaths'], 'Stage inputs changed during verification.')
    for snapshot in checked_snapshots:
        for relative, sha in repair['repairedPaths'].items():
            execution_evidence.append({'path': str(snapshot / relative), 'sha256': sha})
    return {'id': stage['id'], 'root': str(root), 'run': str(repair['run']), 'before': repair['before'],
            'originalInventory': repair['originalInventory'], 'repairedInventory': repair['repairedInventory'],
            'repairedPaths': repair['repairedPaths'], 'inventoryPolicy': repair.get('inventoryPolicy', 'legacy'), 'reportHash': repair['reportHash'], 'scope': repair['scope'],
            'originalCases': repair['originalCases'], 'repairedCases': repair['repairedCases'],
            'evidence': repair['evidence'] + execution_evidence, 'findings': findings}


def approved_tools(manifest, final_root):
    review = json.loads(bound(manifest['toolReview'], final_root).read_bytes())
    require(review.get('reviewed') is True and review.get('reviewer', '').strip() and review.get('reason', '').strip(),
            'Independent exact tool review is required before execution.')
    bindings = review['approvedArtifacts']
    approved = {str(Path(x['path']).resolve()): x['sha256'] for x in bindings}
    require(len(approved) == len(bindings), 'Duplicate approved artifact paths.')
    for item in bindings:
        bound(item)
    for relative in TOOL_FILES:
        bound({'path': str(final_root / relative), 'sha256': digest(final_root / relative)}, approved=approved)
    return approved


def discover_summaries(root):
    paths = set()
    for directory, children, files in os.walk(root / 'development'):
        children[:] = [name for name in children if name.lower() not in {'input', 'bin', 'obj', '__pycache__'}]
        if 'independent-mutation-summary.json' in files:
            paths.add(str((Path(directory) / 'independent-mutation-summary.json').resolve()))
    return paths


def inventory_executions(stage, context, report_hash, nonviable):
    summaries = stage.get('executionSummaries', [])
    require(summaries, 'Every stage must inventory its complete set of execution summaries.')
    declared = {str(bound(x, Path(stage['root']))) for x in summaries}
    require(len(declared) == len(summaries) and declared == discover_summaries(Path(stage['root'])),
            'Stage execution-summary inventory is duplicate, incomplete or has arbitrary additions.')
    errors = []
    for item in summaries:
        summary = json.loads(bound(item, Path(stage['root'])).read_bytes())
        require(summary['sourceSha256'] == context['repairedInventory']['sha256'] and summary['reportSha256'] == report_hash,
                'Stage summary provenance differs.')
        for result in summary['results']:
            if (result['file'], result['mutant']['id']) in nonviable and result['build']['exitCode'] == 0 and not result['build']['timedOut']:
                errors.append('Successful ordinary compilation contradicts nonviability: ' + result['mutant']['id'])
    return errors


def compose(manifest_path, ledger_path, output, final_root):
    import repair_bridge
    import mutation_review
    final_root = final_root.resolve()
    require(not output.exists() and output.is_relative_to(final_root / 'development'), 'Finalization needs a new bounded output directory.')
    manifest = json.loads(manifest_path.read_bytes())
    stages = stage_shape(manifest, final_root)
    approved = approved_tools(manifest, final_root)
    ledger = json.loads(ledger_path.read_bytes())
    require(ledger.get('schemaVersion') == '2.0' and ledger.get('reviewer', '').strip()
            and bound(ledger['composition'], final_root) == manifest_path, 'The v2 ledger must bind this exact stage composition.')
    entries = ledger['entries']
    require(len({(e['file'], e['id']) for e in entries}) == len(entries), 'Duplicate mutation dispositions are forbidden.')
    stage_ids = {s['id'] for s in stages}
    for entry in entries:
        require(entry.get('reason', '').strip() and mutation_review.evidence_valid(entry, final_root), 'Exact final-stage review evidence is required.')
        if 'independentExecution' in entry:
            require(entry.get('executionStage') in stage_ids and entry['classification'] in EXECUTED, 'Execution stage/classification is missing or invalid.')
        else:
            require('executionStage' not in entry, 'A static disposition cannot claim an execution stage.')
    output.mkdir(parents=True)
    verified = []
    evidence = [manifest['toolReview'], {'path': str(manifest_path), 'sha256': digest(manifest_path)},
                {'path': str(ledger_path), 'sha256': digest(ledger_path)}]
    for stage in stages:
        request = {'stage': stage, 'approvedArtifacts': approved, 'finalRoot': str(final_root),
                   'entries': [e for e in entries if e.get('executionStage') == stage['id']]}
        request_path = output / (stage['id'] + '-request.json')
        save(request_path, request)
        request_hash = digest(request_path)
        command = [sys.executable, '-I', str(Path(__file__).resolve()), 'stage-check', '--request', str(request_path)]
        run = subprocess.run(command, cwd=stage['root'], text=True, capture_output=True, timeout=60)
        response_path = output / (stage['id'] + '-validation.json')
        response_path.write_text(run.stdout, encoding='utf-8')
        log_path = output / (stage['id'] + '-validation.log')
        log_path.write_text(run.stderr, encoding='utf-8')
        require(run.returncode == 0, 'Isolated stage validation failed: ' + stage['id'] + ': ' + run.stderr[-2000:])
        require(digest(request_path) == request_hash, 'Isolated stage request changed during verification.')
        context = json.loads(run.stdout)
        require(context.get('id') == stage['id'] and Path(context['root']).resolve() == Path(stage['root']).resolve(),
                'Isolated validator returned another stage identity/root.')
        expected_findings = Counter((e['file'], e['id'], e['classification']) for e in request['entries'])
        actual_findings = Counter((e['file'], e['id'], e['classification']) for e in context['findings'])
        require(actual_findings == expected_findings, 'Isolated validator did not reconcile every requested disposition exactly once.')
        if verified:
            prior = verified[-1]
            require(context['reportHash'] == prior['reportHash'] and context['run'] == prior['run']
                    and context['originalInventory'] == prior['originalInventory'], 'Stages reference different original campaigns.')
            validate_strengthening(prior['repairedPaths'], context['repairedPaths'], stage['changesFromParent'], Path(stage['root']), repair_bridge)
            require(repair_bridge.preserves_cases(prior['repairedCases'], context['repairedCases']), 'Successor omitted predecessor test cases.')
        verified.append(context)
        evidence.extend(context['evidence'])
        for path in (request_path, response_path, log_path):
            evidence.append({'path': str(path), 'sha256': digest(path)})
        execution_path = output / (stage['id'] + '-validator-execution.json')
        save(execution_path, {'command': command, 'workingDirectory': stage['root'],
             'validatorSha256': digest(__file__), 'exitCode': run.returncode, 'requestSha256': request_hash, 'outputSha256': digest(response_path)})
        evidence.append({'path': str(execution_path), 'sha256': digest(execution_path)})
    final = verified[-1]
    require(ledger.get('sourceSha256') == final['originalInventory']['sha256'] and ledger.get('reportSha256') == final['reportHash'],
            'Ledger original inventory/report is not exact.')
    report_path = Path(final['run']) / 'combined/mutation-report.json'
    report = json.loads(report_path.read_bytes())
    actual = {(file, m['id']): m for file, value in report['files'].items() for m in value['mutants']}
    require(len(actual) == sum(len(value['mutants']) for value in report['files'].values()), 'Original mutation inventory contains duplicates.')
    require(len({key[1] for key in actual}) == len(actual) and all(re.fullmatch(r'[0-9]+', key[1]) for key in actual),
            'Original mutation IDs must be globally unique numeric strings.')
    accepted = set()
    static = []
    for entry in entries:
        key = (entry['file'], entry['id'])
        require(key in actual, 'Disposition does not belong to the original mutation inventory.')
        exact_mutation(entry, actual[key])
        require(entry['sourceSha256'] == final['scope'].get('instrumentationSourceHashes', final['scope']['handwrittenSourceHashes']).get(entry['file']), 'Disposition source is not original/final product source.')
        if 'independentExecution' in entry:
            accepted.add(key)
        else:
            static.append(entry)
    static_ledger = dict(ledger, schemaVersion='1.0', entries=static)
    static_accepted, sources, errors = mutation_review.reconcile(static_ledger, final['before'], final['reportHash'], final['scope'], report, final_root)
    accepted.update(static_accepted)
    # A successful normal build means the mutant is viable, even if tests did not kill it.
    nonviable = {(e['file'], e['id']) for e in entries if e['classification'] == 'IndividuallyNonViable'}
    for stage, context in zip(stages, verified):
        errors.extend(inventory_executions(stage, context, final['reportHash'], nonviable))
        evidence.extend(stage['executionSummaries'])
    campaign = local_module('mutation-campaign', TOOL_ROOT)
    original = json.loads((Path(final['run']) / 'mutation-campaign-summary.json').read_bytes())
    summary = campaign.summarize(final['before'], final['before']['files'], final['scope'], report, original['exitCode'], report_path,
        after_source_sha=final['before']['sourceSha256'], accepted=accepted, reviewed_sources=sources, review_errors=errors, completed_threshold_failure=True)
    summary.update(format='exportaramus-multi-stage-mutation-campaign', schemaVersion='1.0',
        evidenceStrategy='complete-original-campaign-plus-exact-reviewed-stage-executions', sourceSha256=final['repairedInventory']['sha256'],
        afterSourceSha256=final['repairedInventory']['sha256'], originalCampaignSourceSha256=final['originalInventory']['sha256'],
        stageComposition=str(manifest_path), stageCompositionSha256=digest(manifest_path), stageScopeVerified=True,
        reportSha256=final['reportHash'], reviewLedger=str(ledger_path), reviewLedgerSha256=digest(ledger_path))
    for entry in entries:
        evidence.extend({'path': str((final_root / x['path']).resolve()), 'sha256': x['sha256']} for x in entry['evidence'])
        for key in ('causalReview', 'environmentReview'):
            if key in entry:
                evidence.append(entry[key])
    evidence.extend({'path': p, 'sha256': sha} for p, sha in approved.items())
    unique = {}
    for item in evidence:
        path = Path(item['path']).resolve()
        require(str(path) not in unique or unique[str(path)] == item['sha256'], 'Conflicting final evidence hashes.')
        unique[str(path)] = item['sha256']
    for path, sha in unique.items():
        bound({'path': path, 'sha256': sha})
    require(repair_bridge.live_paths(final_root, final.get('inventoryPolicy', 'legacy')) == final['repairedPaths'], 'Final source changed during reconciliation.')
    summary['reviewEvidence'] = [{'path': p, 'sha256': sha} for p, sha in sorted(unique.items())]
    save(output / 'mutation-campaign-summary.json', summary)
    save(output / 'unresolved-mutants.json', summary['unresolvedMutants'])
    return 0 if summary['gatePassed'] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('stage-check', 'finish'))
    parser.add_argument('--request', type=Path)
    parser.add_argument('--composition', type=Path)
    parser.add_argument('--ledger', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.mode == 'stage-check':
        require(args.request is not None, 'Stage request required.')
        print(json.dumps(stage_child(json.loads(args.request.read_bytes()))))
        return 0
    require(all((args.composition, args.ledger, args.output)), 'Composition, ledger and new output directory required.')
    return compose(args.composition.resolve(), args.ledger.resolve(), args.output.resolve(), TOOL_ROOT.parent)


if __name__ == '__main__':
    raise SystemExit(main())
