"""Fast tool-only checks; these do not substitute for the product mutation campaign."""
import importlib.util
from pathlib import Path
import sys
import unittest
import copy
import tempfile
import json
from unittest.mock import patch
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True

def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

campaign = load('mutation-campaign')
scope = load('contract-quality-scope')
review = load('mutation_review')

class CampaignChecks(unittest.TestCase):
    def result(self, status='Killed', **changes):
        args = dict(before={'files': {'a.cs': 'one'}, 'sourceSha256': 'source', 'startedAtUtc': 'start'},
                    current={'a.cs': 'one'}, scope={'mutationInstrumentationAudit': {'notRepresented': [], 'unexpectedPaths': [], 'sourceMismatches': []}},
                    report={'files': {'a.cs': {'mutants': [{'id': '1', 'status': status}]}}}, exit_code=0, after_source_sha='source')
        args.update(changes)
        return campaign.summarize(**args)

    def test_killed_and_unreviewed_compile_errors_are_distinct(self):
        self.assertTrue(self.result()['gatePassed'])
        result = self.result('CompileError')
        self.assertFalse(result['gatePassed'])
        self.assertEqual(1, len(result['compileErrorMutants']))
        self.assertEqual([], result['unresolvedMutants'])

    def test_every_unresolved_status_fails_even_with_success_exit(self):
        for status in ('Survived', 'NoCoverage', 'Timeout', 'RuntimeError', 'Ignored', 'Pending', 'Unknown'):
            with self.subTest(status=status):
                result = self.result(status)
                self.assertFalse(result['gatePassed'])
                self.assertEqual(status, result['unresolvedMutants'][0]['status'])

    def test_missing_failed_stale_or_unaudited_runs_fail(self):
        for change in ({'report': None}, {'exit_code': 1}, {'scope': None}, {'current': {'a.cs': 'changed'}},
                       {'after_source_sha': 'other'}, {'audit_error': 'bad JSON'}, {'report': {'files': {}}}):
            with self.subTest(change=change):
                self.assertFalse(self.result(**change)['gatePassed'])

    def test_inventory_gaps_cannot_pass(self):
        for key in ('notRepresented', 'unexpectedPaths', 'duplicatePaths', 'sourceMismatches'):
            self.assertFalse(self.result(scope={'mutationInstrumentationAudit': {key: ['a.cs']}})['gatePassed'])

    def test_linked_paths_resolve_lexically(self):
        path = r'D:\\repo\\tests\\Testable\\..\\..\\src\\Feature.cs'
        self.assertEqual('D:/repo/src/Feature.cs', scope.normalize_report_path(path))
        self.assertEqual('../../src/Feature.cs', scope.normalize_report_path('../../src/Feature.cs'))

    def test_review_preserves_raw_score_and_requires_completed_threshold_failure(self):
        audit = {'mutationInstrumentationAudit': {'instrumentedFiles': {'a.cs': {'reportPath': 'a.cs'}}}}
        args = dict(scope=audit, accepted={('a.cs', '1')}, exit_code=2)
        self.assertFalse(self.result('Survived', **args)['gatePassed'])
        result = self.result('Survived', completed_threshold_failure=True, **args)
        self.assertTrue(result['gatePassed'])
        self.assertEqual(0, result['rawMutationScore'])
        self.assertEqual(1, result['statusCounts']['Survived'])
        self.assertEqual([], result['unresolvedMutants'])
        args['exit_code'] = 1
        self.assertFalse(self.result('Survived', completed_threshold_failure=True, **args)['gatePassed'])

