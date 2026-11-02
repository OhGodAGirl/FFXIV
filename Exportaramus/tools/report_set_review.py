"""Compose unchanged historical domains with complete newly measured source files.

This does not relax repair_bridge or transplant historical mutant reviews.
The first version admits only raw Killed outcomes in replacement reports.
"""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import subprocess
import sys
from collections import Counter
import ast
from datetime import datetime
import base64
import shutil
import uuid


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def bound(item):
    path = Path(item['path']).resolve()
    require(path.is_file() and digest(path) == item['sha256'], 'Artifact missing or changed: ' + str(path))
    return path


def read(item):
    return json.loads(bound(item).read_bytes())


def artifacts(value):
    if isinstance(value, dict):
        if 'path' in value and 'sha256' in value:
            yield value
        for child in value.values():
            yield from artifacts(child)
    elif isinstance(value, list):
        for child in value:
            yield from artifacts(child)


def validate_execution(execution, item, inventory_hash, final_root):
    require(execution.get('completed') is True and execution.get('inputsUnchanged') is True and execution.get('exitCode') in (0, 2), 'Fresh campaign did not finish with unchanged inputs.')
    raw = read(item['rawExecution'])
    require(raw.get('exitCode') == execution['exitCode'] and isinstance(raw.get('completed'), str), 'Raw process completion differs.')
    datetime.fromisoformat(raw['completed'].replace('Z', '+00:00'))
    if execution['exitCode'] == 2:
        log = bound(item['console']).read_text(encoding='utf-8', errors='replace')
        require('The mutation score is lower than the configured break threshold of 100%' in log and 'The final mutation score is ' in log, 'Exit2 is not a completed canonical threshold failure.')
    require(execution['beforeInventorySha256'] == inventory_hash == execution['afterInventorySha256'], 'Fresh campaign input inventory differs.')
    require(execution['reportSha256'] == item['report']['sha256'] and execution['configSha256'] == item['executionConfig']['sha256'], 'Fresh campaign report/config binding differs.')
    selected = read(item['executionConfig'])['stryker-config']['mutate']
    require(execution['mutate'] == selected, 'Actual executed scope differs.')
    process = read(item['process'])
    require(process['version'] == item['canonicalToolVersion'] and digest(process['runner']) == item['canonicalRunner']['sha256'], 'Mutation tool identity changed.')
    bound(item['canonicalRunner'])
    require(Path(process['workingDirectory']).resolve() == final_root / 'tests/Exportaramus.Testable', 'Mutation project differs.')
    arguments = process['arguments']
    expected = ['--config-file', str(bound(item['executionConfig'])), '--output', str(Path(item['outputDirectory']).resolve()), '--skip-version-check', '--verbosity', 'info', '--log-to-file', '--break-on-initial-test-failure']
    # Normalize only path separators for the two actual path arguments.
    require(len(arguments) == len(expected), 'Unexpected mutation command override.')
    for index, (actual, wanted) in enumerate(zip(arguments, expected)):
        require(Path(actual).resolve() == Path(wanted).resolve() if index in (1, 3) else actual == wanted, 'Unexpected mutation command override.')
    require(bound(item['report']).is_relative_to(Path(item['outputDirectory']).resolve()), 'Report is outside actual output directory.')


def canonical_mutation_version(source):
    versions = set()
    for node in ast.walk(ast.parse(source)):
        if isinstance(node, ast.Dict):
            for key, value in zip(node.keys, node.values):
                if isinstance(key, ast.Constant) and key.value == 'toolVersion':
                    require(isinstance(value, ast.Constant) and isinstance(value.value, str), 'Canonical mutation version is not a literal.')
                    versions.add(value.value)
    require(len(versions) == 1, 'Canonical mutation version is missing or ambiguous.')
    return versions.pop()


def recorded_run(command, root, prefix, timeout=120):
    prefix.parent.mkdir(parents=True, exist_ok=True)
    timed_out = False
    try:
        run = subprocess.run(command, cwd=root, capture_output=True, text=True, timeout=timeout)
        stdout, stderr, code = run.stdout, run.stderr, run.returncode
    except subprocess.TimeoutExpired as error:
        timed_out = True
        stdout, stderr, code = error.stdout or '', error.stderr or '', None
        if isinstance(stdout, bytes): stdout = stdout.decode('utf-8', errors='replace')
        if isinstance(stderr, bytes): stderr = stderr.decode('utf-8', errors='replace')
    log, execution = prefix.with_suffix('.log'), prefix.with_suffix('.json')
    log.write_text(stdout + stderr, encoding='utf-8')
    execution.write_text(json.dumps({'command': command, 'workingDirectory': str(root), 'exitCode': code, 'timedOut': timed_out, 'timeoutSeconds': timeout, 'output': {'path': str(log), 'sha256': digest(log)}}, indent=2), encoding='utf-8')
    require(not timed_out, 'Process timed out; complete diagnostics preserved: ' + str(execution))
    return run, [{'path': str(p), 'sha256': digest(p)} for p in (log, execution)]


