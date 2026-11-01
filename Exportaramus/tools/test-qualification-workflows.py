"""Private workflow regression fixtures; never stage files or run product mutation campaigns."""
from pathlib import Path
import contextlib
import hashlib
import importlib.util
import io
import json
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

import qualification_inputs

ROOT = Path(__file__).resolve().parents[1]
POWERSHELL = shutil.which('pwsh')


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding='utf-8')
    return {'path': str(path), 'sha256': digest(path)}


class CurrentInputTests(unittest.TestCase):
    def test_private_current_scanner_matches_public_inventory_and_ignores_history(self):
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory); root = parent / 'source'; root.mkdir()
            names = ['README.md', 'LICENSE', '.config/tools.json', '.github/workflows/ci.yml',
                     'examples/Reader.cs', 'project/Test.csproj', 'docs/Guide.md', 'src/Policy.cs',
                     'tests/PolicyTests.cs', 'tools/helper.py', 'AGENTS.md', 'reference/Legacy.cs',
                     'development/history.cs', 'src/bin/Generated.cs', 'tests/TestResults/result.trx',
                     'root.dll', 'src/cache.pyc']
            for name in names:
                path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('fixture')
            script = root / 'tools/Get-SourceInventory.ps1'
            shutil.copyfile(ROOT / 'tools/Get-SourceInventory.ps1', script)
            receipt = parent / 'inventory.json'
            # Act.
            result = subprocess.run([POWERSHELL, '-NoProfile', '-File', str(script), '-Destination', str(receipt)],
                                    capture_output=True, text=True, timeout=20)
            actual = qualification_inputs.current_paths(root)
            # Assert.
            self.assertEqual(0, result.returncode, result.stderr)
            expected = {entry['path']: entry['sha256'] for entry in json.loads(receipt.read_bytes())['files']}
            self.assertEqual(expected, actual)
            self.assertIn('examples/Reader.cs', actual)
            self.assertNotIn('reference/Legacy.cs', actual)
            self.assertNotIn('AGENTS.md', actual)

    def test_complete_mutation_scope_reconciles_client_reader_and_provider_once(self):
        scope = load('contract-quality-scope')
        included = scope.project_sources(scope.PROJECT) | scope.project_sources(scope.CONSUMER_PROJECT) | {ROOT / 'examples/CollectionExample/InventoryItemReader.cs'}
        target = ROOT / 'tests/Exportaramus.InventoryReader.Mutation/Exportaramus.InventoryReader.Mutation.csproj'
        self.assertEqual({ROOT / 'examples/CollectionExample/InventoryItemReader.cs'}, scope.compile_sources(target))
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory); report = output / 'report.json'
            write(report, {'files': {scope.relative(path): {'source': path.read_bytes().decode('utf-8'), 'mutants': []}
                                    for path in included}})
            # Act: check instrumentation coverage, not the mutation score.
            with contextlib.redirect_stdout(io.StringIO()):
                scope.main(['--mutation-report', str(report), '--mutation-scope', 'complete', '--output-dir', str(output / 'complete')])
                scope.main(['--mutation-report', str(report), '--output-dir', str(output / 'provider')])
            complete = json.loads((output / 'complete/coverage-manifest.json').read_bytes())
            provider = json.loads((output / 'provider/coverage-manifest.json').read_bytes())
            # Assert.
            self.assertEqual(len(included), len(complete['instrumentationSourceHashes']))
            for key in ('notRepresented', 'unexpectedPaths', 'duplicatePaths', 'sourceMismatches'):
                self.assertEqual([], complete['mutationInstrumentationAudit'][key])
            self.assertEqual(2, len(provider['mutationInstrumentationAudit']['unexpectedPaths']))


class CompleteRecordTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.parent = Path(self.temporary.name); self.root = self.parent / 'source'; (self.root / 'tools').mkdir(parents=True)
        for name in ('Complete-QualityRecord.ps1', 'VerificationEnvironment.ps1', 'Get-SourceInventory.ps1'):
            shutil.copyfile(ROOT / 'tools' / name, self.root / 'tools' / name)
        self.sources = {}
        for name in ('src/Provider.cs', 'src/Consumer.cs', 'examples/Reader.cs'):
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('class Fixture {}')
            self.sources[name] = digest(path)
        source_receipt = self.parent / 'source.json'
        result = subprocess.run([POWERSHELL, '-NoProfile', '-File', str(self.root / 'tools/Get-SourceInventory.ps1'),
                                 '-Destination', str(source_receipt)], capture_output=True, text=True, timeout=20)
        self.assertEqual(0, result.returncode, result.stderr)
        self.source_hash = json.loads(source_receipt.read_bytes())['sha256']
        self.evidence = self.parent / 'evidence'; self.evidence.mkdir()
        self.provider = self.coverage('provider', ['Provider'])
        self.consumer = self.coverage('consumer', ['Exportaramus.ConsumerKit.V1_2.CollectionDataIpcClient', 'CollectionExample.InventoryItemReader'])
        client = dict(self.consumer, linesCovered=1, linesValid=1, branchesCovered=2, branchesValid=2)
        reader = dict(client)
        performance = self.coverage('performance', ['Benchmark'])
        performance_trx = Path(performance['trx'])
        document = ET.parse(performance_trx)
        for element in list(document.getroot()):
            if element.tag == 'UriAttachment': document.getroot().remove(element)
        document.write(performance_trx)
        self.performance = dict(trx=str(performance_trx), trxSha256=digest(performance_trx), tests=performance['tests'])
        summary = dict(self.provider, consumerClient=client, inventoryReader=reader, performance=self.performance)
        self.summary_path = self.evidence / 'summary.json'
        package = self.evidence / 'package.zip'; package.write_bytes(b'isolated fixture package')
        self.package = {'status': 'Passed', 'package': str(package), 'packageSha256': digest(package), 'version': 'fixture'}
        scope = {'handwrittenSourceHashes': {'src/Provider.cs': self.sources['src/Provider.cs']},
                 'separateConsumerSuite': {'sourceHashes': {key: value for key, value in self.sources.items() if key != 'src/Provider.cs'}}}
        self.bindings = [write(self.summary_path, summary), write(self.evidence / 'package-inspection.json', self.package),
                         write(self.evidence / 'coverage-manifest.json', scope)]
        for component in (self.provider, self.consumer):
            self.bindings += [{'path': component[name], 'sha256': component[name + 'Sha256']} for name in ('report', 'trx')]
        self.bindings.append({'path': self.performance['trx'], 'sha256': self.performance['trxSha256']})
        self.gate_path = self.evidence / 'gate-execution.json'
        names = ('LockedRestore', 'FormattingAndAnalyzers', 'Build', 'PackageInspection', 'ProviderAndConsumerTestsAndStrictCoverage',
                 'SchemasFixturesMappingsAndKit', 'DecisionScopeInventory', 'PortableToolingTests')
        self.gate = {'format': 'exportaramus-portable-verification', 'status': 'Passed', 'configuration': 'Release',
                     'inputsUnchanged': True, 'sourceSha256': self.source_hash, 'steps': [{'name': name, 'status': 'Passed'} for name in names],
                     'evidence': self.bindings}
        write(self.gate_path, self.gate)
        report = self.evidence / 'mutation-report.json'; write(report, {'files': {}})
        self.mutation_path = self.evidence / 'mutation.json'
        self.mutation = {'format': 'exportaramus-mutation-campaign', 'gatePassed': True, 'inputsUnchanged': True,
                         'sourceSha256': self.source_hash, 'totalMutants': 3, 'unresolvedMutants': [], 'reviewRequired': [],
                         'report': str(report), 'reportSha256': digest(report), 'reviewedSources': [],
                         'instrumentationAudit': {'instrumentedFiles': {key: {'sourceSha256': value, 'matchesCurrentSource': True}
                                                                     for key, value in self.sources.items()}}}
        write(self.mutation_path, self.mutation)
        self.repro_path = self.evidence / 'reproducibility.json'
        write(self.repro_path, {'status': 'Passed', 'scope': 'SameEnvironmentPackageReproduction', 'configuration': 'Release',
                               'dependencyInputsUnchanged': True, 'sourceSha256': self.source_hash,
                               'firstPackageSha256': self.package['packageSha256'], 'rebuiltPackageSha256': self.package['packageSha256']})

    def coverage(self, name, classes):
        run = self.evidence / name; report = run / 'deploy/In/coverage.cobertura.xml'; report.parent.mkdir(parents=True)
        coverage = ET.Element('coverage', {'lines-valid': str(len(classes)), 'lines-covered': str(len(classes)),
                                          'branches-valid': str(2 * len(classes)), 'branches-covered': str(2 * len(classes))})
        container = ET.SubElement(ET.SubElement(coverage, 'packages'), 'package'); types = ET.SubElement(container, 'classes')
        for classname in classes:
            lines = ET.SubElement(ET.SubElement(types, 'class', name=classname), 'lines')
            ET.SubElement(lines, 'line', {'number': '1', 'hits': '1', 'branch': 'true', 'condition-coverage': '100% (2/2)'})
        ET.ElementTree(coverage).write(report)
        trx = run / (name + '.trx'); document = ET.Element('TestRun'); results = ET.SubElement(document, 'Results')
        ET.SubElement(results, 'UnitTestResult', testName='Fixture', outcome='Passed', executionId='fixture', testId='fixture')
        ET.SubElement(ET.SubElement(document, 'TestSettings'), 'Deployment', runDeploymentRoot='deploy')
        ET.SubElement(ET.SubElement(document, 'UriAttachment'), 'A', href='coverage.cobertura.xml')
        counters = {key: '0' for key in ('failed', 'notExecuted', 'aborted', 'timeout', 'error', 'notRunnable', 'disconnected',
                    'inconclusive', 'passedButRunAborted', 'warning', 'completed', 'inProgress', 'pending')}
        counters.update(total='1', executed='1', passed='1')
        ET.SubElement(ET.SubElement(document, 'ResultSummary', outcome='Completed'), 'Counters', counters)
        ET.ElementTree(document).write(trx)
        return {'report': str(report), 'reportSha256': digest(report), 'trx': str(trx), 'trxSha256': digest(trx),
                'linesCovered': len(classes), 'linesValid': len(classes), 'branchesCovered': 2 * len(classes), 'branchesValid': 2 * len(classes),
                'tests': {'total': 1, 'passed': 1, 'failed': 0, 'skipped': 0}}

    def invoke(self):
        return subprocess.run([POWERSHELL, '-NoProfile', '-File', str(self.root / 'tools/Complete-QualityRecord.ps1'),
                               '-GateExecution', str(self.gate_path), '-MutationSummary', str(self.mutation_path),
                               '-Reproduction', str(self.repro_path), '-OutputDirectory', str(self.parent / 'output')],
                              capture_output=True, text=True, timeout=30)

    def test_explicit_portable_receipts_produce_a_new_offline_record_without_staging(self):
        # Act.
        result = self.invoke()
        # Assert.
        self.assertEqual(0, result.returncode, result.stderr)
        record = json.loads(Path(result.stdout.strip()).read_bytes())
        self.assertEqual('2.0', record['schemaVersion'])
        self.assertEqual('Passed', record['status'])
        self.assertFalse(record['loaded'])
        self.assertFalse(record['releaseApproved'])
        self.assertFalse((self.root / 'development/current').exists())

    def test_missing_performance_evidence_cannot_qualify(self):
        summary = json.loads(self.summary_path.read_bytes())
        summary.pop('performance')
        binding = write(self.summary_path, summary)
        self.gate['evidence'] = [binding if item['path'] == str(self.summary_path) else item for item in self.gate['evidence']]
        write(self.gate_path, self.gate)
        result = self.invoke()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Uninstrumented performance evidence is required', result.stderr)

    def test_provider_only_mutation_cannot_qualify_client_and_reader(self):
        # Arrange.
        self.mutation['instrumentationAudit']['instrumentedFiles'].pop('examples/Reader.cs')
        write(self.mutation_path, self.mutation)
        # Act / Assert.
        result = self.invoke()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('mutation instrumentation is required', result.stderr)

    def test_optional_formatting_receipt_cannot_be_relabelled_complete(self):
        # Arrange.
        self.gate['steps'][1]['status'] = 'NotRun'; write(self.gate_path, self.gate)
        # Act / Assert.
        result = self.invoke()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('FormattingAndAnalyzers', result.stderr)

    def test_report_ledger_and_review_proofs_cannot_omit_expected_hashes(self):
        pristine = json.dumps(self.mutation)
        for proof in ('report', 'ledger', 'review'):
            with self.subTest(proof=proof):
                # Arrange: keep artifact bytes but omit the expected identity.
                mutation = json.loads(pristine)
                if proof == 'report':
                    mutation.pop('reportSha256')
                elif proof == 'ledger':
                    mutation['reviewLedger'] = mutation['report']
                else:
                    mutation['reviewEvidence'] = [{'path': mutation['report']}]
                write(self.mutation_path, mutation)
                # Act / Assert.
                result = self.invoke()
                self.assertNotEqual(0, result.returncode)
                self.assertIn('exact artifact hash', result.stderr)


if __name__ == '__main__':
    if not POWERSHELL:
        raise SystemExit('PowerShell7 is required for private qualification regression fixtures.')
    unittest.main()