class PartialCampaignChecks(unittest.TestCase):
    def test_failed_targets_keep_successful_reports_without_passing_the_gate(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            output = run / 'consumerkit12'
            output.mkdir()
            report = output / 'mutation-report.json'
            report.write_text(json.dumps({'files': {'Client.cs': {'mutants': [
                {'status': 'Killed'}, {'status': 'Survived'}, {'status': 'NoCoverage'}]}}}))
            original = report.read_bytes()
            results = campaign.partial_results(run)
            self.assertEqual(4, len(results))
            self.assertEqual(3, sum(item['totalMutants'] for item in results))
            self.assertEqual(3, sum('error' in item for item in results))
            self.assertEqual(original, report.read_bytes())
            self.assertEqual(campaign.digest(report), results[2]['reportSha256'])
            self.assertFalse(CampaignChecks().result(report=None, exit_code=1)['gatePassed'])

    def test_finish_writes_partial_counts_without_qualifying_an_incomplete_campaign(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            before = {'files': {}, 'sourceSha256': 'source', 'startedAtUtc': 'start',
                      'linkedSourceCount': 1, 'schemaVersion': '2.0'}
            (run / 'input-manifest.json').write_text(json.dumps(before))
            (run / 'source-after.json').write_text(json.dumps({'sha256': 'source'}))
            for name in ('consumerkit12', 'inventoryreader'):
                target = run / name
                target.mkdir()
                (target / 'mutation-report.json').write_text(json.dumps({'files': {
                    'Example.cs': {'mutants': [{'id': '1', 'status': 'Killed'}]}}}))
            with patch.object(campaign, 'input_hashes', return_value={}), patch.object(campaign, 'audit_scope', return_value={}):
                self.assertEqual(1, campaign.finish(run, 1))
            summary = json.loads((run / 'mutation-campaign-summary.json').read_bytes())
            self.assertFalse(summary['gatePassed'])
            self.assertEqual('failed', summary['status'])
            self.assertIsNone(summary['report'])
            self.assertEqual(2, summary['partialTotalMutants'])
            self.assertEqual(4, len(summary['partialResults']))

    def test_malformed_and_duplicate_reports_are_not_counted(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            target = run / 'testable'
            target.mkdir()
            (target / 'mutation-report.json').write_text('{')
            self.assertIn('error', campaign.partial_results(run)[0])
            nested = target / 'other'
            nested.mkdir()
            (nested / 'mutation-report.json').write_text('{}')
            result = campaign.partial_results(run)[0]
            self.assertEqual(2, result['reportCount'])
            self.assertEqual(0, result['totalMutants'])


class CombinedCampaignChecks(unittest.TestCase):
    def test_supplemental_configuration_cannot_weaken_primary_campaign(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'tests').mkdir()
            for name in [item['config'].name for item in campaign.campaign_definitions(campaign.ROOT)]:
                (root / 'tests' / name).write_bytes((campaign.ROOT / 'tests' / name).read_bytes())
            campaign.validate_assembly_configs(root)
            path = root / 'tests/stryker-consumerkit-1.2-config.json'
            config = json.loads(path.read_bytes())
            config['stryker-config']['ignore-mutations'] = ['Boolean']
            path.write_text(json.dumps(config))
            with self.assertRaisesRegex(ValueError,'identical strict'):
                campaign.validate_assembly_configs(root)

    def test_only_uninstrumented_performance_is_filtered(self):
        for definition in campaign.campaign_definitions(campaign.ROOT):
            config = json.loads(definition['config'].read_bytes())['stryker-config']
            self.assertEqual('Category!=UninstrumentedPerformance', config['test-case-filter'])
            self.assertEqual(['**/*'], config['mutate'])
            self.assertEqual({'high': 100, 'low': 100, 'break': 100}, config['thresholds'])

    def test_both_complete_raw_reports_are_required_and_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'tools').mkdir()
            # Real scope resolver with a temporary project tree.
            (root / 'tools/contract-quality-scope.py').write_bytes(Path(scope.__file__).read_bytes())
            projects = {item['name']: item['project'] for item in campaign.campaign_definitions(root)}
            run = root / 'run'
            run.mkdir()
            executions = []
            originals = {}
            for name, project in projects.items():
                project.parent.mkdir(parents=True)
                project.write_text('<Project><ItemGroup><Compile Include="Decision.cs" /></ItemGroup></Project>')
                (project.parent / 'Decision.cs').write_text('return true;')
                output = run / name
                output.mkdir()
                report = output / 'mutation-report.json'
                report.write_text(json.dumps({'files': {'Decision.cs': {'source':'return true;', 'mutants':[{'id':'1','status':'Killed'}]}}}))
                originals[name] = report.read_bytes()
                (output / 'console.log').write_text('The final mutation score is 100%')
                executions.append({'name':name,'exitCode':0})
            (run / 'executions.json').write_text(json.dumps(executions))
            with patch.object(campaign, 'ROOT', root):
                self.assertEqual(0, campaign.combine(run))
                merged = json.loads((run / 'combined/mutation-report.json').read_bytes())
                self.assertEqual(4,len(merged['files']))
                self.assertEqual(4,sum(len(entry['mutants']) for entry in merged['files'].values()))
                self.assertEqual({'0','1','2','3'}, {mutant['id'] for entry in merged['files'].values() for mutant in entry['mutants']})
                for name in projects:
                    self.assertEqual(originals[name],(run / name / 'mutation-report.json').read_bytes())
                campaign.validate_combined_evidence(run)
                manifest_path = run / 'constituent-campaigns.json'
                manifest = json.loads(manifest_path.read_bytes())
                broken = copy.deepcopy(manifest)
                broken['campaigns'][0]['exitCode'] = 1
                manifest_path.write_text(json.dumps(broken))
                with self.assertRaisesRegex(ValueError, 'Incomplete or failed'):
                    campaign.validate_combined_evidence(run)
                broken = copy.deepcopy(manifest)
                broken['campaigns'].pop()
                manifest_path.write_text(json.dumps(broken))
                with self.assertRaisesRegex(ValueError, 'Every declared'):
                    campaign.validate_combined_evidence(run)
                manifest_path.write_text(json.dumps(manifest))
                merged['files'][next(iter(merged['files']))]['mutants'][0]['status'] = 'Survived'
                (run / 'combined/mutation-report.json').write_text(json.dumps(merged))
                with self.assertRaisesRegex(ValueError,'differs from the complete'):
                    campaign.validate_combined_evidence(run)
                executions[1]['exitCode'] = 1
                (run / 'executions.json').write_text(json.dumps(executions))
                with self.assertRaisesRegex(ValueError,'Incomplete or failed'):
                    campaign.combine(run)
                (run / 'executions.json').write_text(json.dumps(executions[:1]))
                with self.assertRaisesRegex(ValueError,'Exactly all'):
                    campaign.combine(run)

class LedgerChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        evidence = self.root / 'proof.txt'
        evidence.write_text('Individual behavior proof')
        self.mutant = {'id': '7', 'status': 'Survived', 'location': {'start': {'line': 1, 'column': 1}, 'end': {'line': 1, 'column': 2}}, 'replacement': 'false', 'mutatorName': 'Boolean'}
        self.before = {'sourceSha256': 'whole'}
        self.scope = {'handwrittenSourceHashes': {'src/a.cs': 'filehash'}, 'mutationInstrumentationAudit': {'instrumentedFiles': {'src/a.cs': {'reportPath': 'raw/a.cs'}}, 'notRepresented': []}}
        self.report = {'files': {'raw/a.cs': {'mutants': [self.mutant]}}}
        self.entry = dict(self.mutant, file='src/a.cs', sourceSha256='filehash', classification='Equivalent', reason='Observable behavior remains identical for every input.', evidence=[{'path': 'proof.txt', 'sha256': review.sha256(evidence)}])
        self.ledger = {'schemaVersion': '1.0', 'sourceSha256': 'whole', 'reportSha256': 'report', 'reviewer': 'Independent reviewer', 'entries': [self.entry]}

    def tearDown(self):
        self.temporary.cleanup()

    def resolve(self):
        return review.reconcile(self.ledger, self.before, 'report', self.scope, self.report, self.root)

    def test_exact_review_and_individual_nonviability(self):
        self.assertEqual(({('src/a.cs', '7')}, set(), []), self.resolve())
        self.mutant['status'] = self.entry['status'] = 'CompileError'
        self.entry['classification'] = 'IndividuallyNonViable'
        self.assertEqual(({('src/a.cs', '7')}, set(), []), self.resolve())

    def test_uncovered_kill_requires_repair_and_exact_execution_validation(self):
        self.mutant['status'] = self.entry['status'] = 'NoCoverage'
        self.entry['classification'] = 'IndependentlyKilled'
        self.assertTrue(self.resolve()[2])
        with patch.object(review, 'independently_killed', return_value=False) as check:
            result = review.reconcile(self.ledger, self.before, 'report', self.scope, self.report, self.root, repair={'bound': True})
            self.assertFalse(result[0])
            check.assert_called_once()
        with patch.object(review, 'independently_killed', return_value=True):
            result = review.reconcile(self.ledger, self.before, 'report', self.scope, self.report, self.root, repair={'bound': True})
            self.assertEqual({('src/a.cs', '7')}, result[0])
        self.entry['classification'] = 'Equivalent'
        self.assertTrue(self.resolve()[2])

    def test_timeouts_uncovered_and_runtime_errors_cannot_be_waived(self):
        for status in ('Timeout', 'NoCoverage', 'RuntimeError', 'Pending', 'Killed'):
            self.mutant['status'] = self.entry['status'] = status
            accepted, _, errors = self.resolve()
            self.assertFalse(accepted)
            self.assertTrue(errors)

    def test_stale_duplicate_or_unsubstantiated_reviews_fail(self):
        pristine = copy.deepcopy(self.ledger)
        for mutate in (lambda x: x.update(reportSha256='other'), lambda x: x['entries'][0].update(sourceSha256='old'),
                       lambda x: x['entries'][0].update(replacement='different'), lambda x: x['entries'][0].update(evidence=[]),
                       lambda x: x['entries'].append(copy.deepcopy(x['entries'][0])),
                       lambda x: x['entries'][0].update(supportingKilledMutantIds=['missing'])):
            self.ledger = copy.deepcopy(pristine)
            mutate(self.ledger)
            self.assertTrue(self.resolve()[2])

    def test_filtered_block_needs_exact_reason_and_explicit_evidence(self):
        self.mutant['status'] = self.entry['status'] = 'Ignored'
        self.entry['classification'] = 'RedundantBlock'
        self.assertTrue(self.resolve()[2])
        self.mutant['statusReason'] = review.BLOCK_REASON
        self.assertFalse(self.resolve()[2])

    def test_source_only_review_is_bound_to_exact_missing_file(self):
        self.ledger['entries'] = []
        self.scope['mutationInstrumentationAudit']['notRepresented'] = ['src/a.cs']
        entry = dict(self.entry, classification='NoExecutableMutants')
        self.ledger['sourceReviews'] = [entry]
        self.assertEqual(set(), self.resolve()[0])
        self.assertEqual({'src/a.cs'}, self.resolve()[1])
        entry['sourceSha256'] = 'stale'
        self.assertTrue(self.resolve()[2])

    def independent_proof(self):
        source = self.root / 'src/a.cs'
        source.parent.mkdir()
        source.write_bytes(b'xy')
        self.scope['handwrittenSourceHashes']['src/a.cs'] = self.entry['sourceSha256'] = review.sha256(source)
        self.mutant['status'] = self.entry['status'] = 'Ignored'
        self.mutant['statusReason'] = review.BLOCK_REASON
        self.entry['classification'] = 'IndependentlyKilled'
        self.before['files'] = {'tools/individual-mutation.py': 'executor'}
        mutated = self.root / 'mutated.cs'
        mutated.write_bytes(b'falsey')
        def execution(name, failed):
            trx = self.root / (name + '.trx')
            counts = dict(total=2, executed=2, passed=2-failed, failed=failed, notExecuted=0)
            values = ' '.join(f'{key}="{value}"' for key, value in counts.items())
            rows = ''.join(f'<UnitTestResult outcome="{outcome}"/>' for outcome in ['Passed'] * (2-failed) + ['Failed'] * failed)
            others = ' '.join(f'{key}="0"' for key in ('aborted', 'timeout', 'error', 'notRunnable', 'disconnected',
                                'inconclusive', 'passedButRunAborted', 'warning', 'completed', 'inProgress', 'pending'))
            outcome = 'Failed' if failed else 'Completed'
            trx.write_text(f'<TestRun><Results>{rows}</Results><ResultSummary outcome="{outcome}"><Counters {values} {others}/></ResultSummary></TestRun>')
            log = self.root / (name + '.log')
            log.write_text('completed')
            return dict(command=['dotnet', 'test', 'Exportaramus.Tests.csproj'], exitCode=1 if failed else 0,
                        trx=str(trx), trxSha256=review.sha256(trx), counts=counts, log=str(log), logSha256=review.sha256(log))
        self.independent = dict(format='exportaramus-independent-mutation-shard', schemaVersion='1.0', sourceSha256='whole',
                                reportSha256='report', executorSha256='executor', restoredInputsUnchanged=True, changedInputs=[],
                                baseline=dict(build=dict(exitCode=0), tests=execution('baseline', 0)),
                                results=[dict(file='src/a.cs', mutant=dict(self.mutant), outcome='Killed', sourceSha256=self.entry['sourceSha256'],
                                              mutatedSource=str(mutated), mutatedSourceSha256=review.sha256(mutated), build=dict(exitCode=0), tests=execution('mutant', 1))])
        self.save_independent()

    def save_independent(self):
        path = self.root / 'independent.json'
        path.write_text(campaign.json.dumps(self.independent))
        self.entry['independentExecution'] = dict(summary=str(path), sha256=review.sha256(path))

    def test_baseline_only_policy_requires_collection_and_exact_mutant_command(self):
        import suite_binding
        self.independent_proof()
        settings = self.root / 'tests/coverage.runsettings'
        settings.parent.mkdir(exist_ok=True)
        settings.write_text('<RunSettings><DataCollectionRunSettings><DataCollectors><DataCollector friendlyName="XPlat Code Coverage"/></DataCollectors></DataCollectionRunSettings></RunSettings>')
        self.before['files'].update({'tools/suite_binding.py': review.sha256(Path(suite_binding.__file__)),
                                    'tests/coverage.runsettings': review.sha256(settings)})
        self.independent.update(testCoveragePolicy='baseline-only-v1', snapshot=str(self.root),
                                toolchain={'dotnet': {'path': 'dotnet'}})
        baseline = self.independent['baseline']['tests']
        mutant = self.independent['results'][0]['tests']
        for execution, phase in ((baseline, 'baseline'), (mutant, 'mutant')):
            execution['workingDirectory'] = str(self.root)
            execution['command'] = suite_binding.command('dotnet', 'test', self.root, 'provider', Path(execution['trx']).parent, phase)
        self.save_independent()
        self.assertFalse(self.resolve()[2])
        pristine = copy.deepcopy(self.independent)
        for change in ('baseline', 'filter', 'policy'):
            self.independent = copy.deepcopy(pristine)
            if change == 'baseline':
                self.independent['baseline']['tests']['command'] = suite_binding.command('dotnet', 'test', self.root, 'provider', self.root, 'mutant')
            elif change == 'filter': self.independent['results'][0]['tests']['command'].append('--filter')
            else: self.independent['testCoveragePolicy'] = 'skip'
            self.save_independent()
            self.assertTrue(self.resolve()[2])

    def test_independent_kill_requires_exact_source_and_full_test_evidence(self):
        self.independent_proof()
        self.assertFalse(self.resolve()[2])
        pristine = copy.deepcopy(self.independent)
        for mutate in (lambda x: x.update(sourceSha256='old'), lambda x: x.update(reportSha256='old'),
                       lambda x: x.update(executorSha256='other'), lambda x: x.update(restoredInputsUnchanged=False),
                       lambda x: x['results'][0].update(outcome='Survived'),
                       lambda x: x['results'][0]['tests']['command'].append('--filter=OnlyOne'),
                       lambda x: x['results'][0]['tests']['counts'].update(executed=1),
                       lambda x: x['results'][0]['tests'].update(trxSha256='changed'),
                       lambda x: x['results'][0]['tests'].update(timedOut=True),
                       lambda x: x['results'][0]['mutant'].update(replacement='other'),
                       lambda x: x['results'][0].update(mutatedSourceSha256='wrong'),
                       lambda x: x['baseline']['tests'].update(exitCode=1)):
            self.independent = copy.deepcopy(pristine)
            mutate(self.independent)
            self.save_independent()
            self.assertTrue(self.resolve()[2])

    def test_independent_proof_cannot_waive_a_timeout_or_uncovered_mutant(self):
        self.independent_proof()
        for status in ('Timeout', 'NoCoverage', 'RuntimeError', 'Survived', 'Pending'):
            self.mutant['status'] = self.entry['status'] = status
            self.independent['results'][0]['mutant']['status'] = status
            self.save_independent()
            self.assertTrue(self.resolve()[2])

    def test_hash_valid_aborted_or_inconsistent_trx_cannot_prove_a_kill(self):
        self.independent_proof()
        self.assertFalse(self.resolve()[2])
        path = Path(self.independent['results'][0]['tests']['trx'])
        normal = path.read_bytes()
        changes = {
            'aborted summary': lambda root: root.find('ResultSummary').set('outcome', 'Aborted'),
            'missing outcome': lambda root: root.find('ResultSummary').attrib.pop('outcome'),
            'completed despite failure': lambda root: root.find('ResultSummary').set('outcome', 'Completed'),
            'duplicate summary': lambda root: root.append(ET.fromstring('<ResultSummary outcome="Failed"/>')),
            'missing summary': lambda root: root.remove(root.find('ResultSummary')),
            'missing count': lambda root: root.find('ResultSummary/Counters').attrib.pop('failed'),
            'missing abort count': lambda root: root.find('ResultSummary/Counters').attrib.pop('aborted'),
            'negative count': lambda root: root.find('ResultSummary/Counters').set('passed', '-1'),
            'duplicate counters': lambda root: root.find('ResultSummary').append(ET.fromstring('<Counters/>')),
            'nested counters': lambda root: root.find('ResultSummary').append(ET.fromstring('<Extension><Counters/></Extension>')),
            'mismatched rows': lambda root: root.find('Results')[1].set('outcome', 'Passed'),
            'skipped row': lambda root: root.find('Results')[1].set('outcome', 'NotExecuted'),
            'run still pending': lambda root: root.find('ResultSummary/Counters').set('pending', '1'),
        }
        for name, change in changes.items():
            with self.subTest(name=name):
                root = ET.fromstring(normal)
                change(root)
                ET.ElementTree(root).write(path)
                self.independent['results'][0]['tests']['trxSha256'] = review.sha256(path)
                self.save_independent()
                self.assertTrue(self.resolve()[2])

if __name__ == '__main__':
    unittest.main()
