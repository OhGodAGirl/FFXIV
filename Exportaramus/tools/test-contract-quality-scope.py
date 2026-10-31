"""Regression tests for the inventory used to interpret offline qualification."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('contract_quality_scope', Path(__file__).with_name('contract-quality-scope.py'))
scope = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scope)


class QualityScopeTests(unittest.TestCase):
    def test_qualified_and_grouped_coverage_exclusions_cannot_bypass_scope(self):
        for attribute in ('ExcludeFromCodeCoverage', 'GeneratedCode', 'CompilerGenerated'):
            for spelling in (attribute, attribute + 'Attribute', 'System.Diagnostics.CodeAnalysis.' + attribute,
                             'global::System.Diagnostics.CodeAnalysis.' + attribute + 'Attribute'):
                for text in ('[' + spelling + ']', '[OtherAttribute, ' + spelling + '("reason")]'):
                    with self.subTest(text=text):
                        self.assertTrue(scope.has_attribute(text, attribute))
            self.assertFalse(scope.has_attribute('class ' + attribute + 'Policy {}', attribute))

    def test_transitive_contract_references_are_included_once_even_with_a_cycle(self):
        # Arrange: provider -> contracts -> shared -> contracts.
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name, dependency in [('provider', 'contracts'), ('contracts', 'shared'), ('shared', 'contracts')]:
                (root / f'{name}.cs').write_text(f'public class {name} {{ }}', encoding='utf-8')
                (root / f'{name}.csproj').write_text(
                    f'<Project><ItemGroup><Compile Include="{name}.cs" />'
                    f'<ProjectReference Include="{dependency}.csproj" /></ItemGroup></Project>', encoding='utf-8')

            # Act.
            actual = scope.project_sources(root / 'provider.csproj')

            # Assert.
            self.assertEqual({root / 'provider.cs', root / 'contracts.cs', root / 'shared.cs'}, actual)

    def test_explicit_remove_and_native_exclude_are_honored(self):
        # Arrange: native bindings and a removed experiment must not enter a pure coverage claim.
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ['Policy.cs', 'Native.Dalamud.cs', 'Experiment.cs']:
                (root / name).write_text('class Fixture {}', encoding='utf-8')
            project = root / 'source.csproj'
            project.write_text('<Project><ItemGroup><Compile Include="*.cs" Exclude="*.Dalamud.cs" />'
                               '<Compile Remove="Experiment.cs" /></ItemGroup></Project>', encoding='utf-8')

            # Act.
            actual = scope.project_sources(project)

            # Assert.
            self.assertEqual({root / 'Policy.cs'}, actual)

    def test_current_provider_excludes_the_client_and_the_separate_consumer_suite_includes_it(self):
        # Arrange: the library and provider have different source scopes despite sharing contract records.
        client = scope.ROOT / 'src/Contracts/1.2/Consumer/CollectionDataIpcClient.cs'
        records = scope.ROOT / 'src/Contracts/1.2/CollectionDataContracts.cs'

        # Act.
        provider_sources = scope.project_sources(scope.PROJECT)
        consumer_sources = scope.project_sources(scope.CONSUMER_PROJECT)

        # Assert: provider coverage cannot silently certify the separate client implementation.
        self.assertNotIn(client, provider_sources)
        self.assertIn(client, consumer_sources)
        self.assertIn(records, provider_sources)
        self.assertIn(records, consumer_sources)
        self.assertFalse(any(p.name.endswith('.Dalamud.cs') for p in provider_sources | consumer_sources))


if __name__ == '__main__':
    unittest.main()