def validate_runner_version(item, root, output=None):
    pwsh = shutil.which('pwsh')
    require(pwsh is not None, 'PowerShell is unavailable for actual runner metadata verification.')
    runner = bound(item['canonicalRunner'])
    encoded = base64.b64encode(str(runner).encode('utf-8')).decode('ascii')
    command = "[Diagnostics.FileVersionInfo]::GetVersionInfo([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "'))).ProductVersion"
    output = output or Path(root) / 'development/current-campaign-validation' / uuid.uuid4().hex
    run, evidence = recorded_run([pwsh, '-NoProfile', '-Command', command], root, output / 'runner-version')
    require(run.returncode == 0 and run.stdout.strip() == item['canonicalToolVersion'], 'Actual mutation runner version differs.')
    evidence.append({'path': str(Path(pwsh).resolve()), 'sha256': digest(pwsh)})
    return evidence


def execute_baselines(dotnet, root, output, expected, bridge):
    import suite_binding
    require(set(expected) == set(suite_binding.TARGETS), 'Every canonical suite baseline is required.')
    sdk = json.loads((root / 'global.json').read_bytes())['sdk']
    require(sdk.get('rollForward') == 'disable', 'SDK must be exactly pinned.')
    version, version_evidence = recorded_run([str(dotnet), '--version'], root, output / 'sdk-version')
    require(version.returncode == 0 and version.stdout.strip() == sdk['version'], 'Actual SDK differs from final global.json.')
    cases, evidence = {}, version_evidence
    for target in suite_binding.TARGETS:
        results = output / target
        require(not results.exists(), 'Baseline output must be new.')
        results.mkdir(parents=True)
        restore_command = suite_binding.command(dotnet, 'restore', root, target)
        restore, restore_evidence = recorded_run(restore_command, root, results / 'restore')
        require(restore.returncode == 0, 'Final canonical locked restore failed: ' + target)
        evidence.extend(restore_evidence)
        command = suite_binding.command(dotnet, 'test', root, target, results, phase='baseline')
        run, run_evidence = recorded_run(command, root, results / 'execution')
        require(run.returncode == 0, 'Final canonical baseline failed: ' + target)
        trx = results / 'tests.trx'
        cases[target] = bridge.canonical_trx(trx, target)
        require(bridge.preserves_cases(expected[target], cases[target]), 'Final baseline omitted historical cases.')
        evidence.extend(run_evidence + [{'path': str(trx), 'sha256': digest(trx)}])
    return cases, evidence


def replacement_config(canonical, selected, source):
    a = dict(canonical['stryker-config'])
    b = dict(selected['stryker-config'])
    require(source.startswith('src/') and source.endswith('.cs') and '..' not in source.split('/'), 'Invalid replacement source.')
    require(b.pop('mutate', None) == ['**/' + source.removeprefix('src/')], 'Replacement must select exactly the whole source file.')
    a.pop('mutate', None)
    require(a == b, 'Replacement changed canonical mutation settings.')
    require(a.get('thresholds') == {'high': 100, 'low': 100, 'break': 100}, 'Canonical gate is not 100%.')


def replacement_inventory(report, source, source_bytes):
    require(set(report.get('files', {})) == {source}, 'Replacement report has missing or additional source domains.')
    value = report['files'][source]
    require(value.get('source', '').encode('utf-8') == source_bytes, 'Replacement report source differs from final source.')
    mutants = value.get('mutants', [])
    require(mutants and len({m['id'] for m in mutants}) == len(mutants), 'Replacement inventory is empty or duplicated.')
    for mutant in mutants:
        require(mutant.get('status') in {'Killed', 'Survived', 'NoCoverage', 'Timeout', 'Ignored', 'CompileError'}, 'Incomplete mutation result.')
        require(all(k in mutant for k in ('id', 'location', 'replacement', 'mutatorName')), 'Incomplete mutant identity.')
    return mutants


def scoped_report(report, source, root, paths):
    require(Path(report['projectRoot']).resolve() == root / 'tests/Exportaramus.Testable', 'Report belongs to another project.')
    selected = {}
    seen = set()
    for raw, value in report.get('files', {}).items():
        path = Path(raw).resolve()
        require(path.is_relative_to(root), 'Report path escapes final source root.')
        canonical = path.relative_to(root).as_posix()
        require(canonical in paths and canonical not in seen, 'Report source is missing or duplicated in final inventory.')
        seen.add(canonical)
        if canonical == source:
            selected[canonical] = value
        else:
            require(value.get('mutants') == [] and value.get('source') == 'File ignored by mutate filter', 'Unexpected executable or unbound report domain.')
    return {'files': selected}


def source_domains(old, final, replaced):
    require(replaced and len(set(replaced)) == len(replaced), 'Replacement domains must be unique and nonempty.')
    for source in replaced:
        require(source in old and source in final and old[source] != final[source], 'Replacement is missing or unchanged.')
    # Everything else, including schemas, resources and project files, remains
    # subject to the existing strict strengthening validator below.
    return {k: v for k, v in old.items() if k not in replaced}, {k: v for k, v in final.items() if k not in replaced}


