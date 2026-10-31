"""Regression checks for current schemas versus historical schema provenance."""
from pathlib import Path
import importlib.util
import json
import unittest
from jsonschema import Draft202012Validator

spec = importlib.util.spec_from_file_location('contract_verify', Path(__file__).with_name('contract-verify.py'))
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)


class ContractVerificationTests(unittest.TestCase):
    def test_current_registry_includes_beasts_import_but_not_fragment_or_retired_diagnostics(self):
        # Arrange / Act
        paths = verify.current_schema_paths()
        identifiers = [json.loads(path.read_bytes())['$id'] for path in paths]
        # Assert
        self.assertEqual(22, len(set(identifiers)))
        self.assertEqual(len(paths), len(set(identifiers)))
        self.assertTrue(any('/ffxiv-collect/1.1/' in key for key in identifiers))
        self.assertFalse(any('ContractExtensions' in path.parts or 'Diagnostics/1.0/' in path.as_posix()
                             for path in paths))

    def test_collection_fixture_uses_its_own_version(self):
        # Arrange
        schemas = {key: {'$id': key} for key in
                   (f'https://example.test/{version}/collection-data.schema.json' for version in ('1.0', '1.1', '1.2'))}
        for version in ('1.0', '1.1', '1.2'):
            with self.subTest(version=version):
                path = Path(f'tests/fixtures/Contracts/{version}/fixtures/valid/compact-valid.json')
                # Act
                selected = verify.fixture_schema(path, schemas, '/collection-data.schema.json')
                # Assert
                self.assertIn(f'/{version}/', selected['$id'])

    def test_beasts_import_fixture_uses_kit_resource_schema(self):
        # Arrange
        fixture = verify.ROOT/'tests/fixtures/Contracts/1.2/fixtures/valid/ffxiv-collect-valid-beasts-all-collections.json'
        old = json.loads((verify.ROOT/'data/FFXIVCollect/1.0/ffxiv-collect-import.schema.json').read_bytes())
        payload = json.loads(fixture.read_bytes())
        # Act
        selected = verify.fixture_schema(fixture, {old['$id']: old}, '/import.schema.json')
        # Assert
        self.assertTrue(Draft202012Validator(selected).is_valid(payload))
        self.assertFalse(Draft202012Validator(old).is_valid(payload))


if __name__ == '__main__':
    unittest.main()
