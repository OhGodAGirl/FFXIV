import unittest
from publication_hygiene import check_file

class PublicationHygieneTests(unittest.TestCase):
    def test_generated_outputs_and_live_state_are_rejected(self):
        for name in ['project/bin/a.json', 'tests/TestResults/result.xml', 'out/latest.zip', 'current-snapshot.json', 'snapshots/characters/123/inventory.json', 'development/current/plugin.dll']:
            with self.subTest(name=name):
                self.assertTrue(check_file(name, b'{}'))

    def test_private_paths_and_links_are_rejected_without_echoing_content(self):
        for value in [r'D:\work\secret', 'obsidian://open?vault=private', 'file:///private/data', '/home/person/private/file']:
            with self.subTest(value=value):
                issues = check_file('README.md', value.encode())
                self.assertTrue(issues)
                self.assertNotIn(value, '\n'.join(issues))

    def test_synthetic_fixture_exception_does_not_allow_private_vault_links(self):
        fixture = 'tools/test-reference-inputs.py'
        self.assertFalse(check_file(fixture, b'C:/outside.json'))
        self.assertFalse(check_file('tests/Exportaramus.Tests/Diagnostics/DiagnosticsTests.cs', b'/home/alice/file'))
        self.assertTrue(check_file(fixture, b'obsidian://open?vault=private'))

    def test_source_and_synthetic_snapshots_are_allowed(self):
        for name in ['src/Features/Snapshots/AtomicSnapshotStorage.cs', 'tests/fixtures/snapshot.json', 'images/icon.png']:
            self.assertFalse(check_file(name, b'ordinary content'))

if __name__ == '__main__':
    unittest.main()