FINAL_REPORT_TOOL_FILES = frozenset({
    'tools/current_campaign.py', 'tools/test-current-campaign.py',
    'tools/report_set_review.py', 'tools/test-report-set-review.py',
})


def validate_final_strengthening(original, successor, changes, root, bridge, stage):
    """Review this layer's exact tools without expanding historical repair bridges."""
    extra = [c for c in changes if c['path'] in FINAL_REPORT_TOOL_FILES]
    evidence = bridge.validate_differences(
        {p: h for p, h in original.items() if p in FINAL_REPORT_TOOL_FILES},
        {p: h for p, h in successor.items() if p in FINAL_REPORT_TOOL_FILES},
        extra, root, FINAL_REPORT_TOOL_FILES, multi_suite=True)
    for change in extra:
        path = change['path']
        require(path in successor, 'Final report tool deletions are forbidden.')
        require(change.get('reviewed') is True and change.get('reviewer', '').strip(),
                'Final report tool changes require an identified independent review.')
        require(digest(Path(root) / path) == successor[path], 'Final report tool bytes differ from inventory.')
    stage.validate_strengthening(
        {p: h for p, h in original.items() if p not in FINAL_REPORT_TOOL_FILES},
        {p: h for p, h in successor.items() if p not in FINAL_REPORT_TOOL_FILES},
        [c for c in changes if c['path'] not in FINAL_REPORT_TOOL_FILES], root, bridge, multi_suite=True)
    return evidence


def case_coverage(full_cases, instrumented_cases, performance_cases):
    require(Counter(instrumented_cases) + Counter(performance_cases) == Counter(full_cases), 'Coverage/performance cases do not exactly partition the full provider baseline.')


def documentation_change(items, old, final):
    require(len(items) <= 1, 'Only the root README documentation change is supported.')
    evidence = []
    old, final = dict(old), dict(final)
    for item in items:
        require(item.get('path') == 'README.md' and 'README.md' in old and 'README.md' in final, 'Documentation exception is limited to the existing root README.')
        proof = read(item['review'])
        require(proof.get('reviewed') is True and proof.get('reviewer', '').strip() and proof.get('reason', '').strip() and proof.get('noExecutableOrConfigConsumption') is True, 'Reviewed nonexecutable README evidence is required.')
        require(proof.get('path') == 'README.md' and proof.get('beforeSha256') == old['README.md'] and proof.get('afterSha256') == final['README.md'], 'README review content binding differs.')
        require(item['before']['sha256'] == old['README.md'] and item['after']['sha256'] == final['README.md'], 'README content artifacts differ.')
        bound(item['before']); bound(item['after'])
        require(proof.get('evidence') and all(bound(x) for x in proof['evidence']), 'README consumption audit is missing.')
        evidence.extend(proof['evidence'])
        del old['README.md']; del final['README.md']
    return old, final, evidence


def historical_receipt(receipt, report_hash):
    require(receipt.get('stageScopeVerified') is True and not receipt.get('error') and receipt.get('reportSha256') == report_hash, 'Historical validation did not verify this campaign.')
    allowed_pending = {'Unresolved mutants remain; score alone does not satisfy the configured zero-survivor/coverage/timeout gate.', 'CompileError mutants require individual non-viability review; safe-mode collateral is not automatically accepted.'}
    require(set(receipt.get('reviewRequired', [])) <= allowed_pending, 'Historical evidence has validation errors beyond unresolved dispositions.')


def historical_pending(receipt):
    reviewed = {(x['file'], x['id']) for x in receipt['reviewedMutants']}
    require(len(reviewed) == len(receipt['reviewedMutants']), 'Historical reviewed identities are duplicated.')
    pending = list(receipt['unresolvedMutants'])
    require(not reviewed.intersection((x['file'], x['id']) for x in pending), 'Historical accepted and unresolved identities conflict.')
    for mutant in receipt['compileErrorMutants']:
        key = (mutant['file'], mutant['id'])
        require(mutant.get('reviewedIndividually') is (key in reviewed), 'Historical compiler review flag differs from actual accepted inventory.')
        if key not in reviewed:
            pending.append(mutant)
    return pending


