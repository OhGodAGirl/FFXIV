"""Canonical independent-execution suite binding. No mutation outcome waivers."""
from pathlib import Path
import hashlib
import json
import subprocess

TARGETS = {
    'provider': ('tests/Exportaramus.Testable/Exportaramus.Testable.csproj', 'tests/Exportaramus.Tests/Exportaramus.Tests.csproj'),
    'consumerkit12': ('project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj', 'tests/Exportaramus.ConsumerKit.Tests/Exportaramus.ConsumerKit.Tests.csproj'),
    'inventoryreader': ('tests/Exportaramus.InventoryReader.Mutation/Exportaramus.InventoryReader.Mutation.csproj', 'tests/Exportaramus.InventoryReader.Mutation.Tests/Exportaramus.InventoryReader.Mutation.Tests.csproj'),
}


def command(dotnet, verb, snapshot, target, results=None):
    if target not in TARGETS:
        raise ValueError('Unknown canonical mutation target')
    base = [str(dotnet), verb, str(Path(snapshot) / TARGETS[target][1])]
    if verb == 'restore':
        return base + ['--locked-mode', '--nologo']
    if verb == 'build':
        return base + ['-c', 'Release', '--no-restore', '--nologo', '-v:minimal']
    if verb != 'test':
        raise ValueError('Unknown execution operation')
    base += ['-c', 'Release', '--no-restore', '--nologo']
    if target == 'provider':
        base += ['--settings', str(Path(snapshot) / 'tests/coverage.runsettings'), '--collect:XPlat Code Coverage']
    if results is not None:
        base += ['--results-directory', str(results), '--logger', 'trx;LogFileName=tests.trx']
    return base


def verify_command(execution, verb, snapshot, dotnet, target):
    results = Path(execution['trx']).parent if verb == 'test' else None
    if (Path(execution['workingDirectory']).resolve() != Path(snapshot).resolve()
            or execution['command'] != command(dotnet, verb, snapshot, target, results)):
        raise ValueError('Execution does not use the exact canonical suite, tool, working directory and arguments')


def evaluate(snapshot, target, dotnet, paths):
    """Evaluate the frozen projects, including property-based references, without builds."""
    if target not in TARGETS or target == 'provider':
        raise ValueError('Explicit binding requires a supported non-provider target')
    root = Path(snapshot).resolve()
    projects = {}
    active = set()

    def relative(value):
        path = Path(value).resolve()
        if not path.is_relative_to(root) or not path.is_file():
            raise ValueError('Project input is missing or outside the snapshot')
        key = path.relative_to(root).as_posix()
        if paths.get(key) != hashlib.sha256(path.read_bytes()).hexdigest():
            raise ValueError('Evaluated input is not bound to the frozen inventory: ' + key)
        return key

    def visit(key):
        if key in active:
            raise ValueError('Cyclic project reference')
        if key in projects:
            return
        relative(root / key)
        active.add(key)
        args = [str(dotnet), 'msbuild', str(root / key), '-getItem:Compile,ProjectReference',
                '-getProperty:MSBuildProjectFullPath', '-p:Configuration=Release', '-nologo']
        data = json.loads(subprocess.check_output(args, cwd=root, text=True, encoding='utf-8-sig', timeout=120))
        if relative(data['Properties']['MSBuildProjectFullPath']) != key:
            raise ValueError('Evaluated project identity differs')
        entry = {kind: sorted({relative(item['FullPath']) for item in data['Items'][kind]})
                 for kind in ('Compile', 'ProjectReference')}
        for ref in entry['ProjectReference']:
            visit(ref)
        projects[key] = entry
        active.remove(key)

    production, tests = TARGETS[target]
    visit(production)
    visit(tests)

    def closure(key):
        entry = projects[key]
        return set(entry['Compile']).union(*(closure(r) for r in entry['ProjectReference']))

    sources = closure(production)
    if not sources or not sources <= closure(tests):
        raise ValueError('Canonical test suite does not compile the complete production target')
    return {'target': target, 'productionProject': production, 'testProject': tests,
            'projects': projects, 'productionSources': sorted(sources)}


def verify_summary(summary, paths, selected_sources):
    binding = summary.get('suiteBinding')
    if not binding:
        if any(p.startswith('src/Contracts/1.2/Consumer/') or p == 'examples/CollectionExample/InventoryItemReader.cs' for p in selected_sources):
            raise ValueError('Non-provider evidence requires explicit suite binding')
        return 'provider'
    expected_hash = paths.get('tools/suite_binding.py')
    if not expected_hash or expected_hash != hashlib.sha256(Path(__file__).read_bytes()).hexdigest():
        raise ValueError('Suite validator does not match the frozen inventory')
    dotnet = Path(summary['toolchain']['dotnet']['path']).resolve()
    if hashlib.sha256(dotnet.read_bytes()).hexdigest() != summary['toolchain']['dotnet']['sha256']:
        raise ValueError('Bound .NET host changed')
    snapshot = Path(summary['snapshot']).resolve()
    for relative, expected in paths.items():
        path = (snapshot / relative).resolve()
        if not path.is_relative_to(snapshot) or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError('Frozen suite input changed')
    current = evaluate(snapshot, binding['target'], dotnet, paths)
    if current != binding or not selected_sources or not set(selected_sources) <= set(current['productionSources']):
        raise ValueError('Wrong suite or mixed mutation targets')
    for name, execution in summary['baseline'].items():
        verify_command(execution, 'test' if name == 'tests' else name, snapshot, dotnet, binding['target'])
    for result in summary['results']:
        if result['file'] not in current['productionSources']:
            raise ValueError('Mixed mutation targets')
        verify_command(result['build'], 'build', snapshot, dotnet, binding['target'])
        if result.get('tests') is not None:
            verify_command(result['tests'], 'test', snapshot, dotnet, binding['target'])
    return binding['target']


def report_mapping(binding, report, paths):
    import ntpath
    mapping = {}
    for reported, entry in report['files'].items():
        normalized = ntpath.normpath(reported).replace(chr(92), '/').casefold()
        matches = [source for source in binding['productionSources']
                   if normalized == source.casefold() or normalized.endswith('/' + source.casefold())]
        if not matches:
            continue  # Combined reports also contain other canonical targets.
        if len(matches) != 1 or hashlib.sha256(entry['source'].encode('utf-8')).hexdigest() != paths.get(matches[0]):
            raise ValueError('Report path/source does not match the exact canonical production target')
        if matches[0] in mapping.values():
            raise ValueError('Duplicate report source')
        mapping[reported] = matches[0]
    if set(mapping.values()) != set(binding['productionSources']):
        raise ValueError('Report omits canonical production sources')
    return mapping
