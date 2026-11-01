"""Materialize the exact source-linked offline quality scope; never exclude decision files."""
from pathlib import Path
import glob
import hashlib
import json
import re
import xml.etree.ElementTree as ET
import argparse
import posixpath

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'tests/Exportaramus.Testable/Exportaramus.Testable.csproj'
CONSUMER_PROJECT = ROOT / 'project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj'
CONSUMER_TESTS = ROOT / 'tests/Exportaramus.ConsumerKit.Tests/Exportaramus.ConsumerKit.Tests.csproj'

REASONS = {
    'src/Features/Inventory/InventoryCaptureAdapter.Dalamud.cs': 'Framework-thread bridge for inventory capture; InventoryService and InventoryPolicy remain in the included decision scope.',
    'src/Features/Inventory/InventoryContainerReader.Dalamud.cs': 'Native inventory container and slot observation with transient pointer checks; InventorySlotPolicy quantity/quality decisions and query policies are included.',
    'src/Features/Inventory/InventoryIpcProvider.Dalamud.cs': 'Dalamud gate registration and inventory runtime binding; protocol and inventory state decisions remain included.',
    'src/Features/Inventory/RetainerGuideAdapter.Dalamud.cs': 'Native retainer interaction and readiness bridge; RetainerGuideController and RetainerMenuPolicy unique label selection are included.',
    'src/Shared/Ui/GameThemeObserver.Dalamud.cs': 'Native game-setting read; ThemeObservationPolicy polling/failure retention and prepared presentation are included.',
    'src/Bootstrap/Configuration.Dalamud.cs': 'Dalamud IPluginConfiguration binding; persistence and configuration write coordination are included pure code.',
    'src/Bootstrap/ConfigurationPersistence.Dalamud.cs': 'Dalamud configuration API bridge; file validation and serialized writes are included pure code.',
    'src/Bootstrap/Plugin.Dalamud.cs': 'Dalamud service composition, command/event registration, UI dispatch and native lifecycle. ConfigurationPolicy, IntentAdmissionPolicy and LifecycleCleanup own included version, admission and owned-worker shutdown decisions.',
    'src/Features/CollectionData/Capture/CollectionReader.Dalamud.cs': 'Native/managed game API reads, transient pointer/container identity rechecks and live memory observations. CaptureReadPolicy, notebook mapping, dresser expansion and retainer accumulation are included.',
    'src/Features/CollectionData/Capture/PromptCaptureHints.Dalamud.cs': 'Public Dalamud event subscriptions and native row identification. PromptCaptureHintBuffer classification, coalescing, burst limits and lifetime behavior are included alongside PromptCapturePolicy.',
    'src/Features/CollectionData/Capture/SnapshotCapturePort.Dalamud.cs': 'Framework-thread and game-service bridge to included catalog cache, read operation and snapshot controller.',
    'src/Features/CollectionData/Capture/StorageReadinessObserver.Dalamud.cs': 'Native window/manager observation and byte/ID fingerprint acquisition. Storage readiness and window-edge transition policies are included.',
    'src/Features/CollectionData/Catalog/CollectionCatalogBuilder.Dalamud.cs': 'Lumina typed-sheet access and projection input acquisition. CatalogRowPolicy eligibility, bounded projection, mapping and catalog acceptance are included.',
    'src/Features/CollectionData/Publication/CollectionDataIpcProvider.Dalamud.cs': 'Exact API 15 call-gate registration/invocation/disposal. Publication, notice coalescing and evidence acceptance policies are included.',
    'src/Features/Diagnostics/DiagnosticsWindow.Dalamud.cs': 'ImGui rendering and click dispatch of prepared diagnostic state. Layout, report validation, redaction, preparation and controller decisions are included.',
    'src/Features/Diagnostics/DiagnosticNativeSampling.Dalamud.cs': 'Framework-thread acquisition of native framework timing and bounded game-version bytes. DiagnosticSamplingPolicy owns included validity and parsing decisions.',
    'src/Features/GuidedExport/GuidedCaptureAdapter.Dalamud.cs': 'Native object/addon observation, pointer reacquisition and point-of-action native calls. GuidedAdapterPolicy owns included validation-record applicability and effect/scope authorization; controller and furnishing policies are also included.',
    'src/Features/GuidedExport/GuidedRuntimeValidation.Dalamud.cs': 'Data-only native validation record binds Dalamud ObjectKind and ConditionFlag types; validation applicability decisions are included separately.',
    'src/Features/Main/MainHeaderRenderer.Dalamud.cs': 'ImGui header rendering and button intent dispatch from prepared view state.',
    'src/Features/Main/MainWindow.Dalamud.cs': 'ImGui window/popup focus, draw lifecycle and intent dispatch; view state, layout and guided transition decisions are included.',
    'src/Features/Main/OperationsRenderer.Dalamud.cs': 'ImGui operation controls and intent dispatch; enablement and guided state are prepared by included presenters.',
    'src/Features/Main/SnapshotHealthRenderer.Dalamud.cs': 'ImGui health rendering and copy-click dispatch; health tone and copy eligibility are included presenter decisions.',
    'src/Features/Main/SnapshotSummaryRenderer.Dalamud.cs': 'ImGui collection table geometry and drawing of prepared counts, markers and deltas; count and delta computations are included.',
    'src/Features/Main/StorageHistoryRenderer.Dalamud.cs': 'ImGui history summary drawing and history-view intent dispatch.',
    'src/Shared/Ui/Drawing.Dalamud.cs': 'ImGui drawing, hover tooltip and visual button primitives; tooltip content and visual tokens are included.',
    'src/Shared/Ui/UiIcons.Dalamud.cs': 'Local ImGui vector icon drawing switch; no collection, lifecycle or action authorization decision.',
    'src/Shared/Ui/WindowStyle.Dalamud.cs': 'ImGui style push/pop bridge; visual token values are included.',
    'src/Contracts/1.2/Consumer/CollectionDataGateBindings.Dalamud.cs': 'Exact API 15 consumer gate binding/subscription bridge. CollectionDataIpcClient decisions are tested by the separate ConsumerKit test suite; provider-suite coverage does not establish client coverage.',
}

