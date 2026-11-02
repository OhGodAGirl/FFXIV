"""Adversarial tool contracts; these tests do not execute or admit product mutants."""
from pathlib import Path
import copy
import json
import shutil
import tempfile
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch
import multi_stage_review as stage
import mutation_review as legacy
import repair_bridge as bridge


class StageChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.parent = Path(self.temp.name).resolve()
        self.root = self.parent / 'repair03/source'
        self.root.mkdir(parents=True)

    def artifact(self, relative, text='proof'):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')
        return {'path': str(path), 'sha256': stage.digest(path)}

    def manifest(self):
        return dict(format='exportaramus-mutation-stage-composition', schemaVersion='1.0', reviewed=True,
                    reviewer='Independent review', finalStage='repair03', stages=[
                        dict(id='repair02', root=str(self.parent / 'repair02/source'), parent=None),
                        dict(id='repair03', root=str(self.root), parent='repair02')])

    def test_verified_path_evidence_serializes_and_hashes_are_not_waived(self):
        item = self.artifact('prior.csproj', 'exact original project')
        path = Path(item['path'])
        normalized = stage.serializable_evidence([path, item])
        self.assertEqual(json.loads(json.dumps(normalized)), [item, item])
        altered = dict(item, sha256='0' * 64)
        with self.assertRaises(ValueError):
            stage.serializable_evidence([altered])
        path.unlink()
        with self.assertRaises(FileNotFoundError):
            stage.serializable_evidence([path])

    def test_uncovered_execution_is_eligible_but_labels_are_not_proof(self):
        self.assertTrue(stage.execution_eligible({'status': 'NoCoverage'}))
        for status in ('Killed', 'Pending', 'RuntimeError'):
            self.assertFalse(stage.execution_eligible({'status': status}))
        self.assertFalse(stage.execution_eligible({'status': 'Ignored', 'statusReason': 'excluded'}))
        self.assertTrue(stage.execution_eligible({'status': 'Ignored', 'statusReason': legacy.BLOCK_REASON}))

    def test_ordered_sibling_stages_are_supported(self):
        self.assertEqual(2, len(stage.stage_shape(self.manifest(), self.root)))

    def test_cycles_missing_predecessors_duplicates_and_ambiguous_roots_fail(self):
        for change in (lambda x: x['stages'][0].update(parent='repair03'),
                       lambda x: x['stages'][1].update(parent='missing'),
                       lambda x: x['stages'][1].update(id='repair02'),
                       lambda x: x['stages'][0].update(root=str(self.root)),
                       lambda x: x['stages'][0].update(root=str(self.root / 'nested/source')),
                       lambda x: x.update(finalStage='repair02'),
                       lambda x: x.update(reviewed=False)):
            manifest = self.manifest()
            change(manifest)
            with self.subTest(change=change), self.assertRaises(ValueError):
                stage.stage_shape(manifest, self.root)

    def test_external_and_tampered_evidence_is_rejected(self):
        proof = self.artifact('review.json')
        self.assertEqual(Path(proof['path']), stage.bound(proof, self.root))
        with self.assertRaises(ValueError):
            stage.bound(proof, self.root / 'different')
        with self.assertRaises(ValueError):
            stage.bound(proof, approved={})
        Path(proof['path']).write_text('tampered')
        with self.assertRaises(ValueError):
            stage.bound(proof)

    def test_relative_evidence_cannot_escape_or_change_inventory(self):
        proof = self.artifact('tests/Exportaramus.Tests/A.cs')
        item = dict(path='tests/Exportaramus.Tests/A.cs', sha256=proof['sha256'])
        self.assertEqual(Path(proof['path']), stage.relative_artifact(self.root, item, {item['path']: item['sha256']}))
        for invalid in (proof, dict(item, path='../A.cs')):
            with self.assertRaises(ValueError):
                stage.relative_artifact(self.root, invalid)
        with self.assertRaises(ValueError):
            stage.relative_artifact(self.root, item, {item['path']: 'old'})

    def test_changed_test_requires_all_obligation_reviews(self):
        proof = self.artifact('review.txt')
        path = 'tests/Exportaramus.Tests/A.cs'
        change = dict(path=path, beforeSha256='old', afterSha256='new', kind='TestStrengthening',
                      reason='Exact added assertion reviewed', evidence=[proof], reviewed=True, reviewer='Independent',
                      preservesExistingAssertions=True, preservesFixtureSemantics=True, noRelaxedDeadlines=True, noNewSkips=True)
        stage.validate_strengthening({path: 'old'}, {path: 'new'}, [change], self.root, bridge)
        for key in ('reviewed', 'preservesExistingAssertions', 'preservesFixtureSemantics', 'noRelaxedDeadlines', 'noNewSkips'):
            with self.subTest(key=key), self.assertRaises(ValueError):
                stage.validate_strengthening({path: 'old'}, {path: 'new'}, [dict(change, **{key: False})], self.root, bridge)

    def test_consumer_probe_entrypoint_is_exact_and_individually_reviewed(self):
        path = 'tests/Exportaramus.ConsumerKit.Tests/Exportaramus.ConsumerKit.Tests.csproj'
        before = self.artifact('development/before.csproj', '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        after = self.artifact(path, '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateProgramFile>false</GenerateProgramFile></PropertyGroup></Project>')
        change = dict(path=path, beforeSha256=before['sha256'], afterSha256=after['sha256'],
                      kind='TestHarnessEntryPoint', reason='Run exact schema process probe with the same complete test suite.',
                      beforeProject=before, evidence=[before], reviewed=True, reviewer='Independent', preservesExistingAssertions=True)
        original, successor = {path: before['sha256']}, {path: after['sha256']}
        stage.validate_strengthening(original, successor, [change], self.root, bridge, multi_suite=True)
        with self.assertRaises(ValueError):
            stage.validate_strengthening(original, successor, [change], self.root, bridge)
        for field in ('reviewed', 'preservesExistingAssertions'):
            with self.assertRaises(ValueError):
                stage.validate_strengthening(original, successor, [dict(change, **{field: False})], self.root, bridge, multi_suite=True)
        changed = Path(after['path'])
        changed.write_text(changed.read_text().replace('net10.0', 'net9.0'))
        change['afterSha256'] = successor[path] = stage.digest(changed)
        with self.assertRaises(ValueError):
            stage.validate_strengthening(original, successor, [change], self.root, bridge, multi_suite=True)

    def test_product_reference_config_scope_and_deletion_cannot_be_allowlisted(self):
        for path in ('src/A.cs', 'reference/A.cs', 'data/schema.json', 'Directory.Build.props',
                     'tests/coverage.runsettings', 'tests/Exportaramus.Tests/Exportaramus.Tests.csproj',
                     'tools/contract-quality-scope.py', 'tools/Get-SourceInventory.ps1'):
            change = dict(path=path, beforeSha256='old', afterSha256='new', kind='Tooling', reason='claimed',
                          evidence=[self.artifact('review.txt')], reviewed=True, reviewer='Reviewer')
            with self.subTest(path=path), self.assertRaises(ValueError):
                stage.validate_strengthening({path: 'old'}, {path: 'new'}, [change], self.root, bridge)
        with self.assertRaises(ValueError):
            stage.validate_strengthening({'tests/Exportaramus.Tests/A.cs': 'old'}, {}, [], self.root, bridge)

    def test_validator_pins_require_every_python_tool_and_explicit_approval(self):
        proof = self.artifact('tools/repair_bridge.py')
        descriptor = dict(root=str(self.root), validators=[dict(path='tools/repair_bridge.py', sha256=proof['sha256'])])
        approved = {proof['path']: proof['sha256']}
        stage.verify_stage_pins(descriptor, approved)
        with self.assertRaises(ValueError):
            stage.verify_stage_pins(descriptor, {})
        self.artifact('tools/unreviewed.py')
        with self.assertRaises(ValueError):
            stage.verify_stage_pins(descriptor, approved)

    def test_v1_allowlist_remains_unchanged(self):
        self.assertEqual(6, len(bridge.BRIDGE_TOOL_FILES))
        self.assertNotIn('tools/multi_stage_review.py', bridge.BRIDGE_TOOL_FILES)
        self.assertEqual({'tools/multi_stage_review.py', 'tools/test-multi-stage-review.py', 'tools/suite_binding.py', 'tools/test-suite-binding.py', 'tools/test-individual-mutation.py', 'tools/test-mutation-orchestration.py'},
                         bridge.STAGED_BRIDGE_TOOL_FILES - bridge.BRIDGE_TOOL_FILES)

    def test_exact_mutation_rejects_changed_raw_fields(self):
        original = dict(id='1', status='Survived', statusReason=None, mutatorName='Boolean', location={'start': 1}, replacement='false')
        stage.exact_mutation(original, original)
        for key in stage.FIELDS:
            with self.subTest(key=key), self.assertRaises(ValueError):
                stage.exact_mutation(dict(original, **{key: 'wrong'}), original)

    def command(self, verb):
        dotnet = Path(self.artifact('dotnet.exe')['path'])
        snapshot = self.root / 'development/run/input'
        project = snapshot / 'tests/Exportaramus.Tests/Exportaramus.Tests.csproj'
        prefix = [str(dotnet), verb, str(project)]
        if verb == 'build':
            args = ['-c', 'Release', '--no-restore', '--nologo', '-v:minimal']
        elif verb == 'restore':
            args = ['--locked-mode', '--nologo']
        else:
            args = ['-c', 'Release', '--no-restore', '--nologo', '--settings', str(snapshot / 'tests/coverage.runsettings'),
                    '--collect:XPlat Code Coverage', '--results-directory', str(snapshot.parent / 'tests'), '--logger', 'trx;LogFileName=tests.trx']
        return dict(command=prefix + args, workingDirectory=str(snapshot), trx=str(snapshot.parent / 'tests/tests.trx')), snapshot, dotnet

    def test_exact_restore_build_and_full_test_commands_pass(self):
        for verb in ('restore', 'build', 'test'):
            execution, snapshot, dotnet = self.command(verb)
            stage.verify_command(execution, verb, snapshot, dotnet)

    def test_config_filters_nobuild_and_warning_overrides_fail(self):
        for verb in ('build', 'test'):
            for flag in ('-p:NoWarn=CS8602', '-p:TreatWarningsAsErrors=false', '--filter=OnlyOne', '--no-build', '--list-tests', '-c:Debug'):
                execution, snapshot, dotnet = self.command(verb)
                execution['command'].append(flag)
                with self.subTest(verb=verb, flag=flag), self.assertRaises(ValueError):
                    stage.verify_command(execution, verb, snapshot, dotnet)

    def test_wrong_project_tool_cwd_and_runsettings_fail(self):
        for change in (lambda x: x['command'].__setitem__(0, 'other-dotnet'),
                       lambda x: x['command'].__setitem__(2, 'Other.csproj'),
                       lambda x: x.update(workingDirectory='elsewhere'),
                       lambda x: x['command'].__setitem__(8, 'relaxed.runsettings')):
            execution, snapshot, dotnet = self.command('test')
            change(execution)
            with self.assertRaises(ValueError):
                stage.verify_command(execution, 'test', snapshot, dotnet)

    def compiler_result(self):
        log = self.artifact('build.log', str(self.root / 'src/A.cs') + '(1,1): error CS8602: Dereference of a possibly null reference.\n')
        return dict(outcome='Error', errorKind='CompilerRejected', tests=None, file='src/A.cs',
                    build=dict(exitCode=1, timedOut=False, workingDirectory=str(self.root), log=log['path'], logSha256=log['sha256'], compilerDiagnosticCodes=['CS8602']))

    def test_compiler_rejection_is_distinct_from_kill(self):
        result = self.compiler_result()
        self.assertEqual(['CS8602'], stage.validate_nonviability(result, self.root))
        self.assertEqual('Error', result['outcome'])

    def test_success_timeout_tests_or_missing_diagnostics_cannot_prove_nonviability(self):
        for change in (lambda x: x['build'].update(exitCode=0), lambda x: x['build'].update(timedOut=True),
                       lambda x: x.update(outcome='Killed'), lambda x: x.update(tests={}),
                       lambda x: x['build'].update(compilerDiagnosticCodes=[]),
                       lambda x: x['build'].update(logSha256='wrong')):
            result = self.compiler_result()
            change(result)
            with self.assertRaises(ValueError):
                stage.validate_nonviability(result, self.root)

    def test_infrastructure_or_another_file_error_does_not_prove_mutant_nonviability(self):
        for text, code in [('CSC : error CS0006: Metadata file missing', 'CS0006'),
                           (str(self.root / 'src/Unrelated.cs') + '(1,1): error CS8602: Null access', 'CS8602')]:
            result = self.compiler_result()
            log = Path(result['build']['log'])
            log.write_text(text)
            result['build'].update(logSha256=stage.digest(log), compilerDiagnosticCodes=[code])
            with self.assertRaises(ValueError):
                stage.validate_nonviability(result, self.root)

    def causal_fixture(self, message='Assert.Equal() Failure\nExpected:1\nActual:0', secondary=True):
        test = self.artifact('tests/Exportaramus.Tests/A.cs', 'void RequiredOutput() { Assert.Equal(1, Value); }')
        source = self.artifact('src/A.cs', 'Value = 1;')
        result_path = Path(self.artifact('development/run/mutant-1/result.json', '{}')['path'])
        document = ET.Element('TestRun')
        rows = ET.SubElement(document, 'Results')
        def row(name, text):
            node = ET.SubElement(rows, 'UnitTestResult', outcome='Failed', testName=name)
            ET.SubElement(ET.SubElement(ET.SubElement(node, 'Output'), 'ErrorInfo'), 'Message').text = text
        row('RequiredOutput', message)
        if secondary:
            row('TimingBudget', 'Assert.True() Failure')
        trx = result_path.parent / 'tests.trx'
        ET.ElementTree(document).write(trx, encoding='utf-8')
        mutant = dict(id='1', status='Survived', statusReason=None, mutatorName='Boolean', location={}, replacement='false')
        result = dict(mutant=mutant, tests=dict(trx=str(trx), trxSha256=stage.digest(trx)))
        entry = dict(file='src/A.cs', sourceSha256=source['sha256'])
        first = dict(testName='RequiredOutput', messageSha256=stage.text_digest(message), classification='CausalAssertion',
                     reason='The required published value changed.', testSource=dict(path='tests/Exportaramus.Tests/A.cs', sha256=test['sha256']),
                     evidence=[dict(path='src/A.cs', sha256=source['sha256'])])
        failures = [first]
        if secondary:
            failures.append(dict(testName='TimingBudget', messageSha256=stage.text_digest('Assert.True() Failure'),
                                 classification='NotUsedForAdmission', reason='No causal claim for this secondary failure.'))
        review = dict(format='exportaramus-mutation-causal-review', schemaVersion='1.0', stageId='repair02',
                      file=entry['file'], sourceSha256=entry['sourceSha256'], mutant=copy.deepcopy(mutant), reviewed=True, reviewer='Independent',
                      result=dict(path=str(result_path), sha256=stage.digest(result_path)), trx=dict(path=str(trx), sha256=stage.digest(trx)), failures=failures)
        paths = {'tests/Exportaramus.Tests/A.cs': test['sha256'], 'src/A.cs': source['sha256']}
        return review, entry, result, result_path, paths

    def check_causal(self, fixture):
        review, entry, result, path, paths = fixture
        return stage.validate_causality(review, entry, result, path, 'repair02', self.root, paths)

    def test_causal_assertion_retains_secondary_unreviewed_failure(self):
        rows = self.check_causal(self.causal_fixture())
        self.assertEqual(['CausalAssertion', 'NotUsedForAdmission'], [x['classification'] for x in rows])

    def test_contract_exception_and_direct_operation_deadline_need_evidence(self):
        for classification, message in [('CausalContractFailure', 'JsonReaderException: expected JSON'),
                                        ('CausalOperationDeadline', 'Required operation did not finish before the fixture bound.')]:
            fixture = self.causal_fixture(message)
            fixture[0]['failures'][0]['classification'] = classification
            self.check_causal(fixture)
            fixture[0]['failures'][0]['evidence'] = []
            with self.assertRaises(ValueError):
                self.check_causal(fixture)

    def test_unrelated_or_unreviewed_only_failures_cannot_kill(self):
        for classification in ('Unrelated', 'NotUsedForAdmission'):
            fixture = self.causal_fixture()
            fixture[0]['failures'][0]['classification'] = classification
            with self.assertRaises(ValueError):
                self.check_causal(fixture)

    def test_missing_changed_duplicate_and_trimmed_failure_messages_fail(self):
        for change in (lambda x: x['failures'].pop(), lambda x: x['failures'].append(copy.deepcopy(x['failures'][0])),
                       lambda x: x['failures'][0].update(testName='Other'),
                       lambda x: x['failures'][0].update(messageSha256=stage.text_digest('Assert.Equal() Failure\r\nExpected:1\r\nActual:0'))):
            fixture = self.causal_fixture()
            change(fixture[0])
            with self.assertRaises(ValueError):
                self.check_causal(fixture)

    def test_causal_wrong_stage_source_result_and_unreviewed_claims_fail(self):
        for change in (lambda x: x.update(stageId='repair03'), lambda x: x.update(sourceSha256='wrong'),
                       lambda x: x.update(reviewed=False), lambda x: x['result'].update(sha256='wrong'),
                       lambda x: x['failures'][0]['testSource'].update(sha256='wrong'),
                       lambda x: x['mutant'].update(replacement='other')):
            fixture = self.causal_fixture()
            change(fixture[0])
            with self.assertRaises(ValueError):
                self.check_causal(fixture)

    def test_exception_cannot_be_mislabeled_assertion(self):
        with self.assertRaises(ValueError):
            self.check_causal(self.causal_fixture('TaskCanceledException'))

    def test_default_environment_does_not_invent_a_processor_probe(self):
        summary_path = Path(self.artifact('development/summary.json')['path'])
        evidence = self.artifact('launch.txt')
        summary = dict(sourceSha256='stage')
        review = dict(format='exportaramus-execution-environment-review', schemaVersion='1.0', reviewed=True, reviewer='Independent',
                      summary=dict(path=str(summary_path), sha256=stage.digest(summary_path)), sourceSha256='stage', stageId='repair02',
                      mode='DefaultInheritedEnvironment', actualChildRuntimeProcessorCount=None, environment={'DOTNET_PROCESSOR_COUNT': None},
                      buildTimeoutSeconds=120, testTimeoutSeconds=120, reason='Historical default inheritance, actual count unobserved.', evidence=[evidence])
        descriptor = dict(root=str(self.root), id='repair02')
        approved = {evidence['path']: evidence['sha256']}
        stage.validate_environment(review, summary, summary_path, descriptor, approved)
        for change in (lambda x: x.update(actualChildRuntimeProcessorCount=4),
                       lambda x: x['environment'].update(DOTNET_PROCESSOR_COUNT='4'),
                       lambda x: x.update(testTimeoutSeconds=600), lambda x: x.update(evidence=[])):
            changed = copy.deepcopy(review)
            change(changed)
            with self.assertRaises(ValueError):
                stage.validate_environment(changed, summary, summary_path, descriptor, approved)

    def test_multiset_continuity_rejects_same_count_different_cases(self):
        self.assertTrue(bridge.preserves_cases(['one', 'one'], ['one', 'one', 'two']))
        self.assertFalse(bridge.preserves_cases(['one', 'one'], ['one', 'two']))

    def execution_fixture(self):
        source = self.artifact('src/A.cs', 'true\n')
        self.artifact('tests/Exportaramus.Tests/Exportaramus.Tests.csproj', '<Project/>')
        self.artifact('tests/coverage.runsettings', '<RunSettings/>')
        tool_path = self.root / 'tools/individual-mutation.py'
        tool_path.parent.mkdir()
        shutil.copyfile(Path(__file__).with_name('individual-mutation.py'), tool_path)
        paths = {p.relative_to(self.root).as_posix(): stage.digest(p) for p in self.root.rglob('*') if p.is_file()}
        inv = self.artifact('development/inventory.json', '{}')
        repair_file = self.artifact('development/bridge.json', '{}')
        raw = self.artifact('development/original/combined/mutation-report.json', '{}')
        dotnet = self.artifact('development/toolchain/dotnet.exe')
        csc = self.artifact('development/toolchain/csc.dll')
        folder = self.root / 'development/shard'
        snapshot = folder / 'input'
        for relative in paths:
            destination = snapshot / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.root / relative, destination)
        mutant = dict(id='1', status='Survived', statusReason=None, mutatorName='Boolean', replacement='false',
                      location=dict(start=dict(line=1, column=1), end=dict(line=1, column=5)))
        project = snapshot / 'tests/Exportaramus.Tests/Exportaramus.Tests.csproj'
        def operation(name, verb, failed=0):
            directory = folder / name
            directory.mkdir(parents=True, exist_ok=True)
            log = directory / 'run.log'
            log.write_text('Completed')
            value = dict(command=[dotnet['path'], verb, str(project)], workingDirectory=str(snapshot),
                         exitCode=failed, timedOut=False, log=str(log), logSha256=stage.digest(log))
            if verb == 'restore':
                value['command'] += ['--locked-mode', '--nologo']
            elif verb == 'build':
                value['command'] += ['-c', 'Release', '--no-restore', '--nologo', '-v:minimal']
            else:
                trx = directory / 'tests.trx'
                document = ET.Element('TestRun')
                rows = ET.SubElement(document, 'Results')
                for index in range(2):
                    row = ET.SubElement(rows, 'UnitTestResult', testName='case' + str(index), outcome='Failed' if failed and index == 0 else 'Passed')
                    if failed and index == 0:
                        ET.SubElement(ET.SubElement(ET.SubElement(row, 'Output'), 'ErrorInfo'), 'Message').text = 'Assert.Equal() Failure'
                counts = dict(total=2, executed=2, passed=2 - failed, failed=failed, notExecuted=0)
                others = dict.fromkeys(('aborted', 'timeout', 'error', 'notRunnable', 'disconnected', 'inconclusive',
                                       'passedButRunAborted', 'warning', 'completed', 'inProgress', 'pending'), 0)
                summary = ET.SubElement(document, 'ResultSummary', outcome='Failed' if failed else 'Completed')
                ET.SubElement(summary, 'Counters', **{key: str(value) for key, value in (counts | others).items()})
                ET.ElementTree(document).write(trx)
                value.update(trx=str(trx), trxSha256=stage.digest(trx), counts=counts)
                value['command'] += ['-c', 'Release', '--no-restore', '--nologo', '--settings', str(snapshot / 'tests/coverage.runsettings'),
                                     '--collect:XPlat Code Coverage', '--results-directory', str(directory), '--logger', 'trx;LogFileName=tests.trx']
            return value
        baseline = dict(restore=operation('restore', 'restore'), build=operation('baseline-build', 'build'), tests=operation('baseline-tests', 'test'))
        result = dict(file='src/A.cs', sourceSha256=source['sha256'], originalSpan='true', mutant=mutant,
                      outcome='Killed', build=operation('mutant-1/build', 'build'), tests=operation('mutant-1/tests', 'test', 1))
        mutated_path = folder / 'mutant-1/mutated-source.cs'
        mutated_path.write_text('false\n')
        result.update(mutatedSource=str(mutated_path), mutatedSourceSha256=stage.digest(mutated_path))
        summary = dict(format='exportaramus-independent-mutation-shard', schemaVersion='1.1', sourceSha256='stagehash',
                       originalSourceSha256='originalhash', inventory=inv['path'], inventorySha256=inv['sha256'], report=raw['path'], reportSha256=raw['sha256'],
                       repairBridge=repair_file['path'], repairBridgeSha256=repair_file['sha256'], executor=str(tool_path), executorSha256=stage.digest(tool_path),
                       restoredInputsUnchanged=True, changedInputs=[], snapshot=str(snapshot), results=[result], baseline=baseline, expectedTestCount=2,
                       toolchain=dict(dotnet=dotnet, csc=csc))
        repair = dict(repairedInventory=dict(sha256='stagehash'), originalInventory=dict(sha256='originalhash'),
                      bridge=dict(repairedInventory=inv), repairedPaths=paths, bridgeSha256=repair_file['sha256'],
                      reportHash=raw['sha256'], run=Path(raw['path']).parent.parent, originalCases=['case0', 'case1'], repairedCases=['case0', 'case1'])
        entry = dict(mutant, file=result['file'], sourceSha256=result['sourceSha256'])
        return entry, repair, summary, folder

    def check_execution(self, fixture, cache=None):
        entry, repair, summary, folder = fixture
        path = folder / 'independent-mutation-summary.json'
        stage.save(path, summary)
        stage.save(folder / 'mutant-1/result.json', summary['results'][0])
        entry['independentExecution'] = dict(summary=str(path), sha256=stage.digest(path))
        return stage.verify_summary(entry, repair, legacy, self.root, set(), cache)

    def current_fixture(self):
        entry, repair, summary, folder = self.execution_fixture()
        receipt_path = summary.pop('repairBridge')
        receipt_hash = summary.pop('repairBridgeSha256')
        summary.pop('originalSourceSha256')
        summary.update(schemaVersion='1.2', currentCampaign=receipt_path, currentCampaignSha256=receipt_hash)
        binding = dict(kind='current-campaign', inventorySha256=repair['repairedInventory']['sha256'], inventoryArtifact=repair['bridge']['repairedInventory'], inventoryPaths=repair['repairedPaths'], reportHash=repair['reportHash'], reportPath=summary['report'], receiptPath=receipt_path, receiptSha256=receipt_hash, cases=['case0', 'case1'])
        return entry, binding, summary, folder

    def test_current_execution_is_explicit_and_preserves_full_case_identity(self):
        fixture = self.current_fixture()
        summary, result, _ = self.check_execution(fixture)
        self.assertEqual(summary['schemaVersion'], '1.2')
        stage.validate_kill(result, fixture[1], legacy, self.root)
        with self.assertRaises(ValueError):
            stage.validate_kill(result, dict(fixture[1], cases=['case0', 'other']), legacy, self.root)

    def test_current_execution_cannot_relabel_stage_report_source_receipt_or_executor(self):
        for key, value in [('schemaVersion', '1.1'), ('reportSha256', 'wrong'), ('sourceSha256', 'wrong'), ('inventorySha256', 'wrong'), ('executorSha256', 'wrong'), ('currentCampaignSha256', 'wrong'), ('repairBridge', 'claimed')]:
            fixture = self.current_fixture()
            fixture[2][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.check_execution(fixture)
            # Each fixture owns its root; reset between cases.
            shutil.rmtree(self.root)
            self.root.mkdir()

    def test_current_case_helper_rejects_wrong_target(self):
        with self.assertRaises(ValueError):
            stage.execution_cases({'kind': 'current-campaign', 'cases': ['one']}, 'consumerkit12')

    def test_current_environment_source_artifacts_require_exact_live_inventory(self):
        artifact = self.artifact('tools/individual-mutation.py', 'exact reviewed native executor')
        self.assertEqual(legacy.current_source_artifacts(self.root, {'tools/individual-mutation.py': artifact['sha256']}), {artifact['path']: artifact['sha256']})
        with self.assertRaises(ValueError):
            legacy.current_source_artifacts(self.root, {'tools/individual-mutation.py': 'different'})
        with self.assertRaises(ValueError):
            legacy.current_source_artifacts(self.root, {'../outside': artifact['sha256']})

    def test_native_current_execution_preserves_original_schema_and_status_eligibility(self):
        entry, binding, summary, folder = self.current_fixture()
        binding['kind'] = 'measured-native-campaign'
        binding['validatorInventoryPaths'] = dict(binding['inventoryPaths'])
        summary['schemaVersion'] = '1.0'
        summary.pop('currentCampaign'); summary.pop('currentCampaignSha256')
        entry.update(status='Ignored', statusReason=legacy.BLOCK_REASON)
        summary['results'][0]['mutant'].update(status='Ignored', statusReason=legacy.BLOCK_REASON)
        fixture = (entry, binding, summary, folder)
        import suite_binding
        with patch.object(suite_binding, 'verify_summary', wraps=suite_binding.verify_summary) as verify:
            _, result, _ = self.check_execution(fixture)
            self.assertEqual(verify.call_args.kwargs['validator_inventory'], binding['validatorInventoryPaths'])
            self.assertEqual(verify.call_args.args[1], binding['inventoryPaths'])
        held = binding.pop('validatorInventoryPaths')
        with self.assertRaises(ValueError):
            self.check_execution(fixture)
        binding['validatorInventoryPaths'] = held
        stage.validate_kill(result, binding, legacy, self.root)
        summary['currentCampaignSha256'] = binding['receiptSha256']
        with self.assertRaises(ValueError):
            self.check_execution(fixture)
        summary.pop('currentCampaignSha256')
        entry.update(status='Survived', statusReason=None)
        summary['results'][0]['mutant'].update(status='Survived', statusReason=None)
        with self.assertRaises(ValueError):
            self.check_execution(fixture)

    def test_current_reconciler_revalidates_receipt_and_rejects_old_ledger_identity(self):
        import current_campaign
        entry, binding, summary, folder = self.current_fixture()
        raw = {'files': {'src/A.cs': {'source': 'true\n', 'mutants': [entry]}}}
        Path(binding['reportPath']).write_text(json.dumps(raw))
        binding.update(reportHash=stage.digest(binding['reportPath']), sourcePaths={'src/A.cs'}, evidence=[])
        ledger = dict(schemaVersion='current-1.0', reviewer='reviewer', reportSha256=binding['reportHash'], sourceSha256=binding['inventorySha256'], currentCampaignSha256=binding['receiptSha256'], entries=[])
        with patch.object(current_campaign, 'validate_current_campaign', return_value=binding) as validate:
            self.assertEqual(legacy.reconcile_current(ledger, binding, raw, self.root), set())
            self.assertEqual(validate.call_count, 2)
        for key, value in [('schemaVersion', '2.0'), ('reportSha256', 'old'), ('sourceSha256', 'old'), ('currentCampaignSha256', 'old'), ('reviewer', '')]:
            with patch.object(current_campaign, 'validate_current_campaign', return_value=binding), self.subTest(key=key), self.assertRaises(ValueError):
                legacy.reconcile_current(dict(ledger, **{key: value}), binding, raw, self.root)
        with patch.object(current_campaign, 'validate_current_campaign', return_value=binding), self.assertRaises(ValueError):
            legacy.reconcile_current(ledger, binding, {'files': {}}, self.root)

    def equivalence_fixture(self):
        fixture = self.execution_fixture()
        entry, repair, summary, folder = fixture
        entry.update(status='CompileError', classification='IndependentlyEquivalent', executionStage='test-stage')
        result = summary['results'][0]
        result['mutant']['status'] = 'CompileError'
        result['outcome'] = 'Survived'
        tests = result['tests']
        trx = Path(tests['trx'])
        doc = ET.parse(trx)
        for row in doc.findall('.//UnitTestResult'):
            row.set('outcome', 'Passed')
            for child in list(row):
                row.remove(child)
        doc.find('.//ResultSummary').set('outcome', 'Completed')
        doc.find('.//Counters').set('passed', '2')
        doc.find('.//Counters').set('failed', '0')
        doc.write(trx)
        tests.update(exitCode=0, trxSha256=stage.digest(trx), counts=dict(total=2, executed=2, passed=2, failed=0, notExecuted=0))
        _, result, result_path = self.check_execution(fixture)
        source = dict(path='src/A.cs', sha256=entry['sourceSha256'])
        proof = dict(format='exportaramus-mutation-equivalence-review', schemaVersion='1.0', reviewed=True,
                     reviewer='Individual reviewer', reason='Exact reviewed source equivalence rationale.',
                     stageId='test-stage', file=entry['file'], sourceSha256=entry['sourceSha256'],
                     mutant=copy.deepcopy(result['mutant']), result=dict(path=str(result_path), sha256=stage.digest(result_path)),
                     source=source, evidence=[source])
        path = self.root / 'development/equivalence-review.json'
        stage.save(path, proof)
        entry['equivalenceReview'] = dict(path=str(path), sha256=stage.digest(path))
        return fixture, proof, result_path

    def test_execution_backed_equivalence_requires_complete_passing_replay_and_proof(self):
        fixture, proof, result_path = self.equivalence_fixture()
        entry, repair, summary, _ = fixture
        result = summary['results'][0]
        stage.validate_survival(result, repair, legacy, self.root)
        stage.validate_equivalence(proof, entry, result, result_path, 'test-stage', self.root, repair['repairedPaths'])
        self.assertTrue(legacy.independently_equivalent(entry, dict(sourceSha256='originalhash'), repair['reportHash'], result['mutant'], self.root, repair))
        with self.assertRaises(ValueError):
            legacy.independently_equivalent(entry, dict(sourceSha256='originalhash'), repair['reportHash'], result['mutant'], self.root)

    def test_execution_equivalence_rejects_unreviewed_or_mismatched_source_proof(self):
        fixture, proof, result_path = self.equivalence_fixture()
        entry, repair, summary, _ = fixture
        changes = [lambda p: p.update(reviewed=False), lambda p: p.update(reason=''),
                   lambda p: p.update(stageId='other'), lambda p: p.update(sourceSha256='other'),
                   lambda p: p['mutant'].update(replacement='other'), lambda p: p.update(evidence=[]),
                   lambda p: p['source'].update(path='../outside.cs'), lambda p: p['result'].update(sha256='other')]
        for change in changes:
            altered = copy.deepcopy(proof)
            change(altered)
            with self.subTest(change=change), self.assertRaises((ValueError, FileNotFoundError)):
                stage.validate_equivalence(altered, entry, summary['results'][0], result_path, 'test-stage', self.root, repair['repairedPaths'])

    def test_execution_equivalence_rejects_failure_timeout_partial_and_substituted_cases(self):
        fixture, _, _ = self.equivalence_fixture()
        entry, repair, summary, _ = fixture
        original = copy.deepcopy(summary['results'][0])
        for change in (lambda r: r.update(outcome='Killed'), lambda r: r['build'].update(exitCode=1),
                       lambda r: r['tests'].update(timedOut=True), lambda r: r['tests'].update(exitCode=1),
                       lambda r: r['tests']['counts'].update(executed=1)):
            changed = copy.deepcopy(original)
            change(changed)
            with self.assertRaises(ValueError):
                stage.validate_survival(changed, repair, legacy, self.root)
        trx = Path(original['tests']['trx'])
        doc = ET.parse(trx)
        doc.find('.//UnitTestResult').set('testName', 'substituted')
        doc.write(trx)
        original['tests']['trxSha256'] = stage.digest(trx)
        with self.assertRaises(ValueError):
            stage.validate_survival(original, repair, legacy, self.root)

    def test_execution_equivalence_status_scope_is_explicit(self):
        for status in ('CompileError', 'NoCoverage'):
            self.assertTrue(stage.equivalence_execution_eligible(dict(status=status)))
            self.assertTrue(legacy.equivalence_execution_eligible(dict(status=status)))
        for status in ('Killed', 'Timeout', 'RuntimeError', 'Pending', 'Survived', 'Ignored'):
            self.assertFalse(stage.equivalence_execution_eligible(dict(status=status)))
            self.assertFalse(legacy.equivalence_execution_eligible(dict(status=status)))
        self.assertTrue(stage.equivalence_execution_eligible(dict(status='Ignored', statusReason=legacy.BLOCK_REASON)))

    def test_exact_stage_summary_baseline_and_mutation_binding_pass(self):
        fixture = self.execution_fixture()
        _, result, _ = self.check_execution(fixture)
        stage.validate_kill(result, fixture[1], legacy, self.root)

    def test_old_summary_cannot_be_relabeled_as_final_inventory_or_bridge(self):
        fixture = self.execution_fixture()
        original = copy.deepcopy(fixture[2])
        for field in ('sourceSha256', 'inventorySha256', 'repairBridgeSha256', 'originalSourceSha256', 'executorSha256', 'reportSha256'):
            fixture[2].clear()
            fixture[2].update(copy.deepcopy(original))
            fixture[2][field] = 'different'
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.check_execution(fixture)

    def test_changed_restored_snapshot_and_mutated_bytes_are_rejected(self):
        fixture = self.execution_fixture()
        snapshot = Path(fixture[2]['snapshot']) / 'src/A.cs'
        snapshot.write_text('changed')
        with self.assertRaises(ValueError):
            self.check_execution(fixture)
        snapshot.write_text('true\n')
        Path(fixture[2]['results'][0]['mutatedSource']).write_text('different')
        with self.assertRaises(ValueError):
            self.check_execution(fixture)

    def test_same_count_different_baseline_cases_are_rejected(self):
        fixture = self.execution_fixture()
        execution = fixture[2]['baseline']['tests']
        trx = Path(execution['trx'])
        doc = ET.parse(trx)
        doc.find('.//UnitTestResult').set('testName', 'substituted-case')
        doc.write(trx)
        execution['trxSha256'] = stage.digest(trx)
        with self.assertRaises(ValueError):
            self.check_execution(fixture)

    def test_partial_filtered_and_timeout_mutant_results_do_not_kill(self):
        fixture = self.execution_fixture()
        _, result, _ = self.check_execution(fixture)
        original = copy.deepcopy(result)
        for change in (lambda x: x['tests'].update(timedOut=True), lambda x: x['tests'].update(exitCode=2),
                       lambda x: x['tests']['counts'].update(executed=1), lambda x: x.update(outcome='Timeout'),
                       lambda x: x['tests']['command'].append('--filter=OnlyOne'), lambda x: x['build'].update(exitCode=1)):
            changed = copy.deepcopy(original)
            change(changed)
            with self.assertRaises(ValueError):
                stage.validate_kill(changed, fixture[1], legacy, self.root)

    def test_baseline_validation_is_cached_by_exact_summary_hash(self):
        fixture = self.execution_fixture()
        cache = {}
        with patch.object(legacy, 'execution_counts', wraps=legacy.execution_counts) as check:
            self.check_execution(fixture, cache)
            self.check_execution(fixture, cache)
            self.assertEqual(1, check.call_count)
        self.assertEqual(1, len(cache))
        key = next(iter(cache))
        Path(key[0]).write_text('tampered after cache validation')
        with self.assertRaises(ValueError):
            stage.bound(dict(path=key[0], sha256=key[1]), self.root)

    def test_suite_validation_is_cached_only_after_complete_summary_validation(self):
        import suite_binding
        fixture = self.execution_fixture()
        cache = {}
        with patch.object(suite_binding, 'verify_summary', wraps=suite_binding.verify_summary) as check:
            self.check_execution(fixture, cache)
            self.check_execution(fixture, cache)
            self.assertEqual(1, check.call_count)
            self.assertEqual(['src/A.cs'], check.call_args.args[2])
            fixture[2]['reviewStatus'] = 'same execution, different summary bytes'
            self.check_execution(fixture, cache)
            self.assertEqual(2, check.call_count)
        snapshot = Path(fixture[2]['snapshot']) / 'src/A.cs'
        snapshot.write_text('changed after successful validation')
        with self.assertRaises(ValueError): self.check_execution(fixture, cache)

    def test_successful_compilation_reopens_static_nonviability(self):
        fixture = self.execution_fixture()
        self.check_execution(fixture)
        summary = fixture[3] / 'independent-mutation-summary.json'
        descriptor = dict(root=str(self.root), executionSummaries=[dict(path=str(summary), sha256=stage.digest(summary))])
        errors = stage.inventory_executions(descriptor, fixture[1], fixture[1]['reportHash'], {('src/A.cs', '1')})
        self.assertEqual(['Successful ordinary compilation contradicts nonviability: 1'], errors)
        self.assertEqual([], stage.inventory_executions(descriptor, fixture[1], fixture[1]['reportHash'], set()))

    def test_execution_inventory_cannot_omit_duplicate_or_relabel_runs(self):
        fixture = self.execution_fixture()
        self.check_execution(fixture)
        summary = fixture[3] / 'independent-mutation-summary.json'
        item = dict(path=str(summary), sha256=stage.digest(summary))
        descriptor = dict(root=str(self.root), executionSummaries=[item])
        for values in ([], [item, item]):
            with self.assertRaises(ValueError):
                stage.inventory_executions(dict(descriptor, executionSummaries=values), fixture[1], fixture[1]['reportHash'], set())
        other = self.root / 'development/other/independent-mutation-summary.json'
        other.parent.mkdir()
        other.write_bytes(summary.read_bytes())
        with self.assertRaises(ValueError):
            stage.inventory_executions(descriptor, fixture[1], fixture[1]['reportHash'], set())
        descriptor['executionSummaries'].append(dict(path=str(other), sha256=stage.digest(other)))
        stage.inventory_executions(descriptor, fixture[1], fixture[1]['reportHash'], set())
        with self.assertRaises(ValueError):
            stage.inventory_executions(descriptor, fixture[1], 'different-report', set())

    def cpu_fixture(self):
        summary_path = Path(self.artifact('development/cpu/independent-mutation-summary.json')['path'])
        artifacts = {key: self.artifact('external/' + key) for key in
                     ('launcher', 'executor', 'probe', 'probeSource', 'probeRuntimeConfig', 'dotnet', 'selection', 'ids', 'log')}
        approved = {value['path']: value['sha256'] for value in artifacts.values()}
        summary = dict(sourceSha256='stage', executorSha256=artifacts['executor']['sha256'], expectedTestCount=2,
                       inventory=str(self.root / 'inventory.json'), repairBridge=str(self.root / 'bridge.json'), selectedIds=['1', '2'])
        command = ['python', artifacts['executor']['path'], '--root', str(self.root), '--output', str(summary_path.parent),
                   '--inventory', summary['inventory'], '--repair-bridge', summary['repairBridge'], '--expected-test-count', '2', '--ids', '1,2']
        receipt = dict(artifacts, status='CompletedSupplementalEvidence', actualChildRuntimeProcessorCount=4,
                       environment={'DOTNET_PROCESSOR_COUNT': '4'}, command=command, buildTimeoutSeconds=120, testTimeoutSeconds=120,
                       summary=dict(path=str(summary_path), sha256=stage.digest(summary_path)))
        path = self.root / 'external/launcher-receipt.json'
        stage.save(path, receipt)
        approved[str(path)] = stage.digest(path)
        review = dict(format='exportaramus-execution-environment-review', schemaVersion='1.0', reviewer='Independent', reviewed=True,
                      summary=receipt['summary'], sourceSha256='stage', stageId='repair02', mode='ProcessorCountOverride',
                      buildTimeoutSeconds=120, testTimeoutSeconds=120, reason='Only CPU partitioning changed; actual child count observed.',
                      evidence=[artifacts['launcher']], launcherReceipt=dict(path=str(path), sha256=stage.digest(path)),
                      environment=receipt['environment'], actualChildRuntimeProcessorCount=4)
        descriptor = dict(root=str(self.root), id='repair02')
        return review, summary, summary_path, descriptor, approved, receipt, path

    def test_cpu_override_binds_launcher_probe_environment_and_exact_shard(self):
        fixture = self.cpu_fixture()
        stage.validate_environment(*fixture[:5])

    def test_cpu_missing_probe_changed_count_stale_summary_and_other_output_fail(self):
        for change in (lambda x: x.update(actualChildRuntimeProcessorCount=8),
                       lambda x: x['probe'].update(sha256='wrong'), lambda x: x.update(status='Running'),
                       lambda x: x['summary'].update(sha256='wrong'),
                       lambda x: x['command'].__setitem__(x['command'].index('--output') + 1, 'elsewhere'),
                       lambda x: x.update(testTimeoutSeconds=600)):
            fixture = self.cpu_fixture()
            review, _, _, _, approved, receipt, path = fixture
            change(receipt)
            stage.save(path, receipt)
            review['launcherReceipt']['sha256'] = approved[str(path)] = stage.digest(path)
            with self.assertRaises(ValueError):
                stage.validate_environment(*fixture[:5])


if __name__ == '__main__':
    unittest.main()
