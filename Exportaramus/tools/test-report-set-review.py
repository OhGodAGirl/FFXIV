import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import report_set_review as review


class ReportSetTests(unittest.TestCase):
    source = 'src/Features/Inventory/InventoryService.cs'

    def config(self):
        return {'stryker-config': {'mutate': ['**/*.cs'], 'thresholds': {'high': 100, 'low': 100, 'break': 100}, 'mutation-level': 'Standard', 'ignore-mutations': [], 'test-case-filter': 'Category!=UninstrumentedPerformance', 'coverage-analysis': 'perTestInIsolation'}}

    def mutant(self, identity='1', status='Killed'):
        return dict(id=identity, status=status, location={'start': {'line': 1, 'column': 1}, 'end': {'line': 1, 'column': 2}}, replacement='false', mutatorName='Boolean')

    def report(self, mutants=None):
        return {'files': {self.source: {'source': 'new source', 'mutants': mutants or [self.mutant()]}}}

    def test_only_whole_file_scope_is_allowed(self):
        a = self.config()
        b = copy.deepcopy(a)
        b['stryker-config']['mutate'] = ['**/Features/Inventory/InventoryService.cs']
        review.replacement_config(a, b, self.source)
        for scope in ([], ['**/*.cs'], ['**/Features/Inventory/InventoryService.cs{1..3}'], ['!**/InventoryService.cs'], ['**/Features/Inventory/InventoryService.cs', '**/Other.cs']):
            with self.subTest(scope=scope), self.assertRaises(ValueError):
                b['stryker-config']['mutate'] = scope
                review.replacement_config(a, b, self.source)

    def test_operators_ignores_filters_thresholds_and_coverage_cannot_change(self):
        for key, value in [('mutation-level', 'Basic'), ('ignore-mutations', ['Boolean']), ('test-case-filter', 'Name=One'), ('thresholds', {'high': 99, 'low': 99, 'break': 99}), ('coverage-analysis', 'off')]:
            a = self.config()
            b = copy.deepcopy(a)
            b['stryker-config'].update(mutate=['**/Features/Inventory/InventoryService.cs'])
            b['stryker-config'][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                review.replacement_config(a, b, self.source)

    def test_report_exact_source_and_full_single_domain(self):
        review.replacement_inventory(self.report(), self.source, b'new source')
        for report in ({'files': {}}, {'files': dict(self.report()['files'], other={'source': 'x', 'mutants': []})}, {'files': {self.source: {'source': 'old source', 'mutants': [self.mutant()]}}}):
            with self.subTest(report=report), self.assertRaises(ValueError):
                review.replacement_inventory(report, self.source, b'new source')

    def test_incomplete_duplicate_and_pending_mutants_rejected(self):
        for mutants in ([self.mutant(), self.mutant()], [self.mutant(status='NotRun')], [{'id': '1', 'status': 'Killed'}]):
            with self.subTest(mutants=mutants), self.assertRaises(ValueError):
                review.replacement_inventory(self.report(mutants), self.source, b'new source')
        report = self.report()
        report['files'][self.source]['mutants'] = []
        with self.assertRaises(ValueError):
            review.replacement_inventory(report, self.source, b'new source')

    def test_domains_missing_unchanged_and_duplicate_rejected(self):
        for old, final, replaced in [({}, {}, [self.source]), ({self.source: 'a'}, {self.source: 'a'}, [self.source]), ({self.source: 'a'}, {self.source: 'b'}, [self.source, self.source]), ({self.source: 'a'}, {self.source: 'b'}, [])]:
            with self.subTest(replaced=replaced), self.assertRaises(ValueError):
                review.source_domains(old, final, replaced)
        old, new = review.source_domains({self.source: 'a', 'schema': 'x'}, {self.source: 'b', 'schema': 'y'}, [self.source])
        self.assertEqual(old, {'schema': 'x'})
        self.assertEqual(new, {'schema': 'y'})  # handed to strict strengthening, never silently discarded

    def test_old_review_is_not_transplanted_even_when_id_reused(self):
        original = self.report()
        original['files'][self.source]['source'] = 'old source'
        result = review.current_outcomes(original, [], {self.source: [self.mutant(status='Survived')]})
        self.assertFalse(result['gatePassed'])
        self.assertEqual(len(result['unresolvedMutants']), 1)
        self.assertEqual(result['historicalSupersededMutants'], 1)

    def test_every_nonkill_raw_label_remains_unresolved(self):
        mutants = [self.mutant(str(i), status) for i, status in enumerate(('Killed', 'Survived', 'NoCoverage', 'Ignored', 'CompileError', 'Timeout'))]
        result = review.current_outcomes(self.report(), [], {self.source: mutants})
        self.assertEqual(len(result['unresolvedMutants']), 5)
        self.assertFalse(result['gatePassed'])

    def test_retained_unresolved_cannot_disappear(self):
        original = self.report()
        original['files']['src/Other.cs'] = {'source': 'other', 'mutants': [self.mutant('2', 'Ignored')]}
        unresolved = [dict(self.mutant('2', 'Ignored'), file='src/Other.cs')]
        result = review.current_outcomes(original, unresolved, {self.source: [self.mutant()]})
        self.assertFalse(result['gatePassed'])
        self.assertEqual(result['currentMutants'], 2)
        self.assertEqual(result['unresolvedMutants'][0]['reportSet'], 'historical')

    def test_fabricated_unresolved_identity_rejected(self):
        with self.assertRaises(ValueError):
            review.current_outcomes(self.report(), [dict(self.mutant('999'), file=self.source)], {self.source: [self.mutant()]})

    def test_missing_changed_artifact_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'proof.json'
            path.write_text('{}')
            item = {'path': str(path), 'sha256': review.digest(path)}
            self.assertEqual(review.read(item), {})
            path.write_text('{"gatePassed":true}')
            with self.assertRaises(ValueError):
                review.bound(item)
            path.unlink()
            with self.assertRaises(ValueError):
                review.bound(item)

    def test_execution_rejects_command_tool_input_and_completion_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            def artifact(name, value):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(json.dumps(value))
                return {'path': str(path), 'sha256': review.digest(path)}
            config = self.config()
            config['stryker-config']['mutate'] = ['**/Features/Inventory/InventoryService.cs']
            item = {'executionConfig': artifact('config.json', config), 'report': artifact('out/report.json', self.report()), 'canonicalRunner': artifact('runner.dll', {}), 'canonicalToolVersion': 'pinned-version', 'outputDirectory': str(root / 'out')}
            process = {'runner': item['canonicalRunner']['path'], 'version': 'pinned-version', 'workingDirectory': str(root / 'tests/Exportaramus.Testable'), 'arguments': ['--config-file', item['executionConfig']['path'], '--output', str(root / 'out'), '--skip-version-check', '--verbosity', 'info', '--log-to-file', '--break-on-initial-test-failure']}
            item['process'] = artifact('process.json', process)
            item['rawExecution'] = artifact('raw-execution.json', {'exitCode': 0, 'completed': '2026-11-02T00:48:08Z'})
            execution = dict(completed=True, inputsUnchanged=True, exitCode=0, beforeInventorySha256='abc', afterInventorySha256='abc', reportSha256=item['report']['sha256'], configSha256=item['executionConfig']['sha256'], mutate=config['stryker-config']['mutate'])
            review.validate_execution(execution, item, 'abc', root)
            threshold_item = dict(item, rawExecution=artifact('threshold-execution.json', {'exitCode': 2, 'completed': '2026-11-02T00:48:08Z'}), console=artifact('console.log', 'The final mutation score is 91.79 %. The mutation score is lower than the configured break threshold of 100%'))
            review.validate_execution(dict(execution, exitCode=2), threshold_item, 'abc', root)
            threshold_item['console'] = artifact('failed-console.log', 'Process crashed during test startup')
            with self.assertRaisesRegex(ValueError, 'threshold failure'):
                review.validate_execution(dict(execution, exitCode=2), threshold_item, 'abc', root)
            for key, value in [('completed', False), ('inputsUnchanged', False), ('exitCode', 1), ('afterInventorySha256', 'other'), ('reportSha256', 'other'), ('configSha256', 'other'), ('mutate', ['**/*.cs'])]:
                with self.subTest(key=key), self.assertRaises(ValueError):
                    review.validate_execution(dict(execution, **{key: value}), item, 'abc', root)
            for key, value in [('version', 'different'), ('workingDirectory', str(root)), ('arguments', process['arguments'] + ['--test-case-filter', 'One'])]:
                changed = dict(item, process=artifact('bad-process.json', dict(process, **{key: value})))
                with self.subTest(key=key), self.assertRaises(ValueError):
                    review.validate_execution(execution, changed, 'abc', root)

    def test_nested_artifact_closure_is_rechecked(self):
        value = {'nested': [{'path': 'a', 'sha256': 'b'}, {'deeper': {'path': 'c', 'sha256': 'd'}}]}
        self.assertEqual(list(review.artifacts(value)), [{'path': 'a', 'sha256': 'b'}, {'path': 'c', 'sha256': 'd'}])

    def test_coverage_cannot_omit_duplicate_or_replace_cases(self):
        review.case_coverage(['one', 'two', 'perf'], ['one', 'two'], ['perf'])
        for covered, performance in [(['one'], ['perf']), (['one', 'two', 'perf'], ['perf']), (['one', 'other'], ['perf']), (['one', 'two'], [])]:
            with self.subTest(covered=covered), self.assertRaises(ValueError):
                review.case_coverage(['one', 'two', 'perf'], covered, performance)

    def test_existing_output_cannot_be_used_as_fabricated_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            plan = root / 'plan.json'
            plan.write_text(json.dumps({'schemaVersion': '1.0', 'reviewer': 'reviewer'}))
            with self.assertRaisesRegex(ValueError, 'Output must be new'):
                review.compose(plan, root)

    def test_real_strengthening_validator_rejects_retained_production_and_new_files(self):
        import repair_bridge
        import multi_stage_review
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            old = {self.source: 'old', 'src/Other.cs': 'same'}
            for final in ({self.source: 'new', 'src/Other.cs': 'changed'}, {self.source: 'new'}, {self.source: 'new', 'src/Other.cs': 'same', 'src/New.cs': 'new'}):
                a, b = review.source_domains(old, final, [self.source])
                with self.subTest(final=final), self.assertRaises(ValueError):
                    multi_stage_review.validate_strengthening(a, b, [], root, repair_bridge, multi_suite=True)

    def test_real_case_validator_detects_lost_duplicate_cases(self):
        import repair_bridge
        self.assertFalse(repair_bridge.preserves_cases(['one', 'one', 'two'], ['one', 'two']))
        self.assertTrue(repair_bridge.preserves_cases(['one', 'two'], ['one', 'two', 'three']))

    def test_invalid_historical_proof_is_not_reduced_to_pending_mutants(self):
        receipt = {'stageScopeVerified': True, 'reportSha256': 'exact', 'reviewRequired': []}
        review.historical_receipt(receipt, 'exact')
        for change in ({'stageScopeVerified': False}, {'reportSha256': 'other'}, {'error': 'Invalid proof'}, {'reviewRequired': ['Independent execution artifact bindings failed: changed']}, {'reviewRequired': ['Instrumentation sourceMismatches requires explicit review; see instrumentationAudit.']}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                review.historical_receipt(dict(receipt, **change), 'exact')
        with self.assertRaises(ValueError):
            review.historical_receipt({'gatePassed': True}, 'exact')

    def test_compiler_inventory_preserves_pending_without_reintroducing_reviewed_entries(self):
        accepted = dict(self.mutant('1', 'CompileError'), file=self.source, reviewedIndividually=True)
        pending = dict(self.mutant('2', 'CompileError'), file=self.source, reviewedIndividually=False)
        receipt = {'reviewedMutants': [accepted], 'unresolvedMutants': [], 'compileErrorMutants': [accepted, pending]}
        self.assertEqual(review.historical_pending(receipt), [pending])
        for incorrect in (dict(accepted, reviewedIndividually=False), dict(pending, reviewedIndividually=True)):
            altered = dict(receipt, compileErrorMutants=[incorrect])
            with self.assertRaises(ValueError):
                review.historical_pending(altered)
        with self.assertRaises(ValueError):
            review.historical_pending(dict(receipt, unresolvedMutants=[accepted]))

    def test_readme_exception_requires_exact_content_and_consumption_audit(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def artifact(name, value):
                path = root / name
                path.write_text(json.dumps(value))
                return {'path': str(path), 'sha256': review.digest(path)}
            before = artifact('before.md', 'old')
            after = artifact('after.md', 'new')
            audit = artifact('audit.json', {'inspected': 'all project and C# source inputs'})
            proof = dict(path='README.md', reviewed=True, reviewer='reviewer', reason='Exact documentation-only change', noExecutableOrConfigConsumption=True, beforeSha256=before['sha256'], afterSha256=after['sha256'], evidence=[audit])
            item = dict(path='README.md', before=before, after=after, review=artifact('review.json', proof))
            old, final, evidence = review.documentation_change([item], {'README.md': before['sha256']}, {'README.md': after['sha256']})
            self.assertEqual((old, final, evidence), ({}, {}, [audit]))
            for change in ({'path': 'src/Code.cs'}, {'path': 'docs/Other.md'}, {'before': after}, {'after': before}):
                with self.subTest(change=change), self.assertRaises(ValueError):
                    review.documentation_change([dict(item, **change)], {'README.md': before['sha256']}, {'README.md': after['sha256']})
            for change in ({'noExecutableOrConfigConsumption': False}, {'beforeSha256': 'wrong'}, {'afterSha256': 'wrong'}, {'reviewed': False}, {'evidence': []}):
                modified = dict(item, review=artifact('bad-review.json', dict(proof, **change)))
                with self.subTest(change=change), self.assertRaises(ValueError):
                    review.documentation_change([modified], {'README.md': before['sha256']}, {'README.md': after['sha256']})

    def test_canonical_tool_version_requires_one_literal_pin(self):
        self.assertEqual(review.canonical_mutation_version("x={'toolVersion':'4.16.0'}"), '4.16.0')
        for source in ("x={}", "x={'toolVersion': version}", "x={'toolVersion':'4.16.0'}; y={'toolVersion':'5.0.0'}"):
            with self.subTest(source=source), self.assertRaises(ValueError):
                review.canonical_mutation_version(source)

    def test_final_baselines_are_executed_and_failure_has_no_trx_fallback(self):
        import suite_binding
        import repair_bridge
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'global.json').write_text(json.dumps({'sdk': {'version': '10.0.400', 'rollForward': 'disable'}}))
            dotnet = root / 'dotnet.exe'
            dotnet.write_bytes(b'pinned executable')
            expected = {k: ['case'] for k in suite_binding.TARGETS}
            calls = []
            def run(command, **kwargs):
                calls.append(command)
                if command[-1] == '--version':
                    return SimpleNamespace(returncode=0, stdout='10.0.400\n', stderr='')
                if command[1] == 'restore':
                    return SimpleNamespace(returncode=0, stdout='restored', stderr='')
                results = Path(command[command.index('--results-directory') + 1])
                (results / 'tests.trx').write_text('generated by this invocation')
                return SimpleNamespace(returncode=0, stdout='passed', stderr='')
            fake_bridge = SimpleNamespace(canonical_trx=lambda path, target: ['case'], preserves_cases=repair_bridge.preserves_cases)
            with patch.object(review.subprocess, 'run', side_effect=run):
                cases, evidence = review.execute_baselines(dotnet, root, root / 'pass', expected, fake_bridge)
            self.assertEqual(cases, expected)
            self.assertEqual(len(evidence), 17)
            for target, command in zip(suite_binding.TARGETS, calls[2::2]):
                self.assertEqual(command, suite_binding.command(dotnet, 'test', root, target, root / 'pass' / target, phase='baseline'))
            for target, command in zip(suite_binding.TARGETS, calls[1::2]):
                self.assertEqual(command, suite_binding.command(dotnet, 'restore', root, target))
            def fail(command, **kwargs):
                return SimpleNamespace(returncode=0, stdout='10.0.400', stderr='') if command[-1] == '--version' or command[1] == 'restore' else SimpleNamespace(returncode=1, stdout='', stderr='actual failure')
            with patch.object(review.subprocess, 'run', side_effect=fail), self.assertRaisesRegex(ValueError, 'baseline failed'):
                review.execute_baselines(dotnet, root, root / 'failed', expected, fake_bridge)
            with patch.object(review.subprocess, 'run', return_value=SimpleNamespace(returncode=0, stdout='other SDK', stderr='')), self.assertRaisesRegex(ValueError, 'SDK differs'):
                review.execute_baselines(dotnet, root, root / 'other-sdk', expected, fake_bridge)

    def test_timeout_preserves_command_and_captured_diagnostics(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            error = review.subprocess.TimeoutExpired(['test'], 120, output=b'partial output', stderr=b'actual error')
            with patch.object(review.subprocess, 'run', side_effect=error), self.assertRaisesRegex(ValueError, 'timed out'):
                review.recorded_run(['test'], root, root / 'timeout')
            receipt = json.loads((root / 'timeout.json').read_text())
            self.assertTrue(receipt['timedOut'])
            self.assertIsNone(receipt['exitCode'])
            self.assertEqual(receipt['command'], ['test'])
            self.assertEqual((root / 'timeout.log').read_text(), 'partial outputactual error')

    def test_raw_report_empty_outside_scope_markers_are_preserved_but_not_admitted(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            target, other = str(root / self.source), str(root / 'src/Other.cs')
            report = {'projectRoot': str(root / 'tests/Exportaramus.Testable'), 'files': {target: self.report()['files'][self.source], other: {'source': 'File ignored by mutate filter', 'mutants': []}}}
            paths = {self.source: 'hash', 'src/Other.cs': 'hash2'}
            self.assertEqual(review.scoped_report(report, self.source, root, paths), self.report())
            for value in ({'source': 'unverified source', 'mutants': []}, {'source': 'File ignored by mutate filter', 'mutants': [self.mutant()]}):
                changed = copy.deepcopy(report)
                changed['files'][other] = value
                with self.assertRaises(ValueError):
                    review.scoped_report(changed, self.source, root, paths)
            with self.assertRaises(ValueError):
                review.scoped_report(report, self.source, root, {self.source: 'hash'})


class FidelityTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup); self.root = Path(self.tmp.name)
        self.mutant = {'id': '1', 'location': {'start': {'line': 1, 'column': 1}, 'end': {'line': 1, 'column': 2}}, 'replacement': 'r'}
        self.report = self.art('report.json', {'files': {'a.cs': {'source': 's', 'mutants': [self.mutant]}}})
        self.row = dict(file='a.cs', id='1', sourceSha256=hashlib.sha256(b's').hexdigest(), location=self.mutant['location'], replacement='r', audit='SameExpressionTree', expectedShapeSha256='a' * 64, nativeShapeSha256='a' * 64)
        self.audit = self.art('audit.json', {'reportSha256': self.report['sha256'], 'rows': [self.row]})
        self.helper = self.art('helper.json', {'code': 'reviewed'})
        self.descriptor = dict(reviewed=True, reviewer='independent', reason='Exact operator child review', report=self.report, audit=self.audit, helper=self.helper, preservesOriginalOperatorChildren=True, evidence=[self.helper])
        self.refresh_review()
        self.entry = dict(file='a.cs', id='1', classification='Equivalent')
    def art(self, name, value):
        p = self.root / name; p.write_text(json.dumps(value)); return dict(path=str(p), sha256=review.digest(p))
    def refresh_review(self):
        self.descriptor['independentReview'] = self.art('peer.json', dict(reviewed=True, reviewer='peer', reason='Checked pinned factory construction', audit=self.descriptor['audit'], helper=self.helper))
    def changed_audit(self, rows):
        self.descriptor['audit'] = self.art('audit.json', {'reportSha256': self.report['sha256'], 'rows': rows}); self.refresh_review()
    def test_exact_complete_audit_passes_and_missing_duplicate_changed_identity_reject(self):
        review.validate_mutation_fidelity([self.entry], self.report, self.descriptor)
        with self.assertRaises(ValueError): review.validate_mutation_fidelity([self.entry], self.report, None)
        for rows in ([], [self.row, self.row], [dict(self.row, sourceSha256='changed')], [dict(self.row, replacement='other')], [dict(self.row, expectedShapeSha256=None, nativeShapeSha256=None)]):
            self.changed_audit(rows)
            with self.assertRaises(ValueError): review.validate_mutation_fidelity([self.entry], self.report, self.descriptor)
    def test_mismatch_requires_corrected_execution_or_explicit_static_proof(self):
        self.changed_audit([dict(self.row, audit='DifferentExpressionTree', nativeShapeSha256='b' * 64)])
        with self.assertRaises((ValueError, KeyError)): review.validate_mutation_fidelity([self.entry], self.report, self.descriptor)
        proof = self.art('proof.json', dict(reviewed=True, reviewer='source peer', reason='All closed inputs satisfy same intended condition', file='a.cs', id='1', sourceSha256=self.row['sourceSha256'], expectedShapeSha256='a' * 64, reportSha256=self.report['sha256']))
        entry = dict(self.entry, astReview=proof)
        review.validate_mutation_fidelity([entry], self.report, self.descriptor)
        entry['classification'] = 'IndependentlyKilled'
        with self.assertRaises(ValueError): review.validate_mutation_fidelity([entry], self.report, self.descriptor)
    def test_raw_absolute_report_paths_are_mapped_only_with_measured_root(self):
        raw_file = str(self.root / 'sub' / '..' / 'a.cs')
        self.report = self.art('report.json', {'files': {raw_file: {'source': 's', 'mutants': [self.mutant]}}})
        self.descriptor['report'] = self.report
        self.changed_audit([dict(self.row, file=raw_file)])
        with self.assertRaises(ValueError): review.validate_mutation_fidelity([self.entry], self.report, self.descriptor)
        review.validate_mutation_fidelity([self.entry], self.report, self.descriptor, report_root=self.root)
        with self.assertRaises(ValueError): review.validate_mutation_fidelity([self.entry], self.report, self.descriptor, report_root=self.root / 'other')

    def test_corrective_mode_marker_alone_cannot_bypass_rerendering(self):
        self.changed_audit([dict(self.row, audit='DifferentExpressionTree', nativeShapeSha256='b' * 64)])
        mutated = self.root / 'mutant.cs'; mutated.write_text('correctly grouped')
        inventory = self.art('inventory.json', {'files': []})
        result = dict(file='a.cs', mutant=self.mutant, replacementMode='roslyn-expression-v1', originalSpan='s', mutatedSource=str(mutated), mutatedSourceSha256=review.digest(mutated))
        summary = self.art('corrected.json', dict(replacementMode='roslyn-expression-v1', results=[result], inventory=inventory['path'], inventorySha256=inventory['sha256'], snapshot=str(self.root)))
        entry = dict(self.entry, independentExecution={'summary': summary['path'], 'sha256': summary['sha256']})
        with self.assertRaises(ValueError): review.validate_mutation_fidelity([entry], self.report, self.descriptor)
        validator = SimpleNamespace(recompute_mutation=lambda *args: ('correctly grouped', 's'))
        review.validate_mutation_fidelity([entry], self.report, self.descriptor, stage_validator=validator)
        validator.recompute_mutation = lambda *args: ('different grouping', 's')
        with self.assertRaises(ValueError): review.validate_mutation_fidelity([entry], self.report, self.descriptor, stage_validator=validator)

    def test_unreviewed_helpers_unknown_nodes_and_legacy_mismatch_fail(self):
        self.descriptor['preservesOriginalOperatorChildren'] = False
        with self.assertRaises(ValueError): review.validate_mutation_fidelity([self.entry], self.report, self.descriptor)
        self.descriptor['preservesOriginalOperatorChildren'] = True
        self.changed_audit([dict(self.row, audit='NotExactExpression', exactNode=True, nodeKind='Unknown')])
        with self.assertRaises((ValueError, KeyError)): review.validate_mutation_fidelity([self.entry], self.report, self.descriptor)
        self.changed_audit([dict(self.row, audit='DifferentExpressionTree', nativeShapeSha256='b' * 64)])
        summary = self.art('summary.json', {'results': [{'file': 'a.cs', 'mutant': self.mutant}]})
        entry = dict(self.entry, independentExecution={'summary': summary['path'], 'sha256': summary['sha256']})
        with self.assertRaises(ValueError): review.validate_mutation_fidelity([entry], self.report, self.descriptor)


class MeasurementSuccessorTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.measured=self.root/'measured';self.final=self.root/'final'
        self.measured.mkdir();self.final.mkdir();self.source='src/Features/Inventory/InventoryService.cs'
        self.paths={self.source:'production','tests/A.cs':'old-test'};self.final_paths=dict(self.paths,**{'tests/A.cs':'new-test'})
        self.before={'sha256':'old','files':[]};self.final_inventory={'sha256':'new','files':[]}
        self.item={'source':self.source,'measurementRoot':str(self.measured),'measurementBefore':self.art('before.json',self.before),'measurementAfter':self.art('after.json',self.before),'canonicalToolVersion':'4.16.0+exact','outputDirectory':str(self.measured/'output')}
        for key in ('report','executionConfig','process','canonicalRunner','execution'):self.item[key]=self.art(key+'.json',{'kind':key})
        self.receipt={'campaignRoot':str(self.measured),'measurement':{k:v for k,v in self.item.items() if k not in ('measurementRoot','measurementBefore','measurementAfter')}}
        self.item['finalCurrentCampaign']=self.art('campaign.json',self.receipt);self.final_artifact=self.art('final.json',self.final_inventory)
        self.binding={'inventory':self.final_inventory,'inventoryPaths':self.final_paths,'campaignInventory':self.before,'campaignInventorySha256':'old','reportHash':self.item['report']['sha256'],'sourcePaths':{self.source},'campaignSourceSha256':'production','evidence':[]}
        self.bridge=SimpleNamespace(inventory=lambda path:(json.loads(path.read_bytes()),dict(self.paths)),live_paths=lambda root,policy:dict(self.paths))
    def art(self,name,value):
        p=self.root/name;p.write_text(json.dumps(value));return {'path':str(p),'sha256':review.digest(p)}
    def check(self):return review.measurement_context(self.item,self.final,self.final_inventory,self.final_paths,self.final_artifact,self.bridge)
    def test_successor_retains_measurement(self):
        with patch.object(review,'validate_final_campaign',return_value=self.binding) as validate:
            result=self.check();self.assertEqual(self.measured,result['root']);self.assertEqual(self.before,result['inventory']);self.assertEqual(self.final,validate.call_args.args[1]);self.assertIn(self.item['finalCurrentCampaign'],result['evidence'])
    def test_missing_successor_rejected(self):
        del self.item['finalCurrentCampaign']
        with self.assertRaises(ValueError):self.check()
    def test_bound_inventory_report_and_source_cannot_change(self):
        for field,value in [('inventory',{}),('inventoryPaths',{}),('campaignInventory',{}),('campaignInventorySha256','other'),('reportHash','other'),('sourcePaths',{'other'}),('campaignSourceSha256','other')]:
            with self.subTest(field=field),patch.object(review,'validate_final_campaign',return_value=dict(self.binding,**{field:value})):
                with self.assertRaises(ValueError):self.check()
    def test_production_or_live_inputs_change_rejected(self):
        self.final_paths[self.source]='changed'
        with self.assertRaises(ValueError):self.check()
        self.final_paths[self.source]='production';self.bridge.live_paths=lambda root,policy:{}
        with self.assertRaises(ValueError):self.check()
    def test_rehashed_different_measurement_rejected(self):
        for key in ('report','executionConfig','process','canonicalRunner','execution'):
            altered=copy.deepcopy(self.receipt);altered['measurement'][key]=self.art('other-'+key+'.json',{'kind':key});self.item['finalCurrentCampaign']=self.art('campaign.json',altered)
            with self.subTest(key=key),patch.object(review,'validate_final_campaign',return_value=self.binding):
                with self.assertRaises(ValueError):self.check()
    def test_root_version_output_cannot_be_rebound(self):
        for key,value in [('campaignRoot',str(self.final)),('canonicalToolVersion','4.15.0'),('outputDirectory',str(self.final/'other'))]:
            altered=copy.deepcopy(self.receipt)
            if key=='campaignRoot':altered[key]=value
            else:altered['measurement'][key]=value
            self.item['finalCurrentCampaign']=self.art('campaign.json',altered)
            with self.subTest(key=key),patch.object(review,'validate_final_campaign',return_value=self.binding):
                with self.assertRaises(ValueError):self.check()
    def test_changed_measurement_after_rejected(self):
        self.item['measurementAfter']=self.art('after.json',{'sha256':'changed','files':[]})
        with self.assertRaises(ValueError):self.check()
    def test_unchanged_route_retained(self):
        del self.item['finalCurrentCampaign']
        with patch.object(review,'validate_final_campaign',side_effect=AssertionError('unexpected')):
            result=review.measurement_context(self.item,self.measured,self.before,self.paths,self.item['measurementBefore'],self.bridge);self.assertEqual(self.before,result['inventory'])
    def test_wrong_validator_location_rejected(self):
        fake=SimpleNamespace(__file__=str(self.root/'outside.py'))
        with patch.dict('sys.modules',{'current_campaign':fake}):
            with self.assertRaises(ValueError):review.validate_final_campaign({},self.final,{}, {})


if __name__ == '__main__':
    unittest.main()
