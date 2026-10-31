"""Capture and verify one complete Stryker run without changing its source scope."""
from pathlib import Path
import argparse
from datetime import datetime, timezone
import hashlib
import json
import subprocess
import sys
sys.dont_write_bytecode = True
from mutation_review import reconcile

ROOT = Path(__file__).resolve().parents[1]
STATUSES = ('Killed', 'Survived', 'NoCoverage', 'CompileError', 'RuntimeError', 'Timeout', 'Ignored', 'Pending')


def campaign_definitions(root):
    definitions = (
        ('testable', 'tests/Exportaramus.Testable/Exportaramus.Testable.csproj',
         'tests/stryker-config.json', '../Exportaramus.Tests/Exportaramus.Tests.csproj'),
        ('contracts11', 'project/Contracts/1.1/Exportaramus.ConsumerKit.V1_1.Runtime.csproj',
         'tests/stryker-contracts-1.1-config.json', '../../../tests/Exportaramus.Tests/Exportaramus.Tests.csproj'),
        ('consumerkit12', 'project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj',
         'tests/stryker-consumerkit-1.2-config.json', '../../../tests/Exportaramus.ConsumerKit.Tests/Exportaramus.ConsumerKit.Tests.csproj'),
        ('inventoryreader', 'tests/Exportaramus.InventoryReader.Mutation/Exportaramus.InventoryReader.Mutation.csproj',
         'tests/stryker-inventory-reader-config.json', '../Exportaramus.InventoryReader.Mutation.Tests/Exportaramus.InventoryReader.Mutation.Tests.csproj'),
    )
    return [{'name': name, 'project': root / project, 'workingDirectory': (root / project).parent,
             'config': root / config, 'testProject': test} for name, project, config, test in definitions]


def timestamp():
    return datetime.now(timezone.utc).isoformat(timespec='milliseconds').replace('+00:00', 'Z')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def input_hashes(root, legacy=False):
    if not legacy:
        from qualification_inputs import current_paths
        return current_paths(root)
    extensions = {'.cs', '.csproj', '.props', '.targets', '.json', '.runsettings', '.ps1', '.py', '.slnx'}
    paths = set()
    for directory in ('src', 'tests', 'reference', 'data', 'tools'):
        for path in (root / directory).rglob('*'):
            if path.is_file() and path.suffix in extensions and not {'bin', 'obj', '__pycache__'}.intersection(path.relative_to(root).parts):
                paths.add(path)
    paths.update(path for path in root.iterdir() if path.is_file() and path.suffix in extensions)
    return {path.relative_to(root).as_posix(): digest(path) for path in sorted(paths)}


