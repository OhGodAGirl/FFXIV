"""Validate explicit, exact-artifact mutation reviews; never infer equivalence."""
from pathlib import Path
import hashlib
import json
import suite_binding
import xml.etree.ElementTree as ET

PLATFORM = 'reference/Compendium/1.0/CompendiumFileWatch.cs'
BLOCK_REASON = 'Removed by block already covered filter'
PLATFORM_REASON = 'Removed by exclude from code coverage filter'


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def evidence_valid(entry, root):
    if not isinstance(entry.get('reason'), str) or not entry['reason'].strip():
        return False
    evidence = entry.get('evidence', [])
    if not isinstance(evidence, list) or not evidence:
        return False
    for item in evidence:
        path = (root / item['path']).resolve()
        if not path.is_relative_to(root) or not path.is_file() or sha256(path) != item['sha256']:
            return False
    return True


def verified_artifact(root, path, expected):
    resolved = (root / path).resolve()
    if not resolved.is_relative_to(root) or not resolved.is_file() or sha256(resolved) != expected:
        raise ValueError('Independent execution artifact is missing, outside the repository, or changed.')
    return resolved


def execution_counts(root, execution, target='provider'):
    command = execution['command']
    if (len(command) < 3 or command[1] != 'test' or Path(command[2]).name != Path(suite_binding.TARGETS[target][1]).name
            or any('filter' in item.lower() or item in ('--list-tests', '--no-build') for item in command)):
        raise ValueError('Independent execution must rebuild and run the full configured test project without selection.')
    trx = verified_artifact(root, execution['trx'], execution['trxSha256'])
    verified_artifact(root, execution['log'], execution['logSha256'])
    document = ET.parse(trx)
    root_element = document.getroot()
    summaries = root_element.findall('.//{*}ResultSummary')
    if len(summaries) != 1 or summaries[0] not in list(root_element):
        raise ValueError('Independent execution requires exactly one direct ResultSummary.')
    summary = summaries[0]
    counters = summary.findall('{*}Counters')
    if len(counters) != 1 or len(root_element.findall('.//{*}Counters')) != 1:
        raise ValueError('Independent execution requires exactly one direct ResultSummary Counters element.')
    counters = counters[0]
    other_names = ('aborted', 'timeout', 'error', 'notRunnable', 'disconnected', 'inconclusive',
                   'passedButRunAborted', 'warning', 'completed', 'inProgress', 'pending')
    try:
        counts = {key: int(counters.attrib[key]) for key in ('total', 'executed', 'passed', 'failed', 'notExecuted')}
        other_counts = {key: int(counters.attrib[key]) for key in other_names}
    except (KeyError, ValueError) as exception:
        raise ValueError('Independent execution counters require all integer counts.') from exception
    if any(value < 0 for value in (*counts.values(), *other_counts.values())):
        raise ValueError('Independent execution counters cannot be negative.')
    if counts != execution['counts'] or counts['total'] != counts['executed'] or counts['notExecuted'] or counts['executed'] <= 0:
        raise ValueError('Independent execution contains missing, skipped, or inconsistent test cases.')
    outcomes = [row.attrib.get('outcome') for row in root_element.findall('.//{*}UnitTestResult')]
    if (len(outcomes) != counts['executed'] or outcomes.count('Passed') != counts['passed'] or outcomes.count('Failed') != counts['failed']
            or set(outcomes) - {'Passed', 'Failed'} or execution.get('timedOut')
            or any(other_counts.values())
            or summary.attrib.get('outcome') != ('Failed' if counts['failed'] else 'Completed')):
        raise ValueError('Independent test rows contain incomplete execution or disagree with aggregate counters.')
    return counts


