"""Tool-only checks for exact mutation execution and fail-closed evidence handling."""
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import tempfile
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('individual_mutation', Path(__file__).with_name('individual-mutation.py'))
executor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(executor)


class IndependentExecutionChecks(unittest.TestCase):
    def test_dotnet_resolution_rejects_missing_command(self):
        with patch.object(executor.shutil, 'which', return_value=None), self.assertRaisesRegex(ValueError, 'PATH'):
            executor.resolve_dotnet('missing-dotnet')

    def test_sdk_compiler_uses_reported_installation_with_spaces(self):
        with tempfile.TemporaryDirectory(prefix='sdk test ') as directory:
            root = Path(directory)
            compiler = root / '10.0.400/Roslyn/bincore/csc.dll'
            compiler.parent.mkdir(parents=True)
            compiler.write_bytes(b'compiler')
            with patch.object(executor.subprocess, 'check_output', return_value=f'10.0.400 [{root}]\n'):
                self.assertEqual(compiler.resolve(), executor.sdk_compiler(Path('unrelated/dotnet'), '10.0.400', root))
            for output in ('', f'10.0.400 [{root}]\n10.0.400 [{root}]\n'):
                with patch.object(executor.subprocess, 'check_output', return_value=output), self.assertRaises(ValueError):
                    executor.sdk_compiler(Path('dotnet'), '10.0.400', root)

    def test_provider_only_executor_cannot_misclassify_unexecuted_consumer_mutants(self):
        executor.require_provider_mutants([('src/Contracts/1.2/CollectionDataContracts.cs', '', {})])
        for path in ('src/Contracts/1.2/Consumer/CollectionDataIpcClient.cs',
                     'examples/CollectionExample/InventoryItemReader.cs'):
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, 'provider-only'):
                executor.require_provider_mutants([(path, '', {})])

    def _tests(self, failed=0, total=3, executed=3, skipped=0, code=0):
        return {'exitCode': code, 'timedOut': False, 'counts': {'total': total, 'executed': executed,
                'passed': executed - failed, 'failed': failed, 'notExecuted': skipped},
                'resultRows': executed, 'otherCounts': {}, 'summaryOutcome': 'Failed' if failed else 'Completed',
                'outcomes': {'Passed': executed - failed, 'Failed': failed}}

    def test_actual_trx_structure_rejects_aborted_abbreviated_and_inconsistent_results(self):
        normal = '''<TestRun><Results><UnitTestResult outcome="Passed"/><UnitTestResult outcome="Failed"/></Results>
            <ResultSummary outcome="Failed"><Counters total="2" executed="2" passed="1" failed="1" notExecuted="0"
            aborted="0" timeout="0" error="0" notRunnable="0" disconnected="0" inconclusive="0"
            passedButRunAborted="0" warning="0" completed="0" inProgress="0" pending="0"/></ResultSummary></TestRun>'''
        changes = {
            'aborted summary': lambda root: root.find('ResultSummary').set('outcome', 'Aborted'),
            'missing summary outcome': lambda root: root.find('ResultSummary').attrib.pop('outcome'),
            'completed despite failed test': lambda root: root.find('ResultSummary').set('outcome', 'Completed'),
            'duplicate summary': lambda root: root.append(ET.fromstring('<ResultSummary outcome="Failed"/>')),
            'missing summary': lambda root: root.remove(root.find('ResultSummary')),
            'missing counter': lambda root: root.find('ResultSummary/Counters').attrib.pop('failed'),
            'missing abort counter': lambda root: root.find('ResultSummary/Counters').attrib.pop('aborted'),
            'negative counter': lambda root: root.find('ResultSummary/Counters').set('passed', '-1'),
            'duplicate counters': lambda root: root.find('ResultSummary').append(ET.fromstring('<Counters/>')),
            'nested counters': lambda root: root.find('ResultSummary').append(ET.fromstring('<Extension><Counters/></Extension>')),
            'mismatched rows': lambda root: root.find('Results')[1].set('outcome', 'Passed'),
            'skipped row': lambda root: root.find('Results')[1].set('outcome', 'NotExecuted'),
            'incomplete run': lambda root: root.find('ResultSummary/Counters').set('inProgress', '1'),
        }
        build = {'exitCode': 0, 'timedOut': False}
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'tests.trx'
            path.write_text(normal)
            parsed = dict(executor.read_trx(Path(folder)), exitCode=1, timedOut=False)
            self.assertEqual('Killed', executor.classify(build, parsed, 2))
            self.assertEqual('Error', executor.classify(build, parsed, 934))
            for name, change in changes.items():
                with self.subTest(name=name):
                    root = ET.fromstring(normal)
                    change(root)
                    ET.ElementTree(root).write(path)
                    try:
                        parsed = dict(executor.read_trx(Path(folder)), exitCode=1, timedOut=False)
                    except ValueError:
                        continue
                    self.assertEqual('Error', executor.classify(build, parsed, 2))
            root = ET.fromstring(normal)
            root.find('Results')[1].set('outcome', 'Passed')
            root.find('ResultSummary').set('outcome', 'Completed')
            root.find('ResultSummary/Counters').set('passed', '2')
            root.find('ResultSummary/Counters').set('failed', '0')
            ET.ElementTree(root).write(path)
            parsed = dict(executor.read_trx(Path(folder)), exitCode=0, timedOut=False)
            self.assertEqual('Survived', executor.classify(build, parsed, 2))

    def test_utf16_replacement_preserves_surrogates_and_crlf(self):
        changed, span = executor.replace_span('a🙂z\r\nnext', {'start': {'line': 1, 'column': 2}, 'end': {'line': 1, 'column': 4}}, 'b')
        self.assertEqual(('abz\r\nnext', '🙂'), (changed, span))

    def test_empty_reversed_and_outside_ranges_are_rejected(self):
        for end in (0, 1, 9):
            with self.assertRaises(ValueError):
                executor.replace_span('abc', {'start': {'line': 1, 'column': 2}, 'end': {'line': 1, 'column': end}}, '')

    def test_only_complete_actual_failure_is_killed(self):
        build = {'exitCode': 0, 'timedOut': False}
        self.assertEqual('Killed', executor.classify(build, self._tests(failed=1, code=1), 3))
        self.assertEqual('Survived', executor.classify(build, self._tests(), 3))
        self.assertEqual('Error', executor.classify(build, self._tests(failed=1, total=2, executed=2, code=1), 3))
        self.assertEqual('Error', executor.classify(build, self._tests(failed=1, executed=2, skipped=1, code=1), 3))
        self.assertEqual('Error', executor.classify(build, self._tests(failed=1, code=2), 3))
        self.assertEqual('Error', executor.classify(build, self._tests(code=1), 3))
        self.assertEqual('Error', executor.classify(build, None, 3))

    def test_host_abort_and_compiler_failure_cannot_become_killed(self):
        tests = self._tests(failed=1, code=1)
        tests['otherCounts'] = {'aborted': 1}
        self.assertEqual('Error', executor.classify({'exitCode': 0, 'timedOut': False}, tests, 3))
        self.assertEqual('Error', executor.classify({'exitCode': 1, 'timedOut': False}, self._tests(failed=1, code=1), 3))
        tests = self._tests(failed=1, code=1)
        tests['outcomes'] = {'Failed': 2, 'NotExecuted': 1}
        self.assertEqual('Error', executor.classify({'exitCode': 0, 'timedOut': False}, tests, 3))

    def test_timeouts_remain_distinct_from_kills(self):
        self.assertEqual('Timeout', executor.classify({'exitCode': 1, 'timedOut': True}, None, 3))
        tests = self._tests(failed=1, code=1)
        tests['timedOut'] = True
        self.assertEqual('Timeout', executor.classify({'exitCode': 0, 'timedOut': False}, tests, 3))

    def test_selection_requires_exact_eligible_ids_and_source_hash(self):
        source = 'source'
        report = {'files': {'reported': {'source': source, 'mutants': [
            {'id': '1', 'status': 'CompileError'},
            {'id': '2', 'status': 'Ignored', 'statusReason': executor.BLOCK_REASON},
            {'id': '3', 'status': 'NoCoverage'},
            {'id': '4', 'status': 'Ignored', 'statusReason': 'Removed by exclude from code coverage filter'}]}}}
        mapping = {'reported': 'src/File.cs'}
        paths = {'src/File.cs': executor.digest(source.encode())}
        self.assertEqual(['1', '2'], [row[2]['id'] for row in executor.selected_mutants(report, mapping, paths, None)])
        for requested in ({'3'}, {'4'}, {'99'}):
            with self.assertRaises(ValueError):
                executor.selected_mutants(report, mapping, paths, requested)
        with self.assertRaises(ValueError):
            executor.selected_mutants(report, mapping, {'src/File.cs': 'wrong'}, None)
        with self.assertRaises(ValueError):
            executor.selected_mutants(report, {}, paths, None)

    def test_round_robin_shards_cover_every_selected_id_once(self):
        rows = list(range(17))
        shards = [executor.select_shard(rows, index, 4) for index in range(4)]
        self.assertEqual(rows, sorted(value for shard in shards for value in shard))
        self.assertEqual(17, len({value for shard in shards for value in shard}))
        for index, count in ((-1, 4), (4, 4), (0, 0), (0, 65)):
            with self.assertRaises(ValueError):
                executor.select_shard(rows, index, count)


if __name__ == '__main__':
    unittest.main()