def audit_scope(run, phase, report=None, complete=True):
    output = run / (phase + '-scope')
    command = [sys.executable, str(ROOT / 'tools/contract-quality-scope.py'), '--output-dir', str(output)]
    if complete:
        command.extend(['--mutation-scope', 'complete'])
    if report is not None:
        command.extend(['--mutation-report', str(report)])
    result = subprocess.run(command, cwd=ROOT, text=True, encoding='utf-8', capture_output=True)
    (run / (phase + '-scope.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
    if result.returncode:
        raise RuntimeError(f'{phase} scope verification failed; see {phase}-scope.log')
    return json.loads((output / 'coverage-manifest.json').read_bytes())


def validate_assembly_configs(root):
    primary = json.loads((root / 'tests/stryker-config.json').read_bytes())['stryker-config']
    ignored_keys = {'project', 'test-projects'}
    if primary.get('mutate') != ['**/*'] or primary.get('thresholds') != {'high': 100, 'low': 100, 'break': 100}:
        raise ValueError('Primary mutation scope and thresholds must remain strict.')
    for campaign in campaign_definitions(root):
        config = json.loads(campaign['config'].read_bytes())['stryker-config']
        if {k:v for k,v in primary.items() if k not in ignored_keys} != {k:v for k,v in config.items() if k not in ignored_keys}:
            raise ValueError('All assembly campaigns must use identical strict mutation settings.')
        if config['project'] != campaign['project'].name or config['test-projects'] != [campaign['testProject']]:
            raise ValueError('A mutation campaign does not target its canonical assembly and complete configured suite.')


def prepare(run, runner):
    validate_assembly_configs(ROOT)
    if (run / 'input-manifest.json').exists():
        raise ValueError('This run already has an input manifest; use a new run directory.')
    run.mkdir(parents=True, exist_ok=True)
    source = json.loads((run / 'source-before.json').read_bytes())
    scope = audit_scope(run, 'before')
    value = {
        'format': 'exportaramus-mutation-inputs', 'schemaVersion': '2.0', 'startedAtUtc': timestamp(),
        'root': str(ROOT), 'sourceSha256': source['sha256'], 'toolVersion': '4.16.0', 'toolPath': str(runner), 'toolSha256': digest(runner),
        'configurationSha256': digest(ROOT / 'tests/stryker-config.json'),
        'configurationHashes': {item['config'].relative_to(ROOT).as_posix(): digest(item['config']) for item in campaign_definitions(ROOT)}, 'linkedSourceCount': scope['linkedSourceCount'],
        'files': input_hashes(ROOT),
    }
    write_json(run / 'input-manifest.json', value)
    print(json.dumps({'runDirectory': str(run), 'sourceSha256': value['sourceSha256'], 'inputs': len(value['files']), 'linkedSources': value['linkedSourceCount']}))


def summarize(before, current, scope, report, exit_code, report_path=None, audit_error=None, after_source_sha=None,
              accepted=None, reviewed_sources=None, review_errors=None, completed_threshold_failure=False):
    changed = sorted(key for key in set(before['files']) | set(current) if before['files'].get(key) != current.get(key))
    inputs_unchanged = not changed and before['sourceSha256'] == after_source_sha
    audit = scope.get('mutationInstrumentationAudit', {}) if scope else {}
    counts = {status: 0 for status in STATUSES}
    unresolved = []
    raw_unresolved = []
    compile_errors = []
    reviewed = []
    accepted = accepted or set()
    reviewed_sources = reviewed_sources or set()
    mapping = {value.get('reportPath'): key for key, value in audit.get('instrumentedFiles', {}).items()}
    mutants_total = 0
    if report is not None:
        for path, entry in report.get('files', {}).items():
            for mutant in entry.get('mutants', []):
                status = mutant.get('status', 'Unknown')
                counts[status] = counts.get(status, 0) + 1
                mutants_total += 1
                detail = {'file': path, 'id': mutant.get('id'), 'status': status, 'mutatorName': mutant.get('mutatorName'),
                          'location': mutant.get('location'), 'replacement': mutant.get('replacement'), 'statusReason': mutant.get('statusReason')}
                if status != 'Killed':
                    raw_unresolved.append(dict(detail))
                is_reviewed = (mapping.get(path), mutant.get('id')) in accepted
                if is_reviewed:
                    reviewed.append(detail)
                if status == 'CompileError':
                    detail['reviewedIndividually'] = is_reviewed
                    compile_errors.append(detail)
                elif status != 'Killed' and not is_reviewed:
                    unresolved.append(detail)
    review = []
    if exit_code != 0 and not (exit_code == 2 and completed_threshold_failure and reviewed):
        review.append(f'Stryker exited with code {exit_code}; a partial or failed run cannot satisfy the gate.')
    if not inputs_unchanged:
        review.append('Campaign inputs changed; this report is not final-source evidence.')
    if audit_error:
        review.append(audit_error)
    review.extend(review_errors or [])
    if report is None:
        review.append('Exactly one new mutation-report.json is required inside this run directory.')
    if mutants_total == 0:
        review.append('No mutants were reported; instrumentation has not been established.')
    if not audit:
        review.append('No instrumentation/source reconciliation is available.')
    for key in ('notRepresented', 'unexpectedPaths', 'duplicatePaths', 'sourceMismatches'):
        remaining = set(audit.get(key, [])) - reviewed_sources if key == 'notRepresented' else audit.get(key)
        if remaining:
            review.append(f'Instrumentation {key} requires explicit review; see instrumentationAudit.')
    if unresolved:
        review.append('Unresolved mutants remain; score alone does not satisfy the configured zero-survivor/coverage/timeout gate.')
    if any(not entry['reviewedIndividually'] for entry in compile_errors):
        review.append('CompileError mutants require individual non-viability review; safe-mode collateral is not automatically accepted.')
    scored = sum(counts[status] for status in ('Killed', 'Survived', 'NoCoverage', 'Timeout'))
    return {
        'format': 'exportaramus-mutation-campaign', 'schemaVersion': '1.0',
        'status': 'passed' if not review else 'failed', 'startedAtUtc': before['startedAtUtc'], 'finishedAtUtc': timestamp(),
        'exitCode': exit_code, 'sourceSha256': before['sourceSha256'], 'afterSourceSha256': after_source_sha,
        'inputsUnchanged': inputs_unchanged, 'changedInputs': changed,
        'report': str(report_path) if report_path else None, 'statusCounts': counts, 'totalMutants': mutants_total,
        'rawMutationScore': 100 * (counts['Killed'] + counts['Timeout']) / scored if scored else None,
        'reviewedMutants': reviewed, 'reviewedSources': sorted(reviewed_sources),
        'rawUnresolvedMutants': raw_unresolved,
        'unresolvedMutants': unresolved, 'compileErrorMutants': compile_errors, 'instrumentationAudit': audit,
        'reviewRequired': review, 'gatePassed': not review,
    }


def finish(run, exit_code, ledger_path=None):
    manifest_path = run / 'input-manifest.json'
    before = json.loads(manifest_path.read_bytes())
    candidates = list((run / 'combined').glob('mutation-report.json')) if (run / 'constituent-campaigns.json').exists() else list(run.rglob('mutation-report.json'))
    report_path = candidates[0] if len(candidates) == 1 else None
    report = None
    audit = None
    error = None
    after_source_sha = None
    ledger = None
    constituents = None
    accepted, reviewed_sources, review_errors = set(), set(), []
    try:
        after_source_sha = json.loads((run / 'source-after.json').read_bytes())['sha256']
        if report_path:
            report = json.loads(report_path.read_bytes())
        if (run / 'constituent-campaigns.json').exists():
            constituents = validate_combined_evidence(run)
        audit = audit_scope(run, 'after', report_path, complete=before.get('schemaVersion') == '2.0')
        if ledger_path:
            ledger = json.loads(ledger_path.read_bytes())
            accepted, reviewed_sources, review_errors = reconcile(ledger, before, digest(report_path), audit, report, ROOT)
    except (OSError, ValueError, RuntimeError, KeyError, TypeError) as exception:
        error = f'{type(exception).__name__}: {exception}'
    console = (run / 'console.log').read_text(encoding='utf-8-sig', errors='replace') if (run / 'console.log').exists() else ''
    completed_threshold_failure = ('The final mutation score is ' in console and 'The mutation score is lower than the configured break threshold of 100%' in console)
    summary = summarize(before, input_hashes(ROOT, legacy=before.get('schemaVersion') != '2.0'), audit, report, exit_code, report_path, error, after_source_sha,
                        accepted, reviewed_sources, review_errors, completed_threshold_failure)
    if constituents is not None:
        summary['constituentCampaigns'] = constituents
        summary['constituentManifestSha256'] = digest(run / 'constituent-campaigns.json')
    summary['inputManifestSha256'] = digest(manifest_path)
    summary['reportSha256'] = digest(report_path) if report_path else None
    summary['linkedSourceCount'] = before['linkedSourceCount']
    summary['reviewLedger'] = str(ledger_path) if ledger_path else None
    summary['reviewLedgerSha256'] = digest(ledger_path) if ledger_path and ledger_path.is_file() else None
    review_evidence = {}
    if constituents is not None:
        review_evidence[str(run / 'constituent-campaigns.json')] = digest(run / 'constituent-campaigns.json')
        for item in constituents['campaigns']:
            for key in ('report', 'console'):
                review_evidence[str(run / item[key])] = item[key + 'Sha256']
    if isinstance(ledger, dict):
        for key in ('entries', 'sourceReviews'):
            for entry in ledger.get(key, []) if isinstance(ledger.get(key, []), list) else []:
                if not isinstance(entry, dict):
                    continue
                for item in entry.get('evidence', []) if isinstance(entry.get('evidence', []), list) else []:
                    if isinstance(item, dict) and isinstance(item.get('path'), str) and isinstance(item.get('sha256'), str):
                        review_evidence[str((ROOT / item['path']).resolve())] = item['sha256']
                if entry.get('classification') == 'IndependentlyKilled' and (entry.get('file'), entry.get('id')) in accepted:
                    try:
                        proof = entry['independentExecution']
                        proof_path = (ROOT / proof['summary']).resolve()
                        review_evidence[str(proof_path)] = proof['sha256']
                        independent = json.loads(proof_path.read_bytes())
                        review_evidence[str((ROOT / independent['inventory']).resolve())] = independent['inventorySha256']
                        for value in independent['baseline'].values():
                            for name in ('trx', 'log'):
                                if name in value:
                                    review_evidence[str((ROOT / value[name]).resolve())] = value[name + 'Sha256']
                        for result in independent['results']:
                            if result['file'] != entry['file'] or result['mutant']['id'] != entry['id']:
                                continue
                            review_evidence[str((ROOT / result['mutatedSource']).resolve())] = result['mutatedSourceSha256']
                            for stage in ('build', 'tests'):
                                for name in ('trx', 'log'):
                                    if name in result[stage]:
                                        review_evidence[str((ROOT / result[stage][name]).resolve())] = result[stage][name + 'Sha256']
                    except (KeyError, TypeError, ValueError, OSError) as exception:
                        summary['reviewRequired'].append(f'Independent execution artifact bindings failed: {exception}')
                        summary['status'], summary['gatePassed'] = 'failed', False
    summary['reviewEvidence'] = [{'path': path, 'sha256': sha} for path, sha in sorted(review_evidence.items())]
    if not (run / 'raw-mutation-campaign-summary.json').exists():
        write_json(run / 'raw-mutation-campaign-summary.json', summary)
    write_json(run / 'mutation-campaign-summary.json', summary)
    write_json(run / 'unresolved-mutants.json', summary['unresolvedMutants'])
    print(json.dumps({key: summary[key] for key in ('status', 'totalMutants', 'statusCounts', 'inputsUnchanged', 'gatePassed')}))
    return 0 if summary['gatePassed'] else 1


def review_template(run):
    before = json.loads((run / 'input-manifest.json').read_bytes())
    reports = list((run / 'combined').glob('mutation-report.json')) if (run / 'constituent-campaigns.json').exists() else list(run.rglob('mutation-report.json'))
    if len(reports) != 1:
        raise ValueError('Exactly one report is required to prepare an exact review template.')
    report_path = reports[0]
    report = json.loads(report_path.read_bytes())
    scope = audit_scope(run, 'review-template', report_path, complete=before.get('schemaVersion') == '2.0')
    audit = scope['mutationInstrumentationAudit']
    mapping = {value['reportPath']: key for key, value in audit['instrumentedFiles'].items()}
    entries = []
    for path, file in report['files'].items():
        if path not in mapping:
            continue
        for mutant in file.get('mutants', []):
            if mutant['status'] != 'Killed':
                entry = {key: mutant.get(key) for key in ('id', 'status', 'mutatorName', 'location', 'replacement')}
                entry.update(file=mapping[path], sourceSha256=scope['handwrittenSourceHashes'][mapping[path]],
                             classification='Unreviewed', reason='', evidence=[])
                entries.append(entry)
    value = {'schemaVersion': '1.0', 'sourceSha256': before['sourceSha256'], 'reportSha256': digest(report_path),
             'reviewer': '', 'entries': entries,
             'sourceReviews': [{'file': path, 'sourceSha256': scope['handwrittenSourceHashes'][path], 'classification': 'Unreviewed', 'reason': '', 'evidence': []}
                               for path in audit['notRepresented']]}
    destination = run / 'review-ledger-template.json'
    if destination.exists():
        raise ValueError('Review template already exists; it is never overwritten.')
    write_json(destination, value)
    print(str(destination))
    return 0



def validate_combined_evidence(run):
    """The canonical report must be exactly the bound raw reports, with only IDs/path keys normalized."""
    import copy
    manifest = json.loads((run / 'constituent-campaigns.json').read_bytes())
    campaigns = manifest['campaigns']
    names = {'testable', 'consumerkit11'} if manifest.get('schemaVersion') == '1.0' else (
        {item['name'] for item in campaign_definitions(ROOT)} if manifest.get('schemaVersion') == '2.0' else set())
    if not names or len(campaigns) != len(names) or {item['name'] for item in campaigns} != names:
        raise ValueError('Every declared constituent campaign must remain bound.')
    expected = {}
    global_ids = set()
    for item in campaigns:
        for key in ('report', 'console'):
            path = (run / item[key]).resolve()
            if not path.is_relative_to(run.resolve()) or digest(path) != item[key + 'Sha256']:
                raise ValueError('Constituent evidence hash/path mismatch: ' + item[key])
        validate_completion(item, (run / item['console']).read_text(encoding='utf-8-sig', errors='replace'))
        raw = json.loads((run / item['report']).read_bytes())
        if set(item['sourcePathMap']) != set(raw['files']):
            raise ValueError('Every raw source must appear exactly once in the source path map.')
        identities = {(identity['rawReportPath'], identity['rawId']): identity for identity in item['mutantIdentityMap']}
        if len(identities) != len(item['mutantIdentityMap']):
            raise ValueError('Duplicate raw mutant identity mapping.')
        used = set()
        for raw_path, value in raw['files'].items():
            canonical = item['sourcePathMap'][raw_path]
            if canonical in expected:
                raise ValueError('Duplicate canonical source in constituent reports.')
            normalized = copy.deepcopy(value)
            for mutant in normalized.get('mutants', []):
                key = (raw_path, mutant['id'])
                identity = identities[key]
                if identity['file'] != canonical or identity['combinedId'] in global_ids:
                    raise ValueError('Invalid or duplicate canonical mutant identity.')
                mutant['id'] = identity['combinedId']
                global_ids.add(mutant['id'])
                used.add(key)
            expected[canonical] = normalized
        if used != set(identities):
            raise ValueError('Unused mutant identities cannot be accepted.')
    combined = json.loads((run / 'combined/mutation-report.json').read_bytes())
    if combined['files'] != expected:
        raise ValueError('Canonical mutation report differs from the complete raw report union.')
    return manifest


def validate_completion(item, console):
    if type(item.get('exitCode')) is not int or item['exitCode'] not in (0, 2) or 'The final mutation score is ' not in console:
        raise ValueError('Incomplete or failed constituent campaign: ' + item['name'])
    if item['exitCode'] == 2 and 'The mutation score is lower than the configured break threshold of 100%' not in console:
        raise ValueError('Unrecognized threshold failure: ' + item['name'])


def combine(run):
    """Preserve raw assembly reports and reconcile their complete union without dropping mutants."""
    import importlib.util
    spec = importlib.util.spec_from_file_location('quality_scope', ROOT / 'tools/contract-quality-scope.py')
    quality = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(quality)
    campaigns = json.loads((run / 'executions.json').read_bytes())
    expected = {item['name']: item['project'] for item in campaign_definitions(ROOT)}
    if {item['name'] for item in campaigns} != set(expected) or len(campaigns) != len(expected):
        raise ValueError('Exactly all configured assembly campaigns are required.')
    combined, bindings = None, []
    next_id = 0
    for item in campaigns:
        project = expected[item['name']]
        reports = list((run / item['name']).rglob('mutation-report.json'))
        if len(reports) != 1:
            raise ValueError('Exactly one raw report is required for ' + item['name'])
        report_path = reports[0]
        report = json.loads(report_path.read_bytes())
        console_path = run / item['name'] / 'console.log'
        console = console_path.read_text(encoding='utf-8-sig', errors='replace')
        validate_completion(item, console)
        if combined is None:
            combined = {key: value for key, value in report.items() if key != 'files'}
            combined['files'] = {}
        sources = quality.compile_sources(project)
        links = {}
        import xml.etree.ElementTree as ET
        for node in ET.parse(project).findall('.//Compile'):
            if 'Link' in node.attrib:
                links[node.attrib['Link'].replace('\\','/')] = (project.parent / node.attrib['Include']).resolve()
        identities = []
        source_paths = {}
        seen_ids = set()
        for path, value in report['files'].items():
            normalized = quality.normalize_report_path(path)
            matches = [source for source in sources if normalized == quality.relative(source) or normalized.endswith('/' + quality.relative(source))]
            if not matches:
                resolved = (project.parent / normalized).resolve()
                matches = [source for source in sources if source == resolved]
            if not matches:
                matches = [target for link, target in links.items() if normalized == link or normalized.endswith('/' + link)]
            if len(matches) != 1:
                raise ValueError('Unresolved or ambiguous raw report source: ' + path)
            canonical = quality.relative(matches[0])
            if canonical in combined['files']:
                raise ValueError('Duplicate instrumented source: ' + canonical)
            source_paths[path] = canonical
            for mutant in value.get('mutants', []):
                raw_id = mutant['id']
                if raw_id in seen_ids:
                    raise ValueError('Duplicate raw mutant ID: ' + raw_id)
                seen_ids.add(raw_id)
                mutant['id'] = str(next_id)
                identities.append({'file': canonical, 'rawReportPath': path, 'rawId': raw_id, 'combinedId': str(next_id)})
                next_id += 1
            combined['files'][canonical] = value
        bindings.append({**item, 'project': quality.relative(project), 'report': report_path.relative_to(run).as_posix(),
                         'reportSha256': digest(report_path), 'console': console_path.relative_to(run).as_posix(),
                         'consoleSha256': digest(console_path), 'mutantIdentityMap': identities, 'sourcePathMap': source_paths})
    (run / 'combined').mkdir(exist_ok=True)
    write_json(run / 'combined/mutation-report.json', combined)
    write_json(run / 'constituent-campaigns.json', {'schemaVersion': '2.0', 'campaigns': bindings})
    (run / 'console.log').write_text('\n'.join((run / item['console']).read_text(encoding='utf-8-sig') for item in bindings), encoding='utf-8')
    print(json.dumps({'combinedFiles': len(combined['files']), 'campaigns': len(bindings)}))
    return 0


def finish_repaired(bridge_path, ledger_path, output):
    from repair_bridge import validate_bridge
    repair = validate_bridge(bridge_path, ROOT)
    if output.exists() or not output.is_relative_to(ROOT / 'development'):
        raise ValueError('Repair finalization output must be a new directory under development.')
    run = repair['run']
    original = json.loads((run / 'mutation-campaign-summary.json').read_bytes())
    report_path = run / 'combined/mutation-report.json'
    report = json.loads(report_path.read_bytes())
    ledger = json.loads(ledger_path.read_bytes())
    accepted, reviewed_sources, errors = reconcile(ledger, repair['before'], repair['reportHash'], repair['scope'], report, ROOT, repair)
    summary = summarize(repair['before'], repair['before']['files'], repair['scope'], report, original['exitCode'],
                        report_path, after_source_sha=repair['before']['sourceSha256'], accepted=accepted,
                        reviewed_sources=reviewed_sources, review_errors=errors, completed_threshold_failure=True)
    summary.update(format='exportaramus-repaired-mutation-campaign', schemaVersion='1.0',
                   evidenceStrategy='complete-original-campaign-plus-exact-strengthened-test-execution',
                   originalCampaignSourceSha256=repair['before']['sourceSha256'],
                   originalCampaignSummary=str(run / 'mutation-campaign-summary.json'),
                   sourceSha256=repair['repairedInventory']['sha256'], afterSourceSha256=repair['repairedInventory']['sha256'],
                   repairScopeVerified=True, repairBridge=str(bridge_path), repairBridgeSha256=repair['bridgeSha256'],
                   reportSha256=repair['reportHash'], reviewLedger=str(ledger_path), reviewLedgerSha256=digest(ledger_path))
    evidence = {}
    def bind(path, sha):
        path = str(Path(path).resolve())
        if digest(Path(path)) != sha or path in evidence and evidence[path] != sha:
            raise ValueError('Repair evidence changed or has conflicting bindings: ' + path)
        evidence[path] = sha
    for item in repair['evidence']:
        bind(item['path'],item['sha256'])
    bind(ledger_path,digest(ledger_path))
    for entry in ledger.get('entries', []) + ledger.get('sourceReviews', []):
        for item in entry.get('evidence', []):
            bind(ROOT / item['path'],item['sha256'])
        if entry.get('classification') != 'IndependentlyKilled' or (entry.get('file'),entry.get('id')) not in accepted:
            continue
        proof = entry['independentExecution']
        path = (ROOT / proof['summary']).resolve()
        bind(path,proof['sha256'])
        independent = json.loads(path.read_bytes())
        bind(ROOT / independent['inventory'],independent['inventorySha256'])
        bind(ROOT / independent['executor'],independent['executorSha256'])
        for stage in independent['baseline'].values():
            for name in ('trx','log'):
                if name in stage:
                    bind(ROOT / stage[name],stage[name+'Sha256'])
        for result in independent['results']:
            if result['file'] != entry['file'] or result['mutant']['id'] != entry['id']:
                continue
            bind(ROOT / result['mutatedSource'],result['mutatedSourceSha256'])
            for stage in ('build','tests'):
                for name in ('trx','log'):
                    if name in result[stage]:
                        bind(ROOT / result[stage][name],result[stage][name+'Sha256'])
    validate_bridge(bridge_path, ROOT)
    summary['reviewEvidence'] = [{'path':path,'sha256':sha} for path,sha in sorted(evidence.items())]
    output.mkdir(parents=True)
    write_json(output / 'mutation-campaign-summary.json',summary)
    write_json(output / 'unresolved-mutants.json',summary['unresolvedMutants'])
    print(json.dumps({key:summary[key] for key in ('status','totalMutants','statusCounts','gatePassed','sourceSha256')}))
    return 0 if summary['gatePassed'] else 1


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=('campaigns', 'prepare', 'finish', 'review-template', 'combine', 'finish-repaired', 'finish-staged'))
    parser.add_argument('--run-dir', type=Path)
    parser.add_argument('--repair-bridge', type=Path)
    parser.add_argument('--output-dir', type=Path)
    parser.add_argument('--runner', type=Path)
    parser.add_argument('--exit-code', type=int, default=-1)
    parser.add_argument('--review-ledger', type=Path)
    parser.add_argument('--stage-composition', type=Path)
    args = parser.parse_args()
    if args.mode == 'campaigns':
        print(json.dumps([{key: str(value) for key, value in item.items()} for item in campaign_definitions(ROOT)]))
        return 0
    if args.mode == 'finish-staged':
        if not args.stage_composition or not args.review_ledger or not args.output_dir:
            parser.error('finish-staged requires --stage-composition, --review-ledger, and --output-dir')
        from multi_stage_review import compose
        return compose(args.stage_composition.resolve(), args.review_ledger.resolve(), args.output_dir.resolve(), ROOT)
    if args.mode == 'finish-repaired':
        if not args.repair_bridge or not args.review_ledger or not args.output_dir:
            parser.error('finish-repaired requires --repair-bridge, --review-ledger, and --output-dir')
        return finish_repaired(args.repair_bridge.resolve(), args.review_ledger.resolve(), args.output_dir.resolve())
    if args.run_dir is None:
        parser.error('--run-dir is required outside repair finalization')
    run = args.run_dir.resolve()
    if args.mode == 'combine':
        return combine(run)
    if args.mode == 'prepare':
        if args.runner is None:
            parser.error('--runner is required for prepare')
        prepare(run, args.runner.resolve())
        return 0
    if args.mode == 'review-template':
        return review_template(run)
    return finish(run, args.exit_code, args.review_ledger.resolve() if args.review_ledger else None)


if __name__ == '__main__':
    raise SystemExit(main())