def relative(path): return path.resolve().relative_to(ROOT).as_posix()
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def has_attribute(source, name):
    return re.search(r'(?:\[|,)\s*(?:global::)?(?:\w+\.)*' + re.escape(name) +
                     r'(?:Attribute)?\s*(?:\(|,|\])', source) is not None


def compile_sources(project):
    included = set()
    for node in ET.parse(project).findall('.//Compile'):
        def expand(patterns):
            return {Path(p).resolve() for pattern in patterns.split(';') if pattern
                    for p in glob.glob(str(project.parent / pattern), recursive=True) if Path(p).is_file()}
        included.update(expand(node.attrib.get('Include', '')) - expand(node.attrib.get('Exclude', '')))
        included.difference_update(expand(node.attrib.get('Remove', '')))
    return included


def project_sources(project, visited=None):
    """Include referenced canonical contract projects, without counting a source twice."""
    visited = set() if visited is None else visited
    project = project.resolve()
    if project in visited:
        return set()
    visited.add(project)
    included = compile_sources(project)
    for reference in ET.parse(project).findall('.//ProjectReference'):
        path = reference.attrib['Include']
        if '$(' in path:
            raise ValueError(f'Unresolved project property in {relative(project)}: {path}')
        included.update(project_sources(project.parent / path, visited))
    return included

def normalize_report_path(path):
    """Resolve Stryker's lexical linked paths without depending on host filesystem semantics."""
    return posixpath.normpath(re.sub('/+', '/', path.replace('\\', '/')))

