"""Portable end-to-end verifier regressions using a detached canonical-only root."""
from pathlib import Path
import json
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ('consumer-kit-verify.py', 'contract-checksums.py', 'contract-verify.py',
         'contract-quality-scope.py')


class PortableContractVerificationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.root = Path(cls.temporary.name) / 'detached source'
        cls.root.mkdir()
        for directory in ('src', 'data', 'project', 'tests', 'examples'):
            shutil.copytree(ROOT / directory, cls.root / directory,
                            ignore=shutil.ignore_patterns('bin', 'obj', 'TestResults', '__pycache__'))
        (cls.root / 'tools').mkdir()
        for name in TOOLS:
            shutil.copyfile(ROOT / 'tools' / name, cls.root / 'tools' / name)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def invoke(self, name, *arguments, optimized=False):
        command = [sys.executable, *(['-O'] if optimized else []), str(self.root / 'tools' / name),
                   '--root', str(self.root), *map(str, arguments)]
        return subprocess.run(command, capture_output=True, text=True, cwd=self.temporary.name, timeout=45)

    def test_all_supported_contracts_and_integrity_work_without_private_directories(self):
        # Arrange.
        self.assertFalse((self.root / 'reference').exists())
        self.assertFalse((self.root / 'development').exists())
        manifest = self.root / 'project/ConsumerKit/1.2/SHA256SUMS'
        before = manifest.read_bytes()
        # Act.
        consumer = self.invoke('consumer-kit-verify.py')
        contracts = self.invoke('contract-verify.py')
        checksums = self.invoke('contract-checksums.py', '--verify')
        # Assert.
        for result in (consumer, contracts, checksums):
            self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(21, json.loads(consumer.stdout)['canonicalSchemaCount'])
        self.assertEqual(['1.0', '1.1', '1.2'], [kit['version'] for kit in json.loads(consumer.stdout)['kits']])
        self.assertEqual(22, json.loads(contracts.stdout)['schemas'])
        self.assertGreater(json.loads(contracts.stdout)['canonicalChecksumFiles'], 0)
        self.assertEqual(before, manifest.read_bytes())
        self.assertFalse((self.root / 'reference').exists())
        self.assertFalse((self.root / 'development').exists())

    def test_source_tampering_is_rejected_without_rewriting_the_manifest(self):
        # Arrange.
        path = self.root / 'src/Contracts/1.2/Consumer/CollectionDataIpcClient.cs'
        before = path.read_bytes()
        manifest = self.root / 'project/ConsumerKit/1.2/SHA256SUMS'
        pinned = manifest.read_bytes()
        try:
            path.write_bytes(before + b'\n// Altered detached source.\n')
            # Act.
            result = self.invoke('contract-checksums.py', '--verify', optimized=True)
            # Assert.
            self.assertNotEqual(0, result.returncode)
            self.assertIn('complete shipped source inventory', result.stderr)
            self.assertEqual(pinned, manifest.read_bytes())
        finally:
            path.write_bytes(before)

    def test_unlisted_new_kit_source_is_rejected_as_incomplete_manifest(self):
        # Arrange.
        path = self.root / 'src/Contracts/1.2/AdditionalPolicy.cs'
        path.write_text('internal class AdditionalPolicy {}', encoding='utf-8')
        try:
            # Act.
            result = self.invoke('consumer-kit-verify.py')
            # Assert.
            self.assertNotEqual(0, result.returncode)
            self.assertIn('complete shipped source inventory', result.stderr)
        finally:
            path.unlink()

    def test_canonical_legacy_fixture_is_validated_instead_of_a_private_duplicate(self):
        # Arrange: legacy fixtures are not members of the active 1.2 checksum manifest.
        path = self.root / 'tests/fixtures/Contracts/1.0/fixtures/valid/capabilities-valid.json'
        before = path.read_bytes()
        try:
            path.write_text('{}', encoding='utf-8')
            # Act.
            result = self.invoke('consumer-kit-verify.py', '--kit-version', '1.0')
            # Assert.
            self.assertNotEqual(0, result.returncode)
            self.assertIn('Valid fixture rejected', result.stderr)
        finally:
            path.write_bytes(before)

    def test_conflicting_duplicate_schema_id_is_rejected_offline(self):
        # Arrange.
        path = self.root / 'data/FFXIVCollect/1.0/derived-inputs.schema.json'
        before = path.read_bytes()
        changed = json.loads(before)
        changed['title'] = 'Conflicting duplicate schema'
        try:
            path.write_text(json.dumps(changed), encoding='utf-8')
            # Act.
            result = self.invoke('consumer-kit-verify.py')
            # Assert.
            self.assertNotEqual(0, result.returncode)
            self.assertIn('Conflicting schema ID', result.stderr)
        finally:
            path.write_bytes(before)

    def test_quality_scope_in_detached_root_keeps_provider_and_consumer_distinct(self):
        # Arrange.
        output = Path(self.temporary.name) / 'quality receipt'
        # Act.
        result = self.invoke('contract-quality-scope.py', '--output-dir', output)
        # Assert.
        self.assertEqual(0, result.returncode, result.stderr)
        manifest = json.loads((output / 'coverage-manifest.json').read_bytes())
        client = 'src/Contracts/1.2/Consumer/CollectionDataIpcClient.cs'
        self.assertNotIn(client, manifest['handwrittenSourceHashes'])
        self.assertIn(client, manifest['separateConsumerSuite']['sourceHashes'])
        self.assertEqual([], manifest['decisionFileExclusions'])
        self.assertFalse(any(path.startswith('reference/') for path in manifest['handwrittenSourceHashes']))

    def test_quality_threshold_rejection_survives_python_optimization(self):
        # Arrange.
        path = self.root / 'tests/stryker-config.json'
        before = path.read_bytes()
        changed = json.loads(before)
        changed['stryker-config']['thresholds']['break'] = 0
        try:
            path.write_text(json.dumps(changed), encoding='utf-8')
            # Act.
            result = self.invoke('contract-quality-scope.py', '--output-dir', Path(self.temporary.name) / 'rejected', optimized=True)
            # Assert.
            self.assertNotEqual(0, result.returncode)
            self.assertIn('thresholds differ', result.stderr)
        finally:
            path.write_bytes(before)

    def test_public_commands_reject_unsupported_modes_and_advertise_only_published_inputs(self):
        for name in ('contract-verify.py', 'contract-checksums.py', 'consumer-kit-verify.py'):
            with self.subTest(command=name):
                result = self.invoke(name, '--historical')
                self.assertEqual(2, result.returncode)
                self.assertIn('unrecognized arguments: --historical', result.stderr)
                self.assertNotIn('Traceback', result.stderr)
                help_result = self.invoke(name, '--help')
                self.assertEqual(0, help_result.returncode, help_result.stderr)
                for text in ('--historical', 'development-only', 'reference/'):
                    self.assertNotIn(text, help_result.stdout)

    def test_checksum_command_accepts_only_the_published_source_kit(self):
        before = (self.root / 'project/ConsumerKit/1.2/SHA256SUMS').read_bytes()
        for version in ('all', '1.2'):
            result = self.invoke('contract-checksums.py', '--verify', '--kit-version', version)
            self.assertEqual(0, result.returncode, result.stderr)
        for version in ('1.0', '1.1'):
            result = self.invoke('contract-checksums.py', '--verify', '--kit-version', version)
            self.assertEqual(2, result.returncode)
        self.assertEqual(before, (self.root / 'project/ConsumerKit/1.2/SHA256SUMS').read_bytes())


if __name__ == '__main__':
    unittest.main()
