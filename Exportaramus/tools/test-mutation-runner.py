"""Exercise wrapper control flow with isolated fake campaigns, never product mutants."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

parser = argparse.ArgumentParser()
parser.add_argument('--runner', default=os.environ.get('STRYKER_RUNNER'))
options, remaining = parser.parse_known_args()
if not options.runner:
    parser.error('Pass --runner or set STRYKER_RUNNER to the installed Stryker 4.16.0 DLL.')
ROOT = Path(__file__).resolve().parents[1]


class MutationRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='mutation wrapper ')
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name).resolve()
        self.root = self.base / 'source'
        self.output = self.base / 'results'
        for name in ('tests/test-mutation.ps1', 'tools/Get-SourceInventory.ps1', 'tools/VerificationEnvironment.ps1'):
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / name, target)
        (self.root / 'tools/mutation-campaign.py').write_text('''import json,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
mode=sys.argv[1]
if mode=='campaigns':
 print(json.dumps([dict(name=name,workingDirectory=str(root),config=str(root/(name+'.json')),testProject=test) for name,test in [('provider','Provider.csproj'),('contracts','Provider.csproj'),('consumer','Consumer.csproj'),('reader','Reader.csproj')]]))
elif mode=='prepare':
 Path(sys.argv[sys.argv.index('--run-dir')+1]).mkdir(parents=True,exist_ok=True)
''', encoding='utf-8')
        self.dotnet = self.base / 'dotnet fixture.ps1'
        self.dotnet.write_text('''$ErrorActionPreference='Stop'
ConvertTo-Json -InputObject @($args) -Compress | Add-Content -LiteralPath $env:FAKE_CALL_LOG
if ($args[0] -eq 'test') {
    $index=[Array]::IndexOf($args,'--results-directory')
    $directory=$args[$index+1]
    [void][IO.Directory]::CreateDirectory($directory)
    $skipped=if($env:FAKE_MODE -eq 'skipped'){1}else{0}
    $passed=1-$skipped
    $outcome=if($skipped){'NotExecuted'}else{'Passed'}
    $xml="<TestRun><Results><UnitTestResult outcome='$outcome'/></Results><ResultSummary outcome='Completed'><Counters total='1' executed='$passed' passed='$passed' failed='0' error='0' timeout='0' aborted='0' inconclusive='0' passedButRunAborted='0' notRunnable='0' notExecuted='$skipped' disconnected='0' warning='0' inProgress='0' pending='0'/></ResultSummary></TestRun>"
    $xml | Set-Content -LiteralPath (Join-Path $directory 'baseline.trx')
}
if ($args -contains '--config-file' -and $env:FAKE_MODE -eq 'mutation-failure') { exit 7 }
exit 0
''', encoding='utf-8')

    def run_wrapper(self, setup=False, mode='', runner=None):
        command = [shutil.which('pwsh'), '-NoProfile', '-File', str(self.root / 'tests/test-mutation.ps1'),
                   '-Runner', runner or options.runner, '-Dotnet', str(self.dotnet), '-Python', sys.executable,
                   '-RunDirectory', str(self.output), '-Concurrency', '2']
        if setup:
            command.append('-SetupOnly')
        env = {**os.environ, 'FAKE_MODE': mode, 'FAKE_CALL_LOG': str(self.base / 'calls.jsonl'), 'PYTHONDONTWRITEBYTECODE': '1'}
        return subprocess.run(command, cwd=self.root, env=env, capture_output=True, text=True, timeout=30)

    def calls(self):
        return [json.loads(line) for line in (self.base / 'calls.jsonl').read_text(encoding='utf-8-sig').splitlines()]

    def test_setup_checks_distinct_suites_without_executing_or_passing_mutations(self):
        result = self.run_wrapper(setup=True)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        receipt = json.loads((self.output / 'setup-verification.json').read_text(encoding='utf-8-sig'))
        self.assertEqual('Ready', receipt['status'])
        self.assertFalse(receipt['mutationExecuted'])
        self.assertFalse(receipt['gatePassed'])
        self.assertEqual(4, receipt['campaignCount'])
        self.assertEqual(3, len(receipt['baselines']))
        self.assertEqual(3, sum(call[0] == 'test' for call in self.calls()))
        self.assertFalse(any('--config-file' in call for call in self.calls()))

    def test_setup_rejects_skipped_baseline_tests(self):
        result = self.run_wrapper(setup=True, mode='skipped')
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.output / 'setup-verification.json').exists())

    def test_failed_campaigns_preserve_all_four_execution_statuses(self):
        result = self.run_wrapper(mode='mutation-failure')
        self.assertNotEqual(0, result.returncode)
        executions = json.loads((self.output / 'executions.json').read_text(encoding='utf-8-sig'))
        self.assertEqual(4, len(executions))
        self.assertTrue(all(item['exitCode'] == 7 for item in executions))
        self.assertEqual(4, sum('--config-file' in call for call in self.calls()))
        self.assertTrue(all(call[call.index('--concurrency') + 1] == '2' for call in self.calls()))

    def test_incorrect_tool_version_fails_before_creating_evidence(self):
        result = self.run_wrapper(setup=True, runner=sys.executable)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Expected Stryker 4.16.0', result.stderr)
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main(argv=[sys.argv[0], *remaining])