def independently_killed(entry, before, report_hash, mutant, root, repair=None):
    proof = entry['independentExecution']
    path = verified_artifact(root, proof['summary'], proof['sha256'])
    summary = json.loads(path.read_bytes())
    expected_source = repair['repairedInventory']['sha256'] if repair else before['sourceSha256']
    expected_executor = repair['repairedPaths'].get('tools/individual-mutation.py') if repair else before['files'].get('tools/individual-mutation.py')
    if (summary['format'] != 'exportaramus-independent-mutation-shard' or summary['schemaVersion'] != ('1.1' if repair else '1.0')
            or summary['sourceSha256'] != expected_source or summary['reportSha256'] != report_hash
            or summary['executorSha256'] != expected_executor
            or not summary['restoredInputsUnchanged'] or summary['changedInputs']):
        raise ValueError('Independent execution does not match its exact inventory, report, executor, or restored inputs.')
    if repair:
        if summary.get('repairBridgeSha256') != repair['bridgeSha256'] or summary.get('originalSourceSha256') != before['sourceSha256']:
            raise ValueError('Independent execution is not bound to this reviewed repair bridge.')
        verified_artifact(root, summary['repairBridge'], summary['repairBridgeSha256'])
        verified_artifact(root, summary['inventory'], summary['inventorySha256'])
        if summary['inventorySha256'] != repair['bridge']['repairedInventory']['sha256']:
            raise ValueError('Independent repaired inventory differs from the bridge.')
    matches = [value for value in summary['results'] if value['file'] == entry['file'] and value['mutant']['id'] == entry['id']]
    if len(matches) != 1:
        raise ValueError('Independent execution must identify this exact mutant once.')
    result = matches[0]
    if (result['outcome'] != 'Killed' or result['sourceSha256'] != entry['sourceSha256']
            or any(result['mutant'].get(key) != mutant.get(key) for key in ('id', 'status', 'statusReason', 'mutatorName', 'location', 'replacement'))
            or result['build']['exitCode'] != 0 or summary['baseline']['build']['exitCode'] != 0
            or result['build'].get('timedOut') or summary['baseline']['build'].get('timedOut')):
        raise ValueError('Independent execution did not compile and kill this exact replacement.')
    source = verified_artifact(root, entry['file'], entry['sourceSha256']).read_bytes().decode('utf-8')
    lines = source.splitlines(keepends=True)
    def offset(position):
        line, column = position['line'], position['column']
        if not 1 <= line <= len(lines) or not 1 <= column <= len(lines[line - 1].encode('utf-16-le')) // 2 + 1:
            raise ValueError('Independent mutation location is invalid.')
        return sum(len(value.encode('utf-16-le')) for value in lines[:line - 1]) + (column - 1) * 2
    start, end = offset(mutant['location']['start']), offset(mutant['location']['end'])
    if end <= start:
        raise ValueError('Independent mutation span is empty or reversed.')
    encoded = source.encode('utf-16-le')
    expected = (encoded[:start] + mutant['replacement'].encode('utf-16-le') + encoded[end:]).decode('utf-16-le').encode('utf-8')
    replacement = verified_artifact(root, result['mutatedSource'], result['mutatedSourceSha256'])
    if replacement.read_bytes() != expected:
        raise ValueError('Independent execution did not use the exact source replacement.')
    target = suite_binding.verify_summary(summary, repair['repairedPaths'] if repair else before['files'], [entry['file']])
    baseline = execution_counts(root, summary['baseline']['tests'], target)
    actual = execution_counts(root, result['tests'], target)
    from repair_bridge import case_names
    if target != 'provider' and case_names(Path(summary['baseline']['tests']['trx']), passing=True) != case_names(Path(result['tests']['trx'])):
        raise ValueError('Baseline and mutant test identities differ')
    if (summary['baseline']['tests']['exitCode'] != 0 or baseline['passed'] != baseline['executed'] or baseline['failed']
            or result['tests']['exitCode'] != 1 or actual['failed'] <= 0 or actual['executed'] != baseline['executed']
            or actual['passed'] + actual['failed'] != actual['executed']):
        raise ValueError('Independent test results do not prove a complete passing baseline and a complete failing mutant run.')
    if repair:
        from repair_bridge import case_names, preserves_cases, stage_cases
        baseline_cases = case_names(Path(summary['baseline']['tests']['trx']), passing=True)
        original_cases, repaired_cases = stage_cases(repair, target)
        if not preserves_cases(original_cases, baseline_cases) or baseline_cases != repaired_cases or case_names(Path(result['tests']['trx'])) != baseline_cases:
            raise ValueError('Full original/repaired/mutant test cases do not reconcile.')
    return True


