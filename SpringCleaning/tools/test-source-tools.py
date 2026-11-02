"""Regression checks for the source-publication boundary."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('source_boundary', Path(__file__).with_name('verify-source.py'))
boundary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(boundary)

class SourceBoundaryTests(unittest.TestCase):
    def test_owned_source_is_allowed(self):
        self.assertEqual([], boundary.violations('src/Bootstrap/Plugin.cs', 'namespace SpringCleaning;', True))

    def test_generated_files_are_rejected(self):
        for name in ['project/bin/Release/plugin.dll', 'tests/TestResults/result.trx', 'development/diagnostics.json', 'tests/StrykerOutput/report.json']:
            with self.subTest(name=name):
                self.assertTrue(boundary.violations(name, '', True))

    def test_private_material_is_rejected_publicly(self):
        for name in ['AGENTS.md', 'docs/engineering/history/DECISIONS.md', 'reference/ConsumerKit/1.2/README.md']:
            with self.subTest(name=name):
                self.assertTrue(boundary.violations(name, '', True))

    def test_private_engineering_records_are_allowed_privately(self):
        self.assertEqual([], boundary.violations('docs/engineering/history/review.md', '', False))

    def test_only_selected_icon_is_allowed(self):
        self.assertEqual([], boundary.violations('images/icon.png', '', True))
        self.assertTrue(boundary.violations('images/candidate.png', '', True))

    def test_paths_cannot_escape(self):
        self.assertTrue(boundary.violations('../outside.txt', '', True))
        self.assertTrue(boundary.violations('/outside.txt', '', True))

    def test_credentials_are_rejected_in_both_audiences(self):
        credential = 'gh' + 'p_' + 'A' * 36
        for public in [True, False]:
            self.assertTrue(boundary.violations('src/example.cs', credential, public))

    def test_private_keys_are_rejected(self):
        key = '-----BEGIN ' + 'OPENSSH PRIVATE KEY-----'
        self.assertTrue(boundary.violations('project/example.json', key, True))

    def test_public_machine_paths_are_rejected(self):
        for value in ['D:' + '/Development/project', 'C:' + '\\Users\\example', 'obsidian' + '://private']:
            self.assertTrue(boundary.violations('project/example.json', value, True))

    def test_public_machine_metadata_is_rejected(self):
        self.assertTrue(boundary.violations('tests/performance-budget.json', 'Intel Core ' + 'i9-00000TEST', True))

    def test_synthetic_path_regressions_remain_allowed(self):
        self.assertEqual([], boundary.violations('tests/DiagnosticPoliciesTests.cs', 'C:' + '\\private\\secret token=ABC', True))

    def test_versioned_contracts_remain_allowed(self):
        self.assertEqual([], boundary.violations('project/Contracts/1.2/provenance.json', 'https://github.com/OhGodAGirl/FFXIV', True))

if __name__ == '__main__':
    unittest.main()