def current_outcomes(old_report, historical_unresolved, replacements, accepted=None):
    accepted = accepted or set()
    old_keys = {(p, m['id']) for p, f in old_report['files'].items() for m in f['mutants']}
    require(len(old_keys) == sum(len(f['mutants']) for f in old_report['files'].values()), 'Historical report has duplicate identities.')
    unresolved_keys = {(m['file'], m['id']) for m in historical_unresolved}
    require(len(unresolved_keys) == len(historical_unresolved) and unresolved_keys <= old_keys, 'Historical unresolved identities differ.')
    require(set(replacements) <= set(old_report['files']), 'Replacement domain did not exist in historical report.')
    fresh_keys = {(source, m['id']) for source, mutants in replacements.items() for m in mutants}
    require(accepted <= fresh_keys, 'Fresh accepted review is outside the replacement inventory.')
    unresolved = [dict(m, reportSet='historical') for m in historical_unresolved if m['file'] not in replacements]
    retained = sum(len(f['mutants']) for p, f in old_report['files'].items() if p not in replacements)
    superseded = sum(len(old_report['files'][p]['mutants']) for p in replacements)
    for source, mutants in replacements.items():
        unresolved.extend(dict(m, file=source, reportSet='replacement') for m in mutants if m['status'] != 'Killed' and (source, m['id']) not in accepted)
    return {'currentMutants': retained + sum(map(len, replacements.values())),
            'historicalSupersededMutants': superseded, 'unresolvedMutants': unresolved,
            'gatePassed': not unresolved}


def validate_mutation_fidelity(entries, report_artifact, descriptor, excluded=(), stage_validator=None, report_root=None):
    """Bind individually reviewed AST audit evidence to every retained admission."""
    require(isinstance(descriptor, dict) and descriptor.get('reviewed') is True
            and descriptor.get('reviewer', '').strip() and descriptor.get('reason', '').strip(),
            'A reviewed complete mutation fidelity audit is required.')
    require(descriptor.get('report') == report_artifact, 'Fidelity audit names another report.')
    report = read(report_artifact); audit = read(descriptor['audit'])
    require(audit.get('reportSha256') == report_artifact['sha256'], 'Fidelity audit report hash differs.')
    require(descriptor.get('preservesOriginalOperatorChildren') is True,
            'Audit must preserve original operator children, not parse replacement text alone.')
    require(descriptor.get('helper') and descriptor.get('evidence'), 'Fidelity audit implementation and independent review are required.')
    bound(descriptor['helper'])
    independent = read(descriptor['independentReview'])
    require(independent.get('reviewed') is True and independent.get('reviewer', '').strip()
            and independent.get('reason', '').strip() and independent.get('audit') == descriptor['audit']
            and independent.get('helper') == descriptor['helper'], 'Independent audit review does not bind this implementation and result.')
    for artifact in descriptor['evidence']: bound(artifact)
    originals = {(file, m['id']): (value, m) for file, value in report['files'].items() for m in value['mutants']}
    rows = {(r['file'], r['id']): r for r in audit['rows']}
    require(len(rows) == len(audit['rows']) and set(rows) == set(originals), 'Fidelity audit is incomplete or duplicates identities.')
    if report_root is not None:
        report_root = Path(report_root).resolve()
        def canonical(key):
            path = Path(key[0])
            resolved = path.resolve() if path.is_absolute() else (report_root / path).resolve()
            require(resolved.is_relative_to(report_root), 'Fidelity report source lies outside measured root.')
            return (resolved.relative_to(report_root).as_posix(), key[1])
        normalized_originals = {canonical(k): v for k, v in originals.items()}
        normalized_rows = {canonical(k): v for k, v in rows.items()}
        require(len(normalized_originals) == len(originals) and len(normalized_rows) == len(rows), 'Duplicate normalized fidelity identity.')
        originals, rows = normalized_originals, normalized_rows
    safe_statements = {'Block', 'ExpressionStatement', 'ReturnStatement', 'ThrowStatement', 'BreakStatement', 'ContinueStatement', 'YieldReturnStatement', 'YieldBreakStatement'}
    for entry in entries:
        if entry['file'] in excluded: continue
        key = (entry['file'], entry['id']); require(key in rows, 'Accepted identity is absent from fidelity audit.')
        row = rows[key]; source, mutant = originals[key]
        require(row['sourceSha256'] == hashlib.sha256(source['source'].encode('utf-8')).hexdigest()
                and row['location'] == mutant['location'] and row['replacement'] == mutant['replacement'],
                'Fidelity audit source or mutation identity differs.')
        valid_shape = lambda value: isinstance(value, str) and len(value) == 64 and all(c in '0123456789abcdef' for c in value)
        same = row.get('audit') == 'SameExpressionTree' and valid_shape(row.get('expectedShapeSha256')) and row.get('expectedShapeSha256') == row.get('nativeShapeSha256')
        statement = row.get('audit') == 'NotExactExpression' and row.get('exactNode') is True and row.get('nodeKind') in safe_statements
        if same or statement: continue
        require(row.get('audit') == 'DifferentExpressionTree' and valid_shape(row.get('expectedShapeSha256')) and valid_shape(row.get('nativeShapeSha256')),
                'Fidelity audit lacks exact intended and actual syntax tree hashes.')
        if entry.get('independentExecution'):
            execution = entry['independentExecution']
            require(Path(execution['summary']).is_absolute(), 'Corrective fidelity execution path must be canonical absolute.')
            summary = json.loads(Path(execution['summary']).read_bytes())
            require(digest(execution['summary']) == execution['sha256'], 'Corrective execution summary changed.')
            matches = [r for r in summary['results'] if r['file'] == entry['file'] and r['mutant']['id'] == entry['id']]
            require(summary.get('replacementMode') == 'roslyn-expression-v1' and len(matches) == 1
                    and matches[0].get('replacementMode') == 'roslyn-expression-v1',
                    'Legacy textual execution differs from intended AST and requires faithful replay.')
            require(stage_validator is not None, 'Corrective execution requires independent current AST verification.')
            inventory_path = Path(summary['inventory'])
            require(digest(inventory_path) == summary['inventorySha256'], 'Corrective execution inventory changed.')
            inputs = {x['path']: x['sha256'] for x in json.loads(inventory_path.read_bytes())['files']}
            snapshot = Path(summary['snapshot']).resolve()
            rendered, original_span = stage_validator.recompute_mutation(snapshot, source['source'], entry, matches[0], summary, inputs, None)
            require(matches[0].get('originalSpan') == original_span, 'Corrective original span differs.')
            mutated = Path(matches[0]['mutatedSource'])
            require(digest(mutated) == matches[0]['mutatedSourceSha256'] and mutated.read_bytes() == rendered.encode('utf-8'),
                    'Corrective mutation is not the independently rendered intended AST.')
        else:
            require(isinstance(entry.get('astReview'), dict), 'Static mismatch lacks an intended-AST source review.')
            proof = read(entry['astReview'])
            require(entry.get('classification') == 'Equivalent' and proof.get('reviewed') is True
                    and proof.get('reviewer', '').strip() and proof.get('reason', '').strip()
                    and proof.get('file') == entry['file'] and proof.get('id') == entry['id']
                    and proof.get('sourceSha256') == row['sourceSha256']
                    and proof.get('expectedShapeSha256') == row.get('expectedShapeSha256')
                    and proof.get('reportSha256') == report_artifact['sha256'],
                    'Static review must explicitly address the intended mutation AST.')
    return list(artifacts(descriptor)) + [entry['astReview'] for entry in entries if entry.get('astReview')]


