"""Negative tests for canonical suite and independent evidence binding."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import suite_binding as suite
import mutation_review
import multi_stage_review

class SuiteChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.paths = {}
        self.production, self.tests = suite.TARGETS['consumerkit12']
        for relative in (self.production, self.tests, 'src/client.cs', 'tests/client.cs'):
            file = self.root / relative
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(b'frozen')
            self.paths[relative] = hashlib.sha256(b'frozen').hexdigest()
        self.graph = {
            self.production: (['src/client.cs'], []),
            self.tests: (['tests/client.cs'], [self.production]),
        }
    def msbuild(self, args, **kwargs):
        key = Path(args[2]).relative_to(self.root).as_posix()
        compile, refs = self.graph[key]
        return json.dumps({'Properties': {'MSBuildProjectFullPath': str(self.root / key)},
            'Items': {kind: [{'FullPath': str(self.root / name)} for name in names]
                      for kind, names in (('Compile', compile), ('ProjectReference', refs))}})
    def evaluate(self):
        with patch.object(suite.subprocess, 'check_output', side_effect=self.msbuild):
            return suite.evaluate(self.root, 'consumerkit12', 'dotnet', self.paths)
    def test_evaluated_references_bind_complete_target(self):
        self.assertEqual(['src/client.cs'], self.evaluate()['productionSources'])
    def test_missing_production_reference_rejected(self):
        self.graph[self.tests] = (['tests/client.cs'], [])
        with self.assertRaisesRegex(ValueError, 'complete production'): self.evaluate()
    def test_changed_compile_input_rejected(self):
        (self.root / 'src/client.cs').write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError, 'frozen inventory'): self.evaluate()
    def test_external_input_rejected(self):
        self.graph[self.tests] = (['../outside.cs'], [self.production])
        with self.assertRaisesRegex(ValueError, 'outside'): self.evaluate()
    def test_cycle_rejected(self):
        self.graph[self.production] = (['src/client.cs'], [self.tests])
        with self.assertRaisesRegex(ValueError, 'Cyclic'): self.evaluate()
    def test_wrong_project_evaluation_rejected(self):
        with patch.object(suite.subprocess, 'check_output', return_value=json.dumps({
            'Properties': {'MSBuildProjectFullPath': str(self.root/'src/client.cs')}})):
            with self.assertRaisesRegex(ValueError, 'identity'): suite.evaluate(self.root, 'consumerkit12', 'dotnet', self.paths)
    def test_report_requires_exact_source_path_hash_and_complete_target(self):
        binding = {'productionSources': ['src/client.cs']}
        report = {'files': {'old/root/src/client.cs': {'source': 'frozen'}}}
        self.assertEqual({'old/root/src/client.cs':'src/client.cs'}, suite.report_mapping(binding, report, self.paths))
        combined = copy.deepcopy(report)
        combined['files']['other/target.cs'] = {'source':'other'}
        self.assertEqual(1, len(suite.report_mapping(binding, combined, self.paths)))
        for altered in ({'files': {}}, {'files': {'other.cs': {'source':'frozen'}}},
                        {'files': {'old/root/src/client.cs': {'source':'changed'}}}):
            with self.assertRaises(ValueError): suite.report_mapping(binding, altered, self.paths)

    def test_unknown_target_rejected(self):
        with self.assertRaises(ValueError): suite.evaluate(self.root, 'wrong', 'dotnet', self.paths)
    def test_both_reviewers_reject_wrong_suite(self):
        for target in ('consumerkit12', 'inventoryreader'):
            execution = {'command': suite.command('dotnet', 'test', self.root, target, self.root/'results'),
                         'workingDirectory': str(self.root), 'trx': str(self.root/'results/tests.trx')}
            multi_stage_review.verify_command(execution, 'test', self.root, 'dotnet', target)
            wrong = copy.deepcopy(execution)
            wrong['command'][2] = str(self.root/suite.TARGETS['provider'][1])
            with self.assertRaises(ValueError): multi_stage_review.verify_command(wrong, 'test', self.root, 'dotnet', target)
            with self.assertRaises(ValueError): mutation_review.execution_counts(self.root, wrong, target)
    def test_filtered_or_overridden_commands_rejected(self):
        for target in ('consumerkit12', 'inventoryreader'):
            for extra in ('--no-build', '--filter', '--list-tests', '-p:ConsumerKitProject=other'):
                execution = {'command': suite.command('dotnet', 'test', self.root, target, self.root/'results')+[extra],
                             'workingDirectory': str(self.root), 'trx': str(self.root/'results/tests.trx')}
                with self.assertRaises(ValueError): multi_stage_review.verify_command(execution, 'test', self.root, 'dotnet', target)
    def test_unbound_reader_and_consumer_evidence_rejected(self):
        for file in ('examples/CollectionExample/InventoryItemReader.cs', 'src/Contracts/1.2/Consumer/CollectionDataIpcClient.cs'):
            with self.assertRaises(ValueError): suite.verify_summary({}, {}, [file])
    def test_summary_binding_rejects_mixed_sources_and_relabeling(self):
        host = self.root/'dotnet'; host.write_bytes(b'host')
        self.paths['tools/suite_binding.py'] = hashlib.sha256(Path(suite.__file__).read_bytes()).hexdigest()
        module = self.root/'tools/suite_binding.py'; module.parent.mkdir(); module.write_bytes(Path(suite.__file__).read_bytes())
        binding = self.evaluate()
        summary = {'snapshot':str(self.root), 'suiteBinding':binding, 'baseline':{}, 'results':[],
                   'toolchain':{'dotnet':{'path':str(host),'sha256':hashlib.sha256(b'host').hexdigest()}}}
        with patch.object(suite.subprocess, 'check_output', side_effect=self.msbuild):
            self.assertEqual('consumerkit12',suite.verify_summary(summary, self.paths, ['src/client.cs']))
            with self.assertRaisesRegex(ValueError, 'mixed'): suite.verify_summary(summary, self.paths, ['src/client.cs','tests/client.cs'])
            summary['suiteBinding']['testProject'] = suite.TARGETS['inventoryreader'][1]
            with self.assertRaisesRegex(ValueError, 'Wrong suite'): suite.verify_summary(summary, self.paths, ['src/client.cs'])

if __name__ == '__main__': unittest.main()
