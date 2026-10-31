"""Exercise portable reproduction orchestration using controlled build/inspection boundaries."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import tempfile
import unittest
import zipfile


class ReproducibilityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.root = self.base / 'relocated source with spaces'
        self.tools = self.root / 'tools'
        self.tools.mkdir(parents=True)
        for name in ('Get-SourceInventory.ps1', 'Verify-Reproducibility.ps1', 'VerificationEnvironment.ps1'):
            shutil.copyfile(Path(__file__).with_name(name), self.tools / name)
        self.write('src/Pure.cs', 'source')
        self.write('project/Exportaramus.csproj', '<Project/>')
        self.write('project/bin/Release/Exportaramus/latest.zip', 'package')
        self.write('project/bin/Release/Exportaramus.dll', 'runtime')
        self.write('development/dependency.dll', 'dependency')
        self.write('development/fake-dotnet.ps1', r'''
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$ToolArguments)
$root = Split-Path $PSScriptRoot -Parent
$global:LASTEXITCODE = 0
switch ($ToolArguments[0]) {
    '--version' { '10.0.fixture' }
    'msbuild' { @{Items=@{ReferencePath=@(@{Identity=(Join-Path $root 'development/dependency.dll')})}} | ConvertTo-Json -Depth 5 -Compress }
    'build' {
        switch ($env:EXPORTARAMUS_REPRO_FIXTURE) {
            'source' { Add-Content -LiteralPath "$root/src/Pure.cs" -Value 'changed' }
            'dependency' { Add-Content -LiteralPath "$root/development/dependency.dll" -Value 'changed' }
            'package' { Add-Content -LiteralPath "$root/project/bin/Release/Exportaramus/latest.zip" -Value 'changed' }
            'runtime' { Add-Content -LiteralPath "$root/project/bin/Release/Exportaramus.dll" -Value 'changed' }
            'build-failed' { $global:LASTEXITCODE = 3 }
        }
    }
    default { throw "Unexpected fixture tool arguments: $ToolArguments" }
}
''')
        self.write('tools/Inspect-Package.ps1', r'''
param([string]$OutputDirectory, [string]$EvidenceDirectory)
$package = Join-Path $OutputDirectory 'Exportaramus/latest.zip'
$runtime = Join-Path $OutputDirectory 'Exportaramus.dll'
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
@{status='Passed';package=$package;packageSha256=(Get-FileHash -LiteralPath $package).Hash.ToLowerInvariant();
  files=@(@{path='Exportaramus.dll';sha256=(Get-FileHash -LiteralPath $runtime).Hash.ToLowerInvariant()})} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$EvidenceDirectory/package-inspection.json" -Encoding utf8
''')

    def write(self, relative, content):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding='utf-8')

    def reproduce(self, scenario='unchanged'):
        environment = os.environ.copy()
        environment['EXPORTARAMUS_REPRO_FIXTURE'] = scenario
        return subprocess.run([
            'pwsh', '-NoProfile', '-File', str(self.tools / 'Verify-Reproducibility.ps1'),
            '-DotNet', str(self.root / 'development/fake-dotnet.ps1'),
            '-OutputDirectory', str(self.base / 'evidence with spaces')],
            cwd=self.base, env=environment, capture_output=True, text=True)

    def test_relocated_tree_and_unrelated_working_directory_reproduce(self):
        # Arrange: no private environment script or preexisting inspection receipt exists.
        # Act
        result = self.reproduce()
        # Assert
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        receipts = list((self.base / 'evidence with spaces').rglob('reproducibility.json'))
        self.assertEqual(1, len(receipts))
        receipt = json.loads(receipts[0].read_text(encoding='utf-8-sig'))
        self.assertEqual('Passed', receipt['status'])
        self.assertEqual('SameEnvironmentPackageReproduction', receipt['scope'])
        self.assertEqual(receipt['firstPackageSha256'], receipt['rebuiltPackageSha256'])
        self.assertTrue(receipt['dependencyInputsUnchanged'])
        self.assertEqual(receipt['references'], receipt['referencesAfter'])
        self.assertTrue((receipts[0].parent / 'baseline/package-inspection.json').is_file())
        self.assertTrue((receipts[0].parent / 'rebuilt/package-inspection.json').is_file())
        self.assertFalse((self.root / 'development/docs/package-inspection.json').exists())

    def test_changed_inputs_outputs_or_failed_build_cannot_pass(self):
        cases = {
            'source': 'Source inputs changed during reproduction',
            'dependency': 'Resolved dependency paths or bytes changed during reproduction',
            'package': 'Clean rebuild did not reproduce the exact package bytes',
            'runtime': 'Rebuilt runtime file differs',
            'build-failed': 'Clean product rebuild failed',
        }
        for scenario, expected in cases.items():
            with self.subTest(scenario=scenario):
                # Arrange: restore controlled inputs before each independent run.
                self.write('src/Pure.cs', 'source')
                self.write('development/dependency.dll', 'dependency')
                self.write('project/bin/Release/Exportaramus/latest.zip', 'package')
                self.write('project/bin/Release/Exportaramus.dll', 'runtime')
                # Act
                result = self.reproduce(scenario)
                # Assert
                self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertIn(expected, result.stdout + result.stderr)
                self.assertFalse(list((self.base / 'evidence with spaces').rglob('reproducibility.json')))


class PackageInspectionTests(unittest.TestCase):
    def test_actual_inspector_resolves_relative_package_and_evidence_from_its_source_root(self):
        # Arrange: compile a synthetic assembly with the required public identity, without an SDK install.
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            root = base / 'relocated source'
            output = root / 'project/bin/Release'
            (root / 'tools').mkdir(parents=True)
            output.mkdir(parents=True)
            shutil.copyfile(Path(__file__).with_name('Inspect-Package.ps1'), root / 'tools/Inspect-Package.ps1')
            (root / 'project/Exportaramus.csproj').write_text(
                '<Project><PropertyGroup><Version>1.0.0.2</Version></PropertyGroup></Project>', encoding='utf-8')
            compiler = base / 'compile.ps1'
            compiler.write_text(r'''
param([string]$Destination, [string]$AssemblyName = 'Exportaramus', [string]$TypeName = 'PortableFixture')
Add-Type -TypeDefinition 'internal class ExportaramusCompilerBootstrap {}'
$syntax = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText('using System.Reflection; [assembly: AssemblyVersion("1.0.0.2")] public class ' + $TypeName + ' {}')
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($AssemblyName).WithOptions([Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)).AddReferences([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile([object].Assembly.Location)).AddSyntaxTrees($syntax)
$stream = [IO.File]::Create($Destination)
try { $result = $compilation.Emit($stream); if (!$result.Success) { throw ($result.Diagnostics -join "`n") } }
finally { $stream.Dispose() }
''', encoding='utf-8')
            compiled = subprocess.run(['pwsh', '-NoProfile', '-File', str(compiler), '-Destination',
                                       str(output / 'Exportaramus.dll')], capture_output=True, text=True)
            self.assertEqual(0, compiled.returncode, compiled.stdout + compiled.stderr)
            manifest = {'Name': 'Exportaramus', 'InternalName': 'Exportaramus', 'AssemblyVersion': '1.0.0.2',
                        'DalamudApiLevel': 15, 'Tags': ['fixture'],
                        'IconUrl': 'https://raw.githubusercontent.com/OhGodAGirl/FFXIV/main/Exportaramus/images/icon.png'}
            (output / 'Exportaramus.json').write_text(json.dumps(manifest), encoding='utf-8')
            for name in ('Exportaramus.ConsumerKit.V1_1', 'Exportaramus.ConsumerKit.V1_2'):
                compiled = subprocess.run(['pwsh', '-NoProfile', '-File', str(compiler), '-Destination',
                                           str(output / (name + '.dll')), '-AssemblyName', name], capture_output=True, text=True)
                self.assertEqual(0, compiled.returncode, compiled.stdout + compiled.stderr)
            for name in ('Exportaramus.deps.json', 'images/icon.png'):
                path = output / name
                path.parent.mkdir(exist_ok=True)
                path.write_bytes(b'fixture')
            package = output / 'Exportaramus/latest.zip'
            package.parent.mkdir()
            with zipfile.ZipFile(package, 'w') as archive:
                for name in ('Exportaramus.ConsumerKit.V1_1.dll', 'Exportaramus.ConsumerKit.V1_2.dll',
                             'Exportaramus.deps.json', 'Exportaramus.dll', 'Exportaramus.json', 'images/icon.png'):
                    archive.write(output / name, name)
            # Act: both paths are relative while the process starts outside the source tree.
            inspected = subprocess.run(['pwsh', '-NoProfile', '-File', str(root / 'tools/Inspect-Package.ps1'),
                                        '-OutputDirectory', 'project/bin/Release', '-EvidenceDirectory', 'evidence'],
                                       cwd=base, capture_output=True, text=True)
            # Assert: real metadata, ZIP and per-file validation passed at the relocated root.
            self.assertEqual(0, inspected.returncode, inspected.stdout + inspected.stderr)
            receipt = json.loads((root / 'evidence/package-inspection.json').read_text(encoding='utf-8-sig'))
            self.assertEqual('Passed', receipt['status'])
            self.assertEqual(6, len(receipt['files']))
            self.assertEqual(package.resolve(), Path(receipt['package']).resolve())
            self.assertFalse((base / 'evidence').exists())

            for assembly_name, type_name, expected in [
                    ('Exportaramus.ConsumerKit.V1_2', 'CollectionDataIpcClient', 'Reference consumer or test type ships'),
                    ('UnrelatedAssembly', 'PortableFixture', 'Runtime assembly identity differs')]:
                with self.subTest(contract_assembly=assembly_name, type_name=type_name):
                    # Arrange: substitute an otherwise byte-consistent ZIP entry into the contract DLL slot.
                    compiled = subprocess.run(['pwsh', '-NoProfile', '-File', str(compiler), '-Destination',
                                               str(output / 'Exportaramus.ConsumerKit.V1_2.dll'), '-AssemblyName', assembly_name,
                                               '-TypeName', type_name], capture_output=True, text=True)
                    self.assertEqual(0, compiled.returncode, compiled.stdout + compiled.stderr)
                    with zipfile.ZipFile(package, 'w') as archive:
                        for name in ('Exportaramus.ConsumerKit.V1_1.dll', 'Exportaramus.ConsumerKit.V1_2.dll',
                                     'Exportaramus.deps.json', 'Exportaramus.dll', 'Exportaramus.json', 'images/icon.png'):
                            archive.write(output / name, name)
                    # Act.
                    rejected = subprocess.run(['pwsh', '-NoProfile', '-File', str(root / 'tools/Inspect-Package.ps1'),
                                               '-OutputDirectory', 'project/bin/Release', '-EvidenceDirectory', 'rejected-evidence'],
                                              cwd=base, capture_output=True, text=True)
                    # Assert: package/local parity alone cannot certify an unintended runtime contract assembly.
                    self.assertNotEqual(0, rejected.returncode)
                    self.assertIn(expected, rejected.stdout + rejected.stderr)
                    self.assertFalse((root / 'rejected-evidence/package-inspection.json').exists())


if __name__ == '__main__':
    unittest.main()