def main(arguments=None):
    global ROOT, PROJECT, CONSUMER_PROJECT, CONSUMER_TESTS
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=ROOT, help='Exportaramus source root.')
    parser.add_argument('--mutation-report', type=Path)
    parser.add_argument('--mutation-scope', choices=('provider', 'complete'), default='provider',
                        help='Complete additionally reconciles the separately tested consumer client and example reader.')
    parser.add_argument('--output-dir', type=Path, help='Write audit artifacts here instead of updating maintained tests scope files.')
    arguments = parser.parse_args(arguments)
    ROOT = arguments.root.resolve()
    PROJECT = ROOT / 'tests/Exportaramus.Testable/Exportaramus.Testable.csproj'
    CONSUMER_PROJECT = ROOT / 'project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj'
    CONSUMER_TESTS = ROOT / 'tests/Exportaramus.ConsumerKit.Tests/Exportaramus.ConsumerKit.Tests.csproj'
    included = project_sources(PROJECT)
    links = {}
    for compile in ET.parse(PROJECT).findall('.//Compile'):
        if 'Include' not in compile.attrib:
            continue
        found = set()
        for pattern in compile.attrib['Include'].split(';'):
            found.update(Path(p).resolve() for p in glob.glob(str(PROJECT.parent / pattern), recursive=True) if Path(p).is_file())
        for pattern in compile.attrib.get('Exclude', '').split(';'):
            if pattern:
                found.difference_update(Path(p).resolve() for p in glob.glob(str(PROJECT.parent / pattern), recursive=True))
        if 'Link' in compile.attrib:
            require(len(found) == 1, 'A linked source must identify exactly one file.')
            links[compile.attrib['Link'].replace('\\', '/')] = relative(next(iter(found)))
    product = sorted(relative(p) for p in included if relative(p).startswith('src/') and not relative(p).startswith('src/Contracts/'))
    kit = sorted(relative(p) for p in included if relative(p).startswith('src/Contracts/'))
    runtime = project_sources(ROOT / 'project/Exportaramus.csproj')
    platform = sorted(relative(p) for p in runtime if relative(p).startswith('src/') and p.name.endswith('.Dalamud.cs'))
    kit_platform = ['src/Contracts/1.2/Consumer/CollectionDataGateBindings.Dalamud.cs']
    consumer_sources = project_sources(CONSUMER_PROJECT) | {ROOT / 'examples/CollectionExample/InventoryItemReader.cs'}
    require(set(platform + kit_platform) == set(REASONS), 'Native binding inventory differs from reviewed exclusions.')
    require(included == {p for p in runtime if not p.name.endswith('.Dalamud.cs')}, 'Pure runtime source is missing from the provider test scope.')
    attributes = [relative(p) for p in included if has_attribute(p.read_text(encoding='utf-8-sig'), 'ExcludeFromCodeCoverage')]
    require(attributes == [], f'Handwritten coverage exclusions: {attributes}')
    require(not any(has_attribute(p.read_text(encoding='utf-8-sig'), name)
                    for p in included for name in ('GeneratedCode', 'CompilerGenerated')), 'Handwritten generated-code coverage exclusion found.')
    require(not any(re.search(r'Stryker\s+disable', p.read_text(encoding='utf-8-sig'), re.IGNORECASE) for p in included), 'Mutation suppression found in included source.')
    settings = (ROOT / 'tests/coverage.runsettings').read_text()
    require('ExcludeFromCodeCoverageAttribute' in settings and 'CompilerGeneratedAttribute' not in settings, 'Coverage attribute policy differs.')
    mutation = json.loads((ROOT / 'tests/stryker-config.json').read_bytes())['stryker-config']
    require(mutation['mutate'] == ['**/*'] and mutation['thresholds'] == {'high': 100, 'low': 100, 'break': 100}, 'Mutation scope or thresholds differ.')
    require(mutation.get('coverage-analysis') == 'perTestInIsolation' and mutation.get('disable-mix-mutants') is True, 'Mutation isolation settings differ.')
    require(not mutation.get('ignore-methods') and not mutation.get('ignore-mutations'), 'Mutation exclusions are not allowed.')
    extracted = ['src/Features/GuidedExport/GuidedAdapterPolicy.cs', 'src/Features/GuidedExport/GuidedValidationFacts.cs',
        'src/Bootstrap/ConfigurationPolicy.cs', 'src/Features/Main/IntentAdmissionPolicy.cs',
        'src/Features/CollectionData/Catalog/FacewearMappingPolicy.cs',
        'src/Features/CollectionData/Capture/DresserCapturePolicy.cs',
        'src/Features/CollectionData/Capture/StorageFingerprint.cs']
    require(set(extracted).issubset(set(product)), 'Required pure decisions are missing from the provider test scope.')
    plugin = (ROOT / 'src/Bootstrap/Plugin.Dalamud.cs').read_text(encoding='utf-8-sig')
    guided = (ROOT / 'src/Features/GuidedExport/GuidedCaptureAdapter.Dalamud.cs').read_text(encoding='utf-8-sig')
    require('ConfigurationPolicy.RestoreReduceMotion' in plugin and 'IntentAdmissionPolicy.TryEnqueue' in plugin, 'Runtime plugin does not use the tested policies.')
    require('GuidedAdapterPolicy.IsValidated' in guided and 'GuidedAdapterPolicy.AuthorizeEffect' in guided, 'Guided runtime does not use tested validation/authorization policies.')
    manifest = {
        'format': 'exportaramus-quality-source-scope', 'schemaVersion': '1.0',
        'project': relative(PROJECT), 'coverageSettings': 'tests/coverage.runsettings',
        'requiredLinePercent': 100, 'requiredBranchPercent': 100,
        'allowedValidSurvivingMutants': 0, 'allowedUncoveredMutants': 0, 'allowedTimedOutMutants': 0,
        'includedProductSources': product, 'includedConsumerKitSources': kit,
        'linkedSourceCount': len(included), 'decisionFileExclusions': [],
        'separateConsumerSuite': {
            'project': relative(CONSUMER_TESTS), 'libraryProject': relative(CONSUMER_PROJECT),
            'sources': sorted(relative(p) for p in consumer_sources),
            'sourceHashes': {relative(p): digest(p) for p in sorted(consumer_sources)},
            'coverageInterpretation': 'Not included in provider-suite coverage or provider mutation instrumentation. Execute the separate ConsumerKit suite and measure its own coverage before claiming full client qualification.',
        },
        'platformBindingExclusions': [{'path': p, 'justification': REASONS[p]} for p in platform + kit_platform],
        'attributeExclusions': [{'attribute': 'GeneratedCodeAttribute', 'scope': 'Tool-generated source only; no handwritten use found.'},
            {'attribute': 'ExcludeFromCodeCoverageAttribute', 'allowedHandwrittenFiles': [],
             'justification': 'No handwritten current provider source uses this exclusion.'}],
        'handwrittenSourceHashes': {relative(p): digest(p) for p in sorted(included)},
        'mutationInventoryStatus': 'Awaiting completed final-source instrumentation report; initial campaign is exploratory.',
        'nativeBindingAudit': {'runtimeValidation': 'Not established by offline coverage',
            'pendingPureExtractionReview': [], 'completedPureExtractions': extracted,
            'focusedTests': ['tests/Exportaramus.Tests/GuidedExport/GuidedAdapterPolicyTests.cs', 'tests/Exportaramus.Tests/Main/ConfigurationAndAdmissionTests.cs'],
            'reviewStatus': 'Extracted policies are linked into Testable and invoked by the actual platform bindings; final current-settings coverage remains a separate execution gate.'},
    }
    if arguments.mutation_report:
        instrumented_sources = included | consumer_sources if arguments.mutation_scope == 'complete' else included
        instrumentation_hashes = {relative(p): digest(p) for p in sorted(instrumented_sources)}
        manifest['mutationSourceScope'] = arguments.mutation_scope
        manifest['instrumentationSourceHashes'] = instrumentation_hashes
        report = json.loads(arguments.mutation_report.read_bytes())
        instrumented = {}
        unexpected = []
        duplicate_paths = []
        for path, entry in report['files'].items():
            normalized = normalize_report_path(path)
            match = next((relative(p) for p in instrumented_sources if normalized == relative(p) or normalized.endswith('/' + relative(p))), None)
            if match is None:
                match = next((target for link, target in links.items() if normalized == link or normalized.endswith('/' + link)), None)
            if match is None:
                unexpected.append(normalized)
                continue
            if match in instrumented:
                duplicate_paths.append(normalized)
                continue
            source_hash = hashlib.sha256(entry['source'].encode('utf-8')).hexdigest()
            instrumented[match] = {'reportPath': path, 'sourceSha256': source_hash, 'matchesCurrentSource': source_hash == instrumentation_hashes[match],
                'mutantCount': len(entry.get('mutants', [])),
                'statuses': {status: sum(m.get('status') == status for m in entry.get('mutants', [])) for status in sorted({m.get('status', 'Unknown') for m in entry.get('mutants', [])})}}
        missing = sorted(set(instrumentation_hashes) - set(instrumented))
        mismatched = sorted(path for path, value in instrumented.items() if not value['matchesCurrentSource'])
        manifest['mutationInstrumentationAudit'] = {'report': str(arguments.mutation_report.resolve()), 'instrumentedFiles': instrumented,
            'notRepresented': missing, 'unexpectedPaths': unexpected, 'duplicatePaths': duplicate_paths, 'sourceMismatches': mismatched,
            'interpretation': 'Missing files require explicit no-executable-mutant review; no automatic omission is accepted. Source mismatches identify exploratory or stale evidence.'}
        manifest['mutationInventoryStatus'] = 'Instrumentation inventory recorded; final gate requires status, source and missing-file review.'
    destination = arguments.output_dir.resolve() if arguments.output_dir else ROOT / 'tests'
    destination.mkdir(parents=True, exist_ok=True)
    (destination / 'coverage-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    lines = [
        '# Offline coverage and mutation scope', '',
        'This inventory is generated by `tools/contract-quality-scope.py` from the actual Testable source links and its referenced canonical contract projects, reconciled against the accepted runtime project. Regenerate it after adding, moving or changing source files. The JSON manifest records every linked file and its exact SHA-256. The separate ConsumerKit test suite has its own source inventory; provider coverage does not establish consumer-client coverage.', '',
        f'The configured assemblies include {len(product)} product files and {len(kit)} Consumer Kit files. All handwritten pure files in the accepted runtime project are included, including records and source-generation declarations. Unaccepted Wardrobe experiments remain outside the runtime project; they are not omitted runtime decisions. Files with no executable sequence points may not appear in a coverage report. No handwritten decision file is excluded.', '',
        'The required gate is 100% line and branch coverage across the configured decision scope and zero valid surviving, uncovered or timed-out mutants. Aggregate coverage without a source-inventory check is insufficient. Final mutation evidence must match the final source hashes. An exploratory campaign against an earlier isolated tree is not final evidence.', '',
        '## Attribute policy', '',
        '`GeneratedCodeAttribute` excludes tool-generated source only; the inventory check rejects handwritten uses. `CompilerGeneratedAttribute` is deliberately not excluded because compiler-generated lambdas and async state machines can contain handwritten decisions. No handwritten current provider source uses `ExcludeFromCodeCoverageAttribute`.', '',
        '## Platform bindings', '',
        f'There are exactly {len(platform)} product `.Dalamud.cs` files and {len(kit_platform)} source-distributed consumer binding. Additional platform files are the data-only guided validation record and bounded native diagnostic observation bridge. Native pointer validity, game readiness and supervised runtime behavior are not established by offline coverage; API compilation is separate evidence.', '',
        '| File | Specific scope reason |', '| --- | --- |',
        *[f'| `{entry["path"]}` | {entry["justification"]} |' for entry in manifest['platformBindingExclusions']], '',
        'The canonical ConsumerKit suite is `tests/Exportaramus.ConsumerKit.Tests/Exportaramus.ConsumerKit.Tests.csproj`. It compiles `project/ConsumerKit/1.2/Exportaramus.ConsumerKit.V1_2.csproj` and the example InventoryItemReader. Run it separately. Its client and example reader are not part of provider-suite coverage or mutation instrumentation.', '',
        '## Audit status', '',
        'The identified guided validation/effect authorization and plugin version/intent-admission predicates have been extracted into `GuidedAdapterPolicy`, `GuidedValidationFacts`, `ConfigurationPolicy` and `IntentAdmissionPolicy`. They are source-linked into Testable, invoked by the actual platform bindings and covered by `GuidedAdapterPolicyTests` and `ConfigurationAndAdmissionTests`. PromptCaptureHintBuffer now owns tested hint classification, coalescing, burst limits and lifetime behavior. This inventory does not establish coverage of all platform behavior. Final current-settings coverage is a separate execution gate. Native pointer reacquisition and immediate manager identity checks remain platform-binding responsibilities.', '',
        'The maintained Stryker configuration selects the whole Testable project with `mutate: ["**/*"]`; see [Stryker.NET configuration](https://stryker-mutator.io/docs/stryker-net/configuration/). Check the final JSON instrumentation inventory against the actual provider and source-linked canonical contract files. The separate ConsumerKit client and example reader require their own instrumentation and coverage evidence. Exploratory results cannot establish the final mutation gate; the immutable campaign summary records its exact source hashes and unresolved statuses.', '',
    ]
    (destination / 'coverage-exclusions.md').write_text('\n'.join(lines), encoding='utf-8')
    print(json.dumps({'linkedSources': len(included), 'productSources': len(product), 'kitSources': len(kit), 'productPlatformFiles': len(platform), 'kitPlatformFiles': len(kit_platform), 'handwrittenAttributeExclusions': attributes}))

if __name__ == '__main__': main()