def current_review_child(request_path):
    request = json.loads(request_path.read_bytes())
    root = Path(request['root']).resolve()
    pins = request['validatorFiles']
    require({p.name for p in (root / 'tools').glob('*.py')} == set(pins), 'Current reviewer closure is incomplete.')
    for name, item in pins.items():
        require(bound(item) == root / 'tools' / name, 'Current reviewer tool is outside exact frozen root.')
    require(pins['report_set_review.py']['sha256'] == digest(__file__), 'Running current reviewer differs.')
    sys.path.insert(0, str(root / 'tools'))
    import current_campaign
    import mutation_review
    import multi_stage_review
    binding = current_campaign.validate_current_campaign(bound(request['currentCampaign']), root,
        bound(request['inventory']), bound(request['report']))
    ledger = read(request['ledger'])
    accepted = mutation_review.reconcile_current(ledger, binding, read(request['report']), root)
    evidence = list(artifacts(request)) + binding['evidence']
    fidelity_entries = list(ledger.get('entries', []))
    entry_sets = [(ledger, root, binding['inventoryPaths'])]
    if request.get('nativeLedger'):
        native_ledger = read(request['nativeLedger'])
        native_accepted = mutation_review.reconcile_current(native_ledger, binding, read(request['report']), root, native=True)
        require(not accepted.intersection(native_accepted), 'Duplicate native/current disposition.')
        accepted.update(native_accepted)
        measured_root = Path(read(request['currentCampaign'])['campaignRoot']).resolve()
        measured_paths = {r['path']: r['sha256'] for r in binding['campaignInventory']['files']}
        entry_sets.append((native_ledger, measured_root, measured_paths))
        fidelity_entries.extend(native_ledger.get('entries', []))
    for ledger_value, origin, input_paths in entry_sets:
      for entry in ledger_value.get('entries', []):
        evidence.extend({'path': str((origin / x['path']).resolve()), 'sha256': x['sha256']} for x in artifacts(entry))
        proof = entry['independentExecution']
        summary_path = origin / proof['summary']
        require(digest(summary_path) == proof['sha256'], 'Current summary changed.')
        evidence.append({'path': str(summary_path), 'sha256': proof['sha256']})
        summary = json.loads(summary_path.read_bytes())
        evidence.extend({'path': str((origin / x['path']).resolve()), 'sha256': x['sha256']} for x in artifacts(summary))
        for source, sha in input_paths.items():
            evidence.append({'path': str(Path(summary['snapshot']) / source), 'sha256': sha})
            evidence.append({'path': str(origin / source), 'sha256': sha})
        def execution_artifacts(value):
            if isinstance(value, dict):
                for key, candidate in value.items():
                    if isinstance(candidate, str) and key + 'Sha256' in value:
                        yield {'path': candidate, 'sha256': value[key + 'Sha256']}
                    yield from execution_artifacts(candidate)
            elif isinstance(value, list):
                for child in value: yield from execution_artifacts(child)
        evidence.extend({'path': str((origin / x['path']).resolve()), 'sha256': x['sha256']} for x in execution_artifacts(summary))
    evidence.extend(validate_mutation_fidelity(fidelity_entries, request['report'], request.get('mutationFidelity'), stage_validator=multi_stage_review, report_root=Path(read(request['currentCampaign'])['campaignRoot'])))
    normalized = {}
    for item in evidence:
        path = Path(item['path'])
        if not path.is_absolute(): path = root / path
        bound({'path': str(path), 'sha256': item['sha256']})
        normalized[str(path.resolve())] = item['sha256']
    return {'accepted': sorted(accepted), 'reportSha256': binding['reportHash'], 'currentCampaignSha256': binding['receiptSha256'], 'reviewEvidence': [{'path': p, 'sha256': h} for p, h in sorted(normalized.items())]}