def reconcile(ledger, before, report_hash, scope, report, root, repair=None):
    accepted = set()
    reviewed_sources = set()
    errors = []
    if ledger is None:
        return accepted, reviewed_sources, errors
    if (ledger.get('schemaVersion') != '1.0' or ledger.get('sourceSha256') != before['sourceSha256']
            or ledger.get('reportSha256') != report_hash or not ledger.get('reviewer')):
        return accepted, reviewed_sources, ['Review ledger identity does not match the exact source/report or lacks a reviewer.']
    audit = scope.get('mutationInstrumentationAudit', {})
    mapping = {value['reportPath']: key for key, value in audit.get('instrumentedFiles', {}).items()}
    sources = scope.get('instrumentationSourceHashes', scope.get('handwrittenSourceHashes', {}))
    actual = {}
    for path, file in report.get('files', {}).items():
        if path not in mapping:
            continue
        for mutant in file.get('mutants', []):
            actual[(mapping[path], mutant.get('id'))] = mutant
    seen = set()
    for entry in ledger.get('entries', []):
        try:
            key = (entry['file'], entry['id'])
            mutant = actual[key]
            exact = all(entry.get(field) == mutant.get(field) for field in ('status', 'location', 'replacement', 'mutatorName'))
            if key in seen or not exact or entry.get('sourceSha256') != sources[key[0]] or not evidence_valid(entry, root):
                raise ValueError('Duplicate, mismatched or unsupported exact review entry.')
            seen.add(key)
            kind = entry.get('classification')
            status = mutant['status']
            allowed = (kind == 'Equivalent' and status == 'Survived'
                       or kind == 'IndividuallyNonViable' and (status == 'CompileError' or status == 'Ignored' and mutant.get('statusReason') == BLOCK_REASON)
                       or kind == 'RedundantBlock' and status == 'Ignored' and mutant.get('statusReason') == BLOCK_REASON
                       or kind == 'ExistingPlatformBinding' and status == 'Ignored' and key[0] == PLATFORM and mutant.get('statusReason') == PLATFORM_REASON)
            if kind == 'IndependentlyKilled' and (status == 'CompileError' or status == 'Ignored' and mutant.get('statusReason') == BLOCK_REASON
                    or repair is not None and status in ('Survived', 'Timeout')):
                allowed = independently_killed(entry, before, report_hash, mutant, root, repair)
            if not allowed:
                raise ValueError('Classification cannot resolve this raw status or source.')
            for supporting in entry.get('supportingKilledMutantIds', []):
                if actual.get((key[0], supporting), {}).get('status') != 'Killed':
                    raise ValueError('Supporting mutant is not killed in this exact source/report.')
            accepted.add(key)
        except (KeyError, TypeError, ValueError, OSError, ET.ParseError) as exception:
            errors.append(f'Invalid review entry: {exception}')
    for entry in ledger.get('sourceReviews', []):
        try:
            path = entry['file']
            if (path in reviewed_sources or path not in audit.get('notRepresented', [])
                    or entry.get('classification') != 'NoExecutableMutants' or entry.get('sourceSha256') != sources[path]
                    or not evidence_valid(entry, root)):
                raise ValueError('Missing-file review does not match exact unrepresented source/evidence.')
            reviewed_sources.add(path)
        except (KeyError, TypeError, ValueError, OSError) as exception:
            errors.append(f'Invalid source review: {exception}')
    return accepted, reviewed_sources, errors
