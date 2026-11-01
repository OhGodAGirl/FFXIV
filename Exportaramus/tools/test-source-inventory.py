"""Exercise the source inventory against an isolated tree, without build outputs."""
from pathlib import Path
import json
import shutil
import subprocess
import tempfile
import unittest


class SourceInventoryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / 'tools').mkdir()
        script = Path(__file__).with_name('Get-SourceInventory.ps1')
        shutil.copyfile(script, self.root / 'tools' / script.name)

    def write(self, name, content='fixture'):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding='utf-8')

    def inventory(self):
        output = self.root / 'development' / 'inventory.json'
        subprocess.run(['pwsh', '-NoProfile', '-File', str(self.root / 'tools/Get-SourceInventory.ps1'),
                        '-Destination', str(output)], check=True, capture_output=True, text=True,
                       cwd=self.root.parent)
        return json.loads(output.read_text(encoding='utf-8-sig'))

    def test_consumer_example_and_root_inputs_are_included(self):
        # Arrange
        names = ['examples/CollectionExample/InventoryItemReader.cs',
                 'examples/CollectionExample/Plugin.Dalamud.cs',
                 'examples/CollectionExample/CollectionExample.csproj',
                 'examples/CollectionExample/packages.lock.json',
                 'README.md', 'LICENSE', '.gitattributes', '.gitignore', '.editorconfig', 'global.json',
                 'Directory.Packages.props', '.config/dotnet-tools.json', '.github/workflows/check.yml',
                 'docs/Verification.md', 'docs/assets/new-diagram.png', 'CONTRIBUTING.md']
        for name in names:
            self.write(name)
        # Act
        result = self.inventory()
        # Assert
        paths = {entry['path'] for entry in result['files']}
        self.assertTrue(set(names).issubset(paths), set(names) - paths)

    def test_generated_and_historical_changes_do_not_change_inventory(self):
        # Arrange
        before = self.inventory()
        for name in ('examples/CollectionExample/obj/generated.cs', 'project/bin/product.dll',
                     'tests/TestResults/result.trx', 'tools/__pycache__/cached.pyc',
                     'development/history/source.cs', '.git/config', 'reference/old/source.cs',
                     'docs/development/history.md', 'tools/cache.pyc', 'AGENTS.md', 'old-package.zip'):
            self.write(name)
        # Act
        after = self.inventory()
        # Assert
        self.assertEqual(before, after)

    def test_example_change_updates_hash_and_repeat_is_stable(self):
        # Arrange
        self.write('examples/CollectionExample/InventoryItemReader.cs', 'before')
        before = self.inventory()
        self.write('examples/CollectionExample/InventoryItemReader.cs', 'after')
        # Act
        after = self.inventory()
        repeated = self.inventory()
        # Assert
        self.assertNotEqual(before['sha256'], after['sha256'])
        self.assertEqual(after, repeated)

    def test_destination_inside_authored_tree_does_not_inventory_itself(self):
        # Arrange
        output = self.root / 'docs' / 'inventory.json'
        self.write('docs/Verification.md')
        command = ['pwsh', '-NoProfile', '-File', str(self.root / 'tools/Get-SourceInventory.ps1'),
                   '-Destination', str(output)]
        # Act
        subprocess.run(command, check=True, capture_output=True, text=True)
        before = output.read_bytes()
        subprocess.run(command, check=True, capture_output=True, text=True)
        # Assert
        self.assertEqual(before, output.read_bytes())
        paths = {item['path'] for item in json.loads(before.decode('utf-8-sig'))['files']}
        self.assertNotIn('docs/inventory.json', paths)

    def test_absent_private_reference_and_named_specifications_are_not_required(self):
        # Arrange: include only the portable script and public guide.
        self.write('docs/Verification.md')
        # Act
        result = self.inventory()
        # Assert
        self.assertEqual({'tools/Get-SourceInventory.ps1', 'docs/Verification.md'},
                         {entry['path'] for entry in result['files']})


if __name__ == '__main__':
    unittest.main()