def validate_final_campaign(artifact, root, inventory_artifact, report_artifact):
    import current_campaign
    require(Path(current_campaign.__file__).resolve() == root / 'tools/current_campaign.py',
            'Final campaign validator is not the final pinned tool closure.')
    return current_campaign.validate_current_campaign(bound(artifact), root, bound(inventory_artifact), bound(report_artifact), 'provider')


def measurement_context(item, final_root, final_inventory, final_paths, final_artifact, bridge):
    before, before_paths = bridge.inventory(bound(item['measurementBefore']))
    after, after_paths = bridge.inventory(bound(item['measurementAfter']))
    require(before == after and before_paths == after_paths, 'Measurement inputs changed during mutation execution.')
    root = Path(item.get('measurementRoot', str(final_root))).resolve()
    require(bridge.live_paths(root, 'current') == before_paths, 'Measured live inputs differ.')
    source = item['source']
    require(source in before_paths and before_paths[source] == final_paths.get(source), 'Measured production source differs from final source.')
    evidence = [item['measurementBefore'], item['measurementAfter']]
    successor = item.get('finalCurrentCampaign')
    if before != final_inventory or before_paths != final_paths or successor is not None:
        require(isinstance(successor, dict), 'Changed final inputs require a validated current-campaign successor.')
        receipt = read(successor)
        require(Path(receipt['campaignRoot']).resolve() == root, 'Successor names another measured root.')
        binding = validate_final_campaign(successor, final_root, final_artifact, item['report'])
        require(binding['inventory'] == final_inventory and binding['inventoryPaths'] == final_paths,
                'Successor final inventory differs.')
        require(binding['campaignInventory'] == before and binding['campaignInventorySha256'] == before['sha256'],
                'Successor measured inventory differs.')
        require(binding['reportHash'] == item['report']['sha256'] and binding['sourcePaths'] == {source}
                and binding['campaignSourceSha256'] == before_paths[source], 'Successor report/source binding differs.')
        measured = receipt['measurement']
        require(measured['source'] == source and measured['canonicalToolVersion'] == item['canonicalToolVersion'],
                'Successor source or actual runner version differs.')
        for key in ('report', 'executionConfig', 'process', 'canonicalRunner', 'execution'):
            require(bound(measured[key]) == bound(item[key]) and measured[key]['sha256'] == item[key]['sha256'],
                    'Successor measurement artifact differs: ' + key)
        require(Path(measured['outputDirectory']).resolve() == Path(item['outputDirectory']).resolve(),
                'Successor measurement output differs.')
        evidence.extend(binding['evidence'] + [successor])
    return {'root': root, 'inventory': before, 'paths': before_paths, 'evidence': evidence}


