"""Execute exact report replacements independently against all configured tests.

This produces supplemental evidence only. It never approves a mutation review.
Each shard owns a fresh, hash-verified copy of every source inventory input.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import suite_binding
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
BLOCK_REASON = 'Removed by block already covered filter'


def resolve_dotnet(command):
    executable = shutil.which(str(command))
    if executable is None:
        raise ValueError('The .NET executable was not found. Put dotnet on PATH or pass --dotnet with its executable path.')
    return Path(executable).resolve(strict=True)


def sdk_compiler(dotnet, version, cwd):
    installed = subprocess.check_output([str(dotnet), '--list-sdks'], cwd=cwd, text=True)
    roots = [Path(match.group(1)) for line in installed.splitlines()
             if (match := re.fullmatch(re.escape(version) + r'\s+\[(.+)\]', line.strip()))]
    if len(roots) != 1:
        raise ValueError('The selected SDK must have exactly one installation in dotnet --list-sdks.')
    compiler = roots[0] / version / 'Roslyn/bincore/csc.dll'
    if not compiler.is_file():
        raise ValueError('The selected SDK compiler is missing: ' + str(compiler))
    return compiler.resolve()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def artifact(path):
    return {'path': str(path.resolve()), 'sha256': digest(path.read_bytes())}


def replace_span(source, location, replacement):
    lines = source.splitlines(keepends=True)

    def offset(position):
        line, column = position['line'], position['column']
        if not 1 <= line <= len(lines) or not 1 <= column <= len(lines[line - 1].encode('utf-16-le')) // 2 + 1:
            raise ValueError('Invalid UTF-16 report location.')
        return sum(len(value.encode('utf-16-le')) for value in lines[:line - 1]) + (column - 1) * 2

    start, end = offset(location['start']), offset(location['end'])
    if end <= start:
        raise ValueError('Empty or reversed report span.')
    raw = source.encode('utf-16-le')
    return (raw[:start] + replacement.encode('utf-16-le') + raw[end:]).decode('utf-16-le'), raw[start:end].decode('utf-16-le')


def execute(command, cwd, log, timeout):
    started = time.monotonic()
    timed_out = False
    with log.open('w', encoding='utf-8') as output:
        process = subprocess.Popen([str(part) for part in command], cwd=cwd, stdout=output, stderr=subprocess.STDOUT)
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            if os.name == 'nt':
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], stdout=output, stderr=subprocess.STDOUT, timeout=15, check=False)
            else:
                process.kill()
            process.wait(timeout=15)
    return {'command': [str(part) for part in command], 'workingDirectory': str(cwd),
            'exitCode': process.returncode, 'timedOut': timed_out, 'processId': process.pid,
            'durationSeconds': time.monotonic() - started, 'log': str(log.resolve()),
            'logSha256': digest(log.read_bytes())}


def read_trx(directory):
    reports = list(directory.rglob('*.trx'))
    if len(reports) != 1:
        raise ValueError('Exactly one fresh TRX is required.')
    root = ET.parse(reports[0]).getroot()
    summaries = root.findall('.//{*}ResultSummary')
    if len(summaries) != 1 or summaries[0] not in list(root):
        raise ValueError('Exactly one direct TRX ResultSummary is required.')
    summary = summaries[0]
    counters = summary.findall('{*}Counters')
    if len(counters) != 1 or len(root.findall('.//{*}Counters')) != 1:
        raise ValueError('Exactly one direct ResultSummary Counters element is required.')
    counters = counters[0]
    names = ('total', 'executed', 'passed', 'failed', 'notExecuted')
    other_names = ('aborted', 'timeout', 'error', 'notRunnable', 'disconnected', 'inconclusive',
                   'passedButRunAborted', 'warning', 'completed', 'inProgress', 'pending')
    try:
        counts = {name: int(counters.attrib[name]) for name in names}
        other_counts = {name: int(counters.attrib[name]) for name in other_names}
    except (KeyError, ValueError) as exception:
        raise ValueError('TRX counters must contain all required integer counts.') from exception
    if any(value < 0 for value in (*counts.values(), *other_counts.values())):
        raise ValueError('TRX counters cannot be negative.')
    result_rows = root.findall('.//{*}UnitTestResult')
    return {'trx': str(reports[0].resolve()), 'trxSha256': digest(reports[0].read_bytes()),
            'counts': counts, 'otherCounts': other_counts, 'summaryOutcome': summary.attrib.get('outcome'),
            'resultRows': len(result_rows),
            'outcomes': {outcome: sum(row.attrib.get('outcome') == outcome for row in result_rows)
                         for outcome in sorted({row.attrib.get('outcome', '') for row in result_rows})}}


def complete_counts(tests, expected=None):
    counts = tests.get('counts', {})
    total = counts.get('total', 0)
    names = ('total', 'executed', 'passed', 'failed', 'notExecuted')
    return (all(type(counts.get(name)) is int and counts[name] >= 0 for name in names)
            and total > 0 and counts.get('executed') == total and tests.get('resultRows') == total
            and counts.get('passed', 0) + counts.get('failed', 0) == total
            and counts.get('notExecuted', 0) == 0 and not any(tests.get('otherCounts', {}).values())
            and set(tests.get('outcomes', {})).issubset({'Passed', 'Failed'})
            and tests.get('outcomes', {}).get('Passed', 0) == counts['passed']
            and tests.get('outcomes', {}).get('Failed', 0) == counts['failed']
            and tests.get('summaryOutcome') == ('Failed' if counts['failed'] else 'Completed')
            and (expected is None or total == expected))


def classify(build, tests, expected):
    if build['timedOut'] or tests is not None and tests['timedOut']:
        return 'Timeout'
    if build['exitCode'] != 0 or tests is None or not complete_counts(tests, expected):
        return 'Error'
    if tests['exitCode'] == 1 and tests['counts']['failed'] > 0:
        return 'Killed'
    if tests['exitCode'] == 0 and tests['counts']['failed'] == 0:
        return 'Survived'
    return 'Error'


def selected_mutants(report, mapping, paths, requested_ids, repair_mode=False):
    selected, seen = [], set()
    for report_path, entry in report['files'].items():
        for mutant in entry.get('mutants', []):
            key = mutant['id']
            if key in seen or not re.fullmatch(r'[0-9]+', key):
                raise ValueError('Report IDs must be globally unique numeric strings.')
            seen.add(key)
            eligible = (mutant['status'] == 'CompileError' or mutant['status'] == 'Ignored' and mutant.get('statusReason') == BLOCK_REASON
                        or repair_mode and mutant['status'] in ('Survived', 'Timeout', 'NoCoverage'))
            if not eligible or requested_ids is not None and key not in requested_ids:
                continue
            if report_path not in mapping:
                raise ValueError('Unreconciled report source: ' + report_path)
            path = mapping[report_path]
            if digest(entry['source'].encode('utf-8')) != paths.get(path):
                raise ValueError('Report source differs from frozen inventory: ' + path)
            selected.append((path, entry['source'], mutant))
    selected.sort(key=lambda row: int(row[2]['id']))
    if requested_ids is not None and {row[2]['id'] for row in selected} != requested_ids:
        raise ValueError('An explicit ID is missing or not an eligible raw CompileError/ignored-block mutant.')
    if not selected:
        raise ValueError('No eligible mutants selected.')
    return selected


def run_tests(command, snapshot, directory, timeout):
    directory.mkdir()
    result = execute(command + ['--results-directory', directory, '--logger', 'trx;LogFileName=tests.trx'],
                     snapshot, directory / 'tests.log', timeout)
    try:
        result.update(read_trx(directory))
    except (ValueError, ET.ParseError, OSError) as exception:
        result['trxError'] = str(exception)
    return result


def select_shard(rows, index, count):
    if not 1 <= count <= 64 or not 0 <= index < count:
        raise ValueError('Shard index must be zero-based within shard count1..64.')
    return rows[index::count]


def require_provider_mutants(rows):
    for path, _, _ in rows:
        if path.startswith('src/Contracts/1.2/Consumer/') or path == 'examples/CollectionExample/InventoryItemReader.cs':
            raise ValueError('This independent executor is provider-only; consumer client/reader mutants require their complete suites in a fresh current campaign.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet', help='Executable name on PATH or explicit .NET host path.')
    parser.add_argument('--target', choices=tuple(suite_binding.TARGETS), default='provider')
    parser.add_argument('--root', required=True, type=Path)
    parser.add_argument('--inventory', required=True, type=Path)
    parser.add_argument('--report', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--repair-bridge', type=Path, help='Explicit reviewed original-campaign to repaired-test bridge.')
    parser.add_argument('--current-campaign', type=Path, help='Completed same-production campaign binding with full baseline and coverage.')
    parser.add_argument('--ids', help='Comma-separated exact eligible report IDs; default selects every CompileError and ignored block.')
    parser.add_argument('--expected-test-count', type=int, required=True, help='Exact executed count from the current complete configured suite, bound by the same frozen inputs.')
    parser.add_argument('--shard-index', type=int, default=0)
    parser.add_argument('--shard-count', type=int, default=1)
    parser.add_argument('--build-timeout-seconds', type=int, default=120)
    parser.add_argument('--test-timeout-seconds', type=int, default=120)
    args = parser.parse_args()
    dotnet = resolve_dotnet(args.dotnet)
    root, output = args.root.resolve(), args.output.resolve()
    if output.exists() or not output.is_relative_to(root / 'development'):
        raise ValueError('Output must be a new directory strictly beneath root/development.')
    if not 1 <= args.shard_count <= 64 or not 0 <= args.shard_index < args.shard_count:
        raise ValueError('Shard index must be zero-based within shard count1..64.')
    if not 1 <= args.build_timeout_seconds <= 600 or not 1 <= args.test_timeout_seconds <= 600:
        raise ValueError('Build and test bounds must be between1 and600 seconds.')
    if args.expected_test_count < 1:
        raise ValueError('Expected configured test count must be positive.')
    if args.repair_bridge and args.current_campaign:
        raise ValueError('Choose either historical repair or current campaign binding.')
    repair = None
    current = None
    if args.repair_bridge:
        from repair_bridge import validate_bridge, case_names, preserves_cases
        repair = validate_bridge(args.repair_bridge.resolve(), root)
    inventory_bytes, report_bytes = args.inventory.read_bytes(), args.report.read_bytes()
    inventory, report = json.loads(inventory_bytes), json.loads(report_bytes)
    if repair and (digest(report_bytes) != repair['reportHash'] or inventory['sha256'] != repair['repairedInventory']['sha256'] or digest(inventory_bytes) != repair['bridge']['repairedInventory']['sha256']):
        raise ValueError('Executor inputs do not match the repair bridge.')
    if args.current_campaign:
        from current_campaign import validate_current_campaign, validate_selection, validate_cases
        current = validate_current_campaign(args.current_campaign, root, args.inventory, args.report, args.target)
    paths = {entry['path']: entry['sha256'] for entry in inventory['files']}
    if args.target != 'provider' and paths.get('tools/suite_binding.py') != digest(Path(suite_binding.__file__).read_bytes()):
        raise ValueError('Suite validator must match frozen inventory')
    if len(paths) != len(inventory['files']):
        raise ValueError('Duplicate source inventory paths.')
    if paths.get('tools/individual-mutation.py') != digest(Path(__file__).read_bytes()):
        raise ValueError('Executor bytes must match the frozen inventory tool.')
    output.mkdir(parents=True)
    scope_result = execute([sys.executable, root / 'tools/contract-quality-scope.py', '--mutation-report', args.report.resolve(),
                            '--output-dir', output / 'scope'], root, output / 'scope.log', args.build_timeout_seconds)
    if scope_result['exitCode'] or scope_result['timedOut']:
        raise RuntimeError('Shared source/link reconciliation failed.')
    scope = json.loads((output / 'scope/coverage-manifest.json').read_bytes())
    mapping = {value['reportPath']: path for path, value in scope['mutationInstrumentationAudit']['instrumentedFiles'].items()}
    if args.target != 'provider':
        preliminary_binding = suite_binding.evaluate(root, args.target, dotnet, paths)
        mapping = suite_binding.report_mapping(preliminary_binding, report, paths)
    selected = selected_mutants(report, mapping, paths, set(args.ids.split(',')) if args.ids else None, repair is not None or current is not None)
    if current:
        validate_selection(current, selected)
    if args.target == 'provider':
        require_provider_mutants(selected)
    shard = select_shard(selected, args.shard_index, args.shard_count)
    if not shard:
        raise ValueError('This shard is empty; choose fewer shards.')
    snapshot = output / 'input'
    for relative, expected in paths.items():
        source, destination = (root / relative).resolve(), (snapshot / relative).resolve()
        if not source.is_relative_to(root) or not destination.is_relative_to(snapshot):
            raise ValueError('Source inventory path escapes approved roots.')
        content = source.read_bytes()
        if digest(content) != expected:
            raise ValueError('Frozen input changed: ' + relative)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(content)
        if digest(destination.read_bytes()) != expected:
            raise ValueError('Copied input hash differs: ' + relative)
    project = snapshot / suite_binding.TARGETS[args.target][1]
    # Run every test in the selected canonical suite without filtering.
    build_command = suite_binding.command(dotnet, 'build', snapshot, args.target)
    test_command = suite_binding.command(dotnet, 'test', snapshot, args.target)
    if args.target == 'provider':
        suite_binding.validate_coverage_only_settings(snapshot)
    mutant_test_command = suite_binding.command(dotnet, 'test', snapshot, args.target, phase='mutant')
    baseline = {'restore': execute([dotnet, 'restore', project, '--locked-mode', '--nologo'], snapshot,
                                    output / 'restore.log', args.build_timeout_seconds)}
    if baseline['restore']['exitCode'] or baseline['restore']['timedOut']:
        save(output / 'baseline.json', baseline)
        raise RuntimeError('Locked baseline restore failed.')
    binding = None
    if args.target != 'provider':
        binding = suite_binding.evaluate(snapshot, args.target, dotnet, paths)
        if not {row[0] for row in selected} <= set(binding['productionSources']):
            raise ValueError('Selected mutations do not belong to the canonical target')
    baseline['build'] = execute(build_command, snapshot, output / 'baseline-build.log', args.build_timeout_seconds)
    if baseline['build']['exitCode'] or baseline['build']['timedOut']:
        save(output / 'baseline.json', baseline)
        raise RuntimeError('Original baseline build failed.')
    baseline['tests'] = run_tests(test_command, snapshot, output / 'baseline-tests', args.test_timeout_seconds)
    save(output / 'baseline.json', baseline)
    if classify(baseline['build'], baseline['tests'], args.expected_test_count) != 'Survived':
        raise RuntimeError('All configured baseline tests must pass with zero skipped/aborted/missing cases.')
    from repair_bridge import case_names
    baseline_cases = case_names(Path(baseline['tests']['trx']), passing=True)
    if current:
        validate_cases(current, baseline_cases)
    if repair:
        from repair_bridge import stage_cases
        original_cases, repaired_cases = stage_cases(repair, args.target)
        if not preserves_cases(original_cases, baseline_cases) or baseline_cases != repaired_cases:
            raise RuntimeError('The repaired full suite removed original test cases.')
    sdk = subprocess.check_output([str(dotnet), '--version'], cwd=snapshot, text=True).strip()
    summary = {'format': 'exportaramus-independent-mutation-shard', 'schemaVersion': '1.0',
               'testCoveragePolicy': 'baseline-only-v1',
               'sourceSha256': inventory['sha256'], 'inventory': str(args.inventory.resolve()), 'inventorySha256': digest(inventory_bytes),
               'report': str(args.report.resolve()), 'reportSha256': digest(report_bytes),
               'executor': str(Path(__file__).resolve()), 'executorSha256': digest(Path(__file__).read_bytes()),
               'snapshot': str(snapshot), 'copiedInputs': len(paths), 'baseline': baseline, 'expectedTestCount': args.expected_test_count,
               'shard': {'index': args.shard_index, 'count': args.shard_count},
               'selectedIds': [row[2]['id'] for row in selected], 'shardIds': [row[2]['id'] for row in shard],
               'toolchain': {'dotnet': artifact(dotnet), 'sdkVersion': sdk,
                             'csc': artifact(sdk_compiler(dotnet, sdk, snapshot))},
               'reviewStatus': 'Supplemental exact execution evidence only; no automatic review approval.', 'results': []}
    if binding:
        summary['suiteBinding'] = binding
    if repair:
        summary.update(schemaVersion='1.1', repairBridge=repair['bridgePath'], repairBridgeSha256=repair['bridgeSha256'],
                       originalSourceSha256=repair['originalInventory']['sha256'])
    if current:
        summary.update(schemaVersion='1.2', currentCampaign=current['receiptPath'], currentCampaignSha256=current['receiptSha256'])
    for relative, source, mutant in shard:
        directory = output / ('mutant-' + mutant['id'])
        directory.mkdir()
        target = snapshot / relative
        original = target.read_bytes()
        if digest(original) != paths[relative]:
            raise ValueError('Prior source replacement was not restored exactly.')
        changed, original_span = replace_span(source, mutant['location'], mutant['replacement'])
        mutated = changed.encode('utf-8')
        (directory / 'mutated-source.cs').write_bytes(mutated)
        value = {'format': 'exportaramus-independent-mutant-result', 'schemaVersion': '1.0', 'file': relative,
                 'sourceSha256': paths[relative], 'mutatedSourceSha256': digest(mutated),
                 'mutatedSource': str((directory / 'mutated-source.cs').resolve()), 'originalSpan': original_span,
                 'mutant': {name: mutant.get(name) for name in ('id', 'status', 'statusReason', 'mutatorName', 'location', 'replacement')}}
        try:
            target.write_bytes(mutated)
            value['build'] = execute(build_command, snapshot, directory / 'build.log', args.build_timeout_seconds)
            value['build']['compilerDiagnosticCodes'] = sorted(set(re.findall(r'\berror (CS\d+):', (directory / 'build.log').read_text(encoding='utf-8'))))
            value['tests'] = None
            if value['build']['exitCode'] == 0 and not value['build']['timedOut']:
                value['tests'] = run_tests(mutant_test_command, snapshot, directory / 'test-results', args.test_timeout_seconds)
            value['outcome'] = classify(value['build'], value['tests'], baseline['tests']['counts']['total'])
            if value['tests'] is not None and value['outcome'] in ('Killed', 'Survived'):
                if case_names(Path(value['tests']['trx'])) != baseline_cases:
                    value['outcome'] = 'Error'
            if value['outcome'] == 'Error':
                value['errorKind'] = 'CompilerRejected' if value['build']['compilerDiagnosticCodes'] else 'IncompleteOrFailedExecution'
        finally:
            target.write_bytes(original)
            if digest(target.read_bytes()) != paths[relative]:
                raise RuntimeError('Exact source restoration failed.')
        save(directory / 'result.json', value)
        summary['results'].append(value)
        print(json.dumps({'id': mutant['id'], 'outcome': value['outcome'], 'completed': len(summary['results']), 'total': len(shard)}), flush=True)
    summary['changedInputs'] = [path for path, expected in paths.items() if digest((snapshot / path).read_bytes()) != expected]
    summary['restoredInputsUnchanged'] = not summary['changedInputs']
    summary['statusCounts'] = {status: sum(row['outcome'] == status for row in summary['results']) for status in ('Killed', 'Survived', 'Timeout', 'Error')}
    save(output / 'independent-mutation-summary.json', summary)
    # A completed run is not a passing mutation result. Every non-killed mutant needs review.
    return 0 if summary['restoredInputsUnchanged'] and all(row['outcome'] == 'Killed' for row in summary['results']) else 1


if __name__ == '__main__':
    raise SystemExit(main())
