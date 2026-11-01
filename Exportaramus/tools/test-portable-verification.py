"""Exercise portable PowerShell runners from a relocated tree with isolated tool/result fixtures."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from copy import deepcopy

parser = argparse.ArgumentParser()
parser.add_argument('--powershell', default=shutil.which('pwsh') or shutil.which('powershell'))
options, remaining = parser.parse_known_args()
if not options.powershell:
    parser.error('PowerShell is required; pass --powershell with its executable path.')
ROOT = Path(__file__).resolve().parents[1]


class PortableVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='exportaramus portable ')
        self.root = Path(self.temporary.name)
        (self.root / 'tools').mkdir()
        (self.root / 'tests').mkdir()
        shutil.copyfile(ROOT / 'tools/VerificationEnvironment.ps1', self.root / 'tools/VerificationEnvironment.ps1')
        for name in ['test-focused.ps1', 'test-all.ps1', 'run-quality-gates.ps1']:
            shutil.copyfile(ROOT / 'tests' / name, self.root / 'tests' / name)

    def tearDown(self):
        self.temporary.cleanup()

    def ps(self, body, success=True):
        script = self.root / 'check.ps1'
        script.write_text("$ErrorActionPreference = 'Stop'\n. \"$PSScriptRoot/tools/VerificationEnvironment.ps1\"\n" + body, encoding='utf-8')
        actual = subprocess.run([options.powershell, '-NoProfile', '-File', str(script)], cwd=self.root,
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(actual.returncode == 0, success, actual.stdout + actual.stderr)
        return actual.stdout.strip()

    def trx(self, passed=1, skipped=0):
        counters = dict(total=passed + skipped, executed=passed, passed=passed, failed=0, error=0, timeout=0,
                        aborted=0, inconclusive=0, passedButRunAborted=0, notRunnable=0, notExecuted=skipped,
                        disconnected=0, warning=0, inProgress=0, pending=0)
        attributes = ' '.join(f'{key}="{value}"' for key, value in counters.items())
        rows = '<UnitTestResult outcome="Passed" />' * passed
        (self.root / 'result.trx').write_text(
            f'<TestRun><Results>{rows}</Results><TestSettings><Deployment runDeploymentRoot="deployment" /></TestSettings>'
            f'<ResultSummary outcome="Completed"><Counters {attributes} />'
            '<CollectorDataEntries><Collector><UriAttachments><UriAttachment><A href="coverage.cobertura.xml" />'
            '</UriAttachment></UriAttachments></Collector></CollectorDataEntries></ResultSummary></TestRun>', encoding='utf-8')

    def coverage(self, covered=1, valid=1):
        (self.root / 'coverage.cobertura.xml').write_text(
            f'<coverage lines-covered="{covered}" lines-valid="{valid}" branches-covered="2" branches-valid="2">'
            '<packages><package><classes><class name="Example.Reader"><lines>'
            f'<line number="1" hits="{covered}" branch="true" condition-coverage="100% (2/2)" />'
            '</lines></class><class name="TestHarness"><lines><line number="3" hits="0" /></lines></class>'
            '</classes></package></packages></coverage>', encoding='utf-8')
        attachment = self.root / 'deployment/In/coverage.cobertura.xml'
        attachment.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(self.root / 'coverage.cobertura.xml', attachment)

    def test_tool_resolution_and_unique_output_directories_work_after_relocation(self):
        # Arrange.
        tool = self.root / 'my tool.ps1'
        tool.write_text('exit 0', encoding='utf-8')
        # Act.
        result = json.loads(self.ps(r'''$tool = Resolve-VerificationTool -Command "$PSScriptRoot/my tool.ps1"
$a = New-VerificationRun -ProjectRoot $PSScriptRoot -Name sample
$b = New-VerificationRun -ProjectRoot $PSScriptRoot -Name sample
@{tool=$tool;first=$a;second=$b} | ConvertTo-Json'''))
        # Assert.
        self.assertEqual(Path(result['tool']).resolve(), tool.resolve())
        self.assertNotEqual(result['first'], result['second'])
        for key in ['first', 'second']:
            self.assertTrue(Path(result[key]).is_dir())
            self.assertTrue(Path(result[key]).resolve().is_relative_to((self.root / 'development/verification').resolve()))

    def test_skips_and_zero_test_runs_cannot_pass(self):
        # Arrange, Act, Assert.
        for passed, skipped in [(1, 1), (0, 0)]:
            self.trx(passed, skipped)
            self.ps('Read-VerificationTests -Trx "$PSScriptRoot/result.trx"', success=False)

    def test_aborted_or_incomplete_run_cannot_pass_using_only_positive_test_counts(self):
        # Arrange: finish individual tests but leave the run incomplete.
        for bad in ['outcome="Aborted"', 'outcome="Completed"']:
            self.trx()
            trx = self.root / 'result.trx'
            contents = trx.read_text().replace('outcome="Completed"', bad)
            if bad == 'outcome="Completed"':
                contents = contents.replace('aborted="0"', 'aborted="1"')
            trx.write_text(contents, encoding='utf-8')
            # Act and Assert.
            self.ps('Read-VerificationTests -Trx "$PSScriptRoot/result.trx"', success=False)

    def test_ambiguous_summary_counters_or_results_cannot_select_one_favorable_record(self):
        for duplicate in ['summary', 'counters', 'results']:
            # Arrange: duplicate an otherwise valid result.
            self.trx()
            path = self.root / 'result.trx'
            document = ET.parse(path)
            root = document.getroot()
            if duplicate == 'summary':
                root.append(deepcopy(root.find('ResultSummary')))
            elif duplicate == 'results':
                root.append(deepcopy(root.find('Results')))
            else:
                summary = root.find('ResultSummary')
                summary.append(deepcopy(summary.find('Counters')))
            document.write(path, encoding='utf-8')
            # Act and Assert.
            self.ps('Read-VerificationTests -Trx "$PSScriptRoot/result.trx"', success=False)

    def test_execution_counters_and_passed_rows_must_describe_the_same_completed_run(self):
        for fault in ['not-executed', 'missing-counter', 'missing-row', 'failed-row']:
            # Arrange: report totals without completed result rows.
            self.trx()
            path = self.root / 'result.trx'
            document = ET.parse(path)
            root = document.getroot()
            counters = root.find('ResultSummary/Counters')
            if fault == 'not-executed':
                counters.set('executed', '0')
            elif fault == 'missing-counter':
                del counters.attrib['error']
            elif fault == 'missing-row':
                root.find('Results').remove(root.find('Results/UnitTestResult'))
            else:
                root.find('Results/UnitTestResult').set('outcome', 'Failed')
            document.write(path, encoding='utf-8')
            # Act and Assert.
            self.ps('Read-VerificationTests -Trx "$PSScriptRoot/result.trx"', success=False)

    def test_component_coverage_requires_exact_type_and_all_measured_branches(self):
        # Arrange: leave harness code uncovered while covering the reader.
        self.trx()
        self.coverage()
        # Act.
        value = json.loads(self.ps('Read-VerificationCoverage -Run $PSScriptRoot -Trx "$PSScriptRoot/result.trx" -TypePrefix Example.Reader | ConvertTo-Json -Depth 5'))
        # Assert.
        self.assertEqual((1, 1, 2, 2), tuple(value[k] for k in ['linesCovered', 'linesValid', 'branchesCovered', 'branchesValid']))
        self.ps('Read-VerificationCoverage -Run $PSScriptRoot -Trx "$PSScriptRoot/result.trx" -TypePrefix Absent.Reader', success=False)
        report = self.root / 'coverage.cobertura.xml'
        report.write_text(report.read_text().replace('100% (2/2)', '50% (1/2)'), encoding='utf-8')
        shutil.copyfile(report, self.root / 'deployment/In/coverage.cobertura.xml')
        self.ps('Read-VerificationCoverage -Run $PSScriptRoot -Trx "$PSScriptRoot/result.trx" -TypePrefix Example.Reader', success=False)

    def test_missing_or_conflicting_coverage_reports_cannot_pass(self):
        # Arrange.
        self.trx()
        # Act and Assert.
        self.ps('Read-VerificationCoverage -Run $PSScriptRoot -Trx "$PSScriptRoot/result.trx"', success=False)

        self.coverage()
        duplicate = self.root / 'attachment'
        duplicate.mkdir()
        (duplicate / 'coverage.cobertura.xml').write_text('<coverage />', encoding='utf-8')
        self.ps('Read-VerificationCoverage -Run $PSScriptRoot -Trx "$PSScriptRoot/result.trx"', success=False)

    def test_nested_coverage_or_missing_branch_counts_cannot_certify_the_primary_component(self):
        for fault in ['nested-only', 'missing-branch-counts']:
            # Arrange: keep attachments but remove required component evidence.
            self.trx()
            self.coverage()
            report = self.root / 'coverage.cobertura.xml'
            contents = report.read_text()
            if fault == 'nested-only':
                contents = contents.replace('name="Example.Reader"', 'name="Example.Reader/Nested"')
            else:
                contents = contents.replace('condition-coverage="100% (2/2)"', '')
            report.write_text(contents, encoding='utf-8')
            shutil.copyfile(report, self.root / 'deployment/In/coverage.cobertura.xml')
            # Act and Assert.
            self.ps('Read-VerificationCoverage -Run $PSScriptRoot -Trx "$PSScriptRoot/result.trx" -TypePrefix Example.Reader', success=False)

    def test_unrelated_report_does_not_replace_a_missing_trx_attachment(self):
        # Arrange: keep a collector copy but remove the file named by the TRX.
        self.trx()
        self.coverage()
        (self.root / 'deployment/In/coverage.cobertura.xml').unlink()
        # Act and Assert.
        self.ps('Read-VerificationCoverage -Run $PSScriptRoot -Trx "$PSScriptRoot/result.trx"', success=False)

    def test_coverage_attachment_cannot_escape_its_fresh_run_directory(self):
        # Arrange: use a traversal path despite a valid report elsewhere.
        self.trx()
        self.coverage()
        trx = self.root / 'result.trx'
        trx.write_text(trx.read_text().replace('href="coverage.cobertura.xml"', 'href="../../../coverage.cobertura.xml"'), encoding='utf-8')
        # Act and Assert.
        self.ps('Read-VerificationCoverage -Run $PSScriptRoot -Trx "$PSScriptRoot/result.trx"', success=False)

    def test_focused_runner_uses_explicit_tool_and_output_directory(self):
        # Arrange: record dotnet arguments and write the requested test receipt.
        (self.root / 'fake dotnet.ps1').write_text(r'''$args | ConvertTo-Json | Set-Content "$PSScriptRoot/arguments.json"
$index = [Array]::IndexOf($args, '--results-directory')
$directory = $args[$index + 1]
[void][IO.Directory]::CreateDirectory($directory)
'<TestRun><Results><UnitTestResult outcome="Passed" /><UnitTestResult outcome="Passed" /></Results><ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" inProgress="0" pending="0" /></ResultSummary></TestRun>' | Set-Content "$directory/focused.trx"
$global:LASTEXITCODE = 0
''', encoding='utf-8')
        # Act.
        self.ps('& "$PSScriptRoot/tests/test-focused.ps1" -Dotnet "$PSScriptRoot/fake dotnet.ps1" -Filter Requested.Case -OutputDirectory "$PSScriptRoot/results elsewhere"')
        # Assert.
        arguments = json.loads((self.root / 'arguments.json').read_text(encoding='utf-8-sig'))
        self.assertIn('Requested.Case', arguments)
        self.assertTrue(list((self.root / 'results elsewhere').rglob('focused.trx')))

    def test_mutation_request_is_rejected_before_any_tools_are_invoked(self):
        # Arrange: leave external tools unavailable in the relocated fixture.
        # Act and Assert: require the error to explain the separate mutation run.
        output = self.ps('try { & "$PSScriptRoot/tests/run-quality-gates.ps1" -RunMutation } catch { if ($_.Exception.Message -notlike "*separate manual campaign*") { throw }; "expected-boundary" }')
        self.assertEqual('expected-boundary', output)

    def test_source_changed_receipt_is_preserved_and_returns_a_nonzero_exit(self):
        # Arrange: pass every check, then change the source inventory.
        receipt = self.root / 'gate-execution.json'
        value = {'status': 'IncompleteOrFailed', 'inputsUnchanged': False,
                 'sourceSha256': 'starting-source', 'finalSourceSha256': 'changed-source',
                 'steps': [{'name': 'Build', 'status': 'Passed'}, {'name': 'Tests', 'status': 'Passed'}]}
        receipt.write_text(json.dumps(value), encoding='utf-8')

        # Act: call the runner's final receipt check.
        self.ps('Assert-VerificationOutcome -Receipt "$PSScriptRoot/gate-execution.json"', success=False)

        # Assert: return failure and keep its evidence.
        self.assertEqual(value, json.loads(receipt.read_text(encoding='utf-8')))
        orchestrator = (self.root / 'tests/run-quality-gates.ps1').read_text(encoding='utf-8')
        self.assertIn('Assert-VerificationOutcome -Receipt "$run/gate-execution.json"', orchestrator)

    def test_completed_unchanged_verification_receipt_returns_success(self):
        # Arrange.
        receipt = self.root / 'gate-execution.json'
        receipt.write_text(json.dumps({'status': 'Passed', 'inputsUnchanged': True}), encoding='utf-8')

        # Act and Assert.
        self.ps('Assert-VerificationOutcome -Receipt "$PSScriptRoot/gate-execution.json"')


if __name__ == '__main__':
    unittest.main(argv=[__file__, *remaining])