def compose(plan_path, output):
    plan = json.loads(plan_path.read_bytes())
    require(plan.get('schemaVersion') == '1.0' and plan.get('reviewer', '').strip(), 'Reviewed report-set plan required.')
    require(not output.exists(), 'Output must be new; failed attempts are preserved.')
    plan_hash = digest(plan_path)
    composer_hash = digest(__file__)
    evidence = list(artifacts(plan))
    for artifact in evidence:
        bound(artifact)
    output.mkdir(parents=True)
    pins = plan['validatorFiles']
    tools = bound(pins['multi_stage_review.py']).parent
    require(all(bound(v).parent == tools and Path(v['path']).name == k for k, v in pins.items()), 'Validator tool closure differs.')
    needed = {'multi_stage_review.py', 'repair_bridge.py', 'mutation_review.py', 'mutation-campaign.py', 'suite_binding.py', 'qualification_inputs.py'}
    require(needed <= set(pins), 'Incomplete validator tool closure.')
    require({p.name for p in tools.glob('*.py')} <= set(pins), 'Every local Python validator dependency must be pinned.')
    sys.path.insert(0, str(tools))
    import repair_bridge as bridge
    spec = importlib.util.spec_from_file_location('report_set_stage_validator', tools / 'multi_stage_review.py')
    stage = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(stage)
    historical = plan['historical']
    root = Path(historical['root']).resolve()
    manifest = bound(historical['composition'])
    ledger = bound(historical['ledger'])
    old_report = read(historical['report'])
    # Never accept a supplied gate boolean. Execute the strict historical
    # validator and consume only the receipt generated by this invocation.
    old_output = root / 'development' / ('report-set-' + output.name)
    require(not old_output.exists(), 'Historical verification output already exists.')
    launcher = output / 'historical-launch.py'
    launcher.write_text("import sys\nfrom pathlib import Path\nsys.path.insert(0, sys.argv[1])\nimport multi_stage_review as validator\nsys.exit(validator.compose(Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]), Path(sys.argv[5])))\n", encoding='utf-8')
    command = [sys.executable, '-I', str(launcher), str(tools), str(manifest), str(ledger), str(old_output), str(root)]
    run, historical_execution = recorded_run(command, root, output / 'historical-execution', timeout=1800)
    evidence.extend(historical_execution + [{'path': str(launcher), 'sha256': digest(launcher)}])
    require(run.returncode in (0, 1), 'Historical validator failed.')
    receipt_path = old_output / 'mutation-campaign-summary.json'
    require(receipt_path.is_file(), 'Historical validator failed before emitting a receipt; preserved execution log explains the failure.')
    receipt = json.loads(receipt_path.read_bytes())
    historical_receipt(receipt, historical['report']['sha256'])
    old_inventory, old_paths = bridge.inventory(bound(historical['inventory']))
    require(receipt['sourceSha256'] == old_inventory['sha256'], 'Historical final inventory differs.')
    final_root = Path(plan['finalRoot']).resolve()
    final_inventory, final_paths = bridge.inventory(bound(plan['finalInventory']))
    require(bridge.live_paths(final_root, 'current') == final_paths, 'Final live inventory differs.')
    replaced = [x['source'] for x in plan['replacements']]
    a, b = source_domains(old_paths, final_paths, replaced)
    a, b, documentation_evidence = documentation_change(plan.get('documentationChanges', []), a, b)
    evidence.extend(documentation_evidence)
    evidence.extend(validate_final_strengthening(a, b, plan['changesFromHistorical'], final_root, bridge, stage))
    # Separate coverage checkout is permitted only when every input is exact.
    coverage_root = Path(plan['coverageRoot']).resolve()
    cov_inventory, cov_paths = bridge.inventory(bound(plan['coverageInventory']))
    require(cov_paths == final_paths and cov_inventory == final_inventory and bridge.live_paths(coverage_root, 'current') == final_paths, 'Coverage root is not byte-identical.')
    final_context = json.loads((old_output / (json.loads(manifest.read_bytes())['stages'][-1]['id'] + '-validation.json')).read_bytes())
    before_cases = {'provider': final_context['repairedCases']}
    before_cases.update({k: v['repaired'] for k, v in final_context['targetCases'].items()})
    dotnet = bound(plan['dotnet'])
    baseline_cases, baseline_evidence = execute_baselines(dotnet, final_root, output / 'baselines', before_cases, bridge)
    evidence.extend(baseline_evidence)
    coverage = read(plan['coverage'])
    covered_cases, coverage_evidence = bridge.repaired_baseline({'repairedBaselineSummary': plan['coverage']}, coverage_root, final_inventory, [])
    evidence.extend(coverage_evidence)
    performance_cases = bridge.canonical_trx(bound(plan['performanceBaseline']), 'provider')
    case_coverage(baseline_cases['provider'], covered_cases, performance_cases)
    evidence.extend(bridge.component_coverage(coverage, coverage_root, final_paths))
    replacements = {}
    normalizations = []
    measurement_contexts = []
    fresh_accepted = set()
    for item in plan['replacements']:
        source = item['source']
        repair_review = read(item['productionReview'])
        require(repair_review.get('reviewed') is True and repair_review.get('reviewer', '').strip() and repair_review.get('reason', '').strip(), 'Individual production repair review is required.')
        require(repair_review.get('source') == source and repair_review.get('beforeSha256') == old_paths[source] and repair_review.get('afterSha256') == final_paths[source], 'Production repair review is not exact.')
        require(repair_review.get('evidence') and all(bound(x) for x in repair_review['evidence']), 'Production repair evidence is missing.')
        evidence.extend(repair_review['evidence'])
        require(not item.get('ledger'), 'A bare fresh ledger is unsupported; use an independently validated currentReview descriptor.')
        replacement_config(read(item['canonicalConfig']), read(item['executionConfig']), source)
        require(digest(bound(item['canonicalConfig'])) == old_paths[item['canonicalConfigPath']], 'Canonical config is not historical canonical input.')
        measured = measurement_context(item, final_root, final_inventory, final_paths, plan['finalInventory'], bridge)
        measurement_contexts.append(measured)
        evidence.extend(measured['evidence'])
        measured_root = measured['root']
        validate_execution(read(item['execution']), item, measured['inventory']['sha256'], measured_root)
        canonical_version = canonical_mutation_version((measured_root / 'tools/mutation-campaign.py').read_text(encoding='utf-8'))
        require(item['canonicalToolVersion'].split('+')[0] == canonical_version, 'Mutation version differs from final canonical campaign tooling.')
        evidence.extend(validate_runner_version(item, measured_root, output / ('mutation-version-' + str(len(replacements)))))
        raw_report = read(item['report'])
        replacements[source] = replacement_inventory(scoped_report(raw_report, source, measured_root, measured['paths']), source, (final_root / source).read_bytes())
        normalization = {'rawReport': item['report'], 'selectedSource': source, 'selectedSourceSha256': final_paths[source], 'selectedMutants': len(replacements[source]), 'selectedMutantIdentitiesSha256': hashlib.sha256(json.dumps(replacements[source], sort_keys=True, separators=(',', ':')).encode('utf-8')).hexdigest(), 'paths': [{'raw': raw, 'canonical': Path(raw).resolve().relative_to(measured_root).as_posix(), 'selected': Path(raw).resolve() == measured_root / source, 'mutants': len(value['mutants'])} for raw, value in raw_report['files'].items()]}
        normalization_path = output / ('normalization-' + str(len(normalizations)) + '.json')
        normalization_path.write_text(json.dumps(normalization, indent=2), encoding='utf-8')
        normalizations.append({'path': str(normalization_path), 'sha256': digest(normalization_path)})
        evidence.extend(normalizations[-1:])
        if item.get('currentReview'):
            request = item['currentReview']
            require(request['report'] == item['report'], 'Current reviews name another fresh report.')
            request_path = output / ('current-request-' + str(len(normalizations)) + '.json')
            request_path.write_text(json.dumps(request, indent=2), encoding='utf-8')
            tool = bound(request['validatorFiles']['report_set_review.py'])
            run, run_evidence = recorded_run([sys.executable, '-I', str(tool), '--current-request', str(request_path)], Path(request['root']), output / ('current-review-' + str(len(normalizations))), timeout=1800)
            require(run.returncode == 0, 'Fresh current review failed; diagnostics preserved.')
            reviewed = json.loads(run.stdout)
            require(reviewed['reportSha256'] == item['report']['sha256'] and reviewed['currentCampaignSha256'] == request['currentCampaign']['sha256'], 'Current reviewer returned different bindings.')
            accepted = {tuple(x) for x in reviewed['accepted']}
            require(len(accepted) == len(reviewed['accepted']) and not fresh_accepted.intersection(accepted), 'Duplicate current accepted entries.')
            fresh_accepted.update(accepted)
            evidence.extend(run_evidence + reviewed['reviewEvidence'] + [{'path': str(request_path), 'sha256': digest(request_path)}])
    accepted_historical = {(r['file'], r['id']) for r in receipt['reviewedMutants']}
    retained_entries = [e for e in json.loads(ledger.read_bytes())['entries'] if (e['file'], e['id']) in accepted_historical]
    evidence.extend(validate_mutation_fidelity(retained_entries, historical['report'], historical.get('mutationFidelity'), replacements, stage_validator=stage))
    result = current_outcomes(old_report, historical_pending(receipt), replacements, fresh_accepted)
    # Recheck all inputs after potentially long validators and report reads.
    require(bridge.live_paths(final_root, 'current') == final_paths and bridge.live_paths(coverage_root, 'current') == final_paths, 'Inputs changed during composition.')
    for measured in measurement_contexts:
        require(bridge.live_paths(measured['root'], 'current') == measured['paths'], 'Measured inputs changed during composition.')
    for value in evidence:
        bound(value)
    require(digest(plan_path) == plan_hash, 'Plan changed during composition.')
    require(digest(__file__) == composer_hash, 'Composer changed during verification.')
    evidence.extend([{'path': str(plan_path), 'sha256': plan_hash}, {'path': str(Path(__file__).resolve()), 'sha256': composer_hash}, {'path': str(receipt_path), 'sha256': digest(receipt_path)}])
    evidence.extend(receipt['reviewEvidence'])
    closure = {}
    for item in evidence:
        path = str(bound(item))
        require(path not in closure or closure[path] == item['sha256'], 'Conflicting evidence hashes.')
        closure[path] = item['sha256']
    result.update(format='exportaramus-report-set-review', schemaVersion='1.0', sourceSha256=final_inventory['sha256'], historicalReceipt={'path': str(receipt_path), 'sha256': digest(receipt_path)}, originalRawReport=historical['report'], replacementReports=[x['report'] for x in plan['replacements']], normalizations=normalizations, plan={'path': str(plan_path), 'sha256': plan_hash}, reviewEvidence=[{'path': p, 'sha256': h} for p, h in sorted(closure.items())])
    (output / 'report-set-summary.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--plan', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--current-request', type=Path)
    args = parser.parse_args()
    if args.current_request:
        require(args.plan is None and args.output is None, 'Current child request cannot also compose.')
        print(json.dumps(current_review_child(args.current_request.resolve())))
        sys.exit(0)
    require(args.plan is not None and args.output is not None, 'Composition requires plan and output.')
    result = compose(args.plan.resolve(), args.output.resolve())
    sys.exit(0 if result['gatePassed'] else 1)
