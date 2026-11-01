"""Fail-closed repair bridge checks; no product campaign substitution."""
from pathlib import Path
import copy
import json
import tempfile
import unittest
import xml.etree.ElementTree as ET
import importlib.util
import sys
sys.dont_write_bytecode = True
import repair_bridge as bridge
spec = importlib.util.spec_from_file_location('existing_checks', Path(__file__).with_name('test-mutation-orchestration.py'))
existing = importlib.util.module_from_spec(spec)
spec.loader.exec_module(existing)

class BridgeChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.evidence = self.root / 'review.txt'
        self.evidence.write_text('Reviewed exact diff: existing assertions and cases preserved.')

    def change(self, path):
        return dict(path=path,beforeSha256='old',afterSha256='new',kind='TestStrengthening' if path.endswith('.cs') else 'Tooling',reason='Reviewed exact strengthening diff',preservesExistingAssertions=True,evidence=[dict(path=str(self.evidence),sha256=bridge.digest(self.evidence))])

    def test_only_exact_tools_and_test_sources_are_eligible(self):
        for path in ['tests/Exportaramus.Tests/New.cs', *bridge.BRIDGE_TOOL_FILES]:
            self.assertTrue(bridge.validate_differences({path:'old'},{path:'new'},[self.change(path)],self.root))
        for path in ['tools/contract-quality-scope.py','tools/Get-SourceInventory.ps1','tools/arbitrary.py','src/A.cs','reference/Kit.cs','data/a.json','tests/stryker-config.json','tests/Exportaramus.Tests/Exportaramus.Tests.csproj']:
            with self.subTest(path=path), self.assertRaises(ValueError):
                bridge.validate_differences({path:'old'},{path:'new'},[self.change(path)],self.root)

    def test_exact_diff_hash_review_and_no_deletion_required(self):
        path='tests/Exportaramus.Tests/A.cs'
        for mutate in [lambda x:x.update(beforeSha256='wrong'),lambda x:x.update(afterSha256='wrong'),lambda x:x.update(evidence=[]),lambda x:x.update(preservesExistingAssertions=False),lambda x:x.update(reason=''),lambda x:x.update(kind='Tooling')]:
            entry=self.change(path)
            mutate(entry)
            with self.assertRaises(ValueError):
                bridge.validate_differences({path:'old'},{path:'new'},[entry],self.root)
        for changes,repaired in [([], {path:'new'}),([self.change(path)]*2,{path:'new'}),([self.change(path)],{}),([self.change(path)],{path:'new','src/extra.cs':'new'})]:
            with self.assertRaises(ValueError):
                bridge.validate_differences({path:'old'},repaired,changes,self.root)

    def test_missing_repaired_baseline_fails_without_any_kill_ledger(self):
        with self.assertRaises(KeyError):
            bridge.repaired_baseline({},self.root,{'sha256':'repaired'},['original'])

    def test_reviewer_and_original_bindings_required(self):
        path=self.root/'bridge.json'
        for value in [{},{'format':'exportaramus-mutation-repair-bridge','schemaVersion':'1.0','reviewer':''}]:
            path.write_text(json.dumps(value))
            with self.assertRaises(ValueError): bridge.validate_bridge(path,self.root)

class ProofChecks(unittest.TestCase):
    def setUp(self):
        self.fixture=existing.LedgerChecks()
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        f=self.fixture
        f.independent_proof()
        for execution in [f.independent['baseline']['tests'],f.independent['results'][0]['tests']]:
            path=Path(execution['trx'])
            document=ET.parse(path)
            for i,row in enumerate(document.findall('.//UnitTestResult')): row.set('testName','case'+str(i))
            document.write(path)
            execution['trxSha256']=bridge.digest(path)
        b=f.root/'bridge.json'; b.write_text('{}')
        inv=f.root/'inventory.json'; inv.write_text('{}')
        self.repair=dict(repairedInventory={'sha256':'repaired'},repairedPaths={'tools/individual-mutation.py':'executor'},bridgeSha256=bridge.digest(b),bridge={'repairedInventory':{'sha256':bridge.digest(inv)}},originalCases=['case0'],repairedCases=['case0','case1'])
        f.independent.update(schemaVersion='1.1',sourceSha256='repaired',originalSourceSha256='whole',repairBridge=str(b),repairBridgeSha256=bridge.digest(b),inventory=str(inv),inventorySha256=bridge.digest(inv))

    def resolve(self,status='Survived',repair=True):
        f=self.fixture
        f.mutant['status']=f.entry['status']=f.independent['results'][0]['mutant']['status']=status
        f.save_independent()
        return existing.review.reconcile(f.ledger,f.before,'report',f.scope,f.report,f.root,self.repair if repair else None)

    def test_bound_normal_failure_resolves_replay_statuses_only_in_bridge(self):
        for status in ('Survived','Timeout','NoCoverage'):
            self.assertFalse(self.resolve(status)[2])
            self.assertTrue(self.resolve(status,False)[2])
        for status in ('RuntimeError','Pending'):
            self.assertTrue(self.resolve(status)[2])

    def test_changed_binding_or_incomplete_execution_fails(self):
        pristine=copy.deepcopy(self.fixture.independent)
        for mutate in [lambda x:x.update(repairBridgeSha256='wrong'),lambda x:x.update(originalSourceSha256='wrong'),lambda x:x.update(inventorySha256='wrong'),lambda x:x.update(executorSha256='wrong'),lambda x:x['results'][0]['tests'].update(timedOut=True),lambda x:x['results'][0].update(outcome='Timeout'),lambda x:x['results'][0]['tests']['command'].append('--filter=OnlyOne')]:
            self.fixture.independent=copy.deepcopy(pristine)
            mutate(self.fixture.independent)
            self.assertTrue(self.resolve()[2])
            self.assertTrue(self.resolve('NoCoverage')[2])

    def test_same_count_different_cases_fails(self):
        execution=self.fixture.independent['results'][0]['tests']
        path=Path(execution['trx']); doc=ET.parse(path)
        doc.find('.//UnitTestResult').set('testName','different')
        doc.write(path); execution['trxSha256']=bridge.digest(path)
        self.assertTrue(self.resolve()[2])

    def test_unconditional_baseline_requires_passing_cases_and_strict_coverage(self):
        f=self.fixture
        summary=dict(sourceSha256='repaired',linesValid=2,linesCovered=2,branchesValid=2,branchesCovered=2,trx=f.independent['baseline']['tests']['trx'],trxSha256=f.independent['baseline']['tests']['trxSha256'])
        report=f.root/'deployment/In/coverage.cobertura.xml'
        report.parent.mkdir(parents=True)
        report.write_text('<coverage lines-valid="2" lines-covered="2" branches-valid="2" branches-covered="2"/>')
        trx=Path(summary['trx']); doc=ET.parse(trx)
        ET.SubElement(ET.SubElement(doc.getroot(),'TestSettings'),'Deployment',runDeploymentRoot='deployment')
        ET.SubElement(ET.SubElement(doc.getroot(),'UriAttachment'),'A',href='coverage.cobertura.xml')
        doc.write(trx);summary['trxSha256']=bridge.digest(trx)
        summary.update(report=str(report),reportSha256=bridge.digest(report))
        path=f.root/'coverage.json'
        def check():
            path.write_text(json.dumps(summary))
            return bridge.repaired_baseline({'repairedBaselineSummary':{'path':str(path),'sha256':bridge.digest(path)}},f.root,{'sha256':'repaired'},['case0'])
        self.assertEqual(['case0','case1'],check()[0])
        for key,value in [('sourceSha256','stale'),('linesCovered',1),('branchesCovered',1),('trxSha256','wrong'),('reportSha256','wrong'),('report',str(f.root/'missing.xml'))]:
            old=summary[key];summary[key]=value
            with self.assertRaises(ValueError):check()
            summary[key]=old
        normal=trx.read_bytes()
        for duplicate in (False,True):
            doc=ET.fromstring(normal)
            if duplicate: ET.SubElement(ET.SubElement(doc,'UriAttachment'),'A',href='coverage.cobertura.xml')
            else: doc.find('UriAttachment/A').set('href','other/coverage.cobertura.xml')
            ET.ElementTree(doc).write(trx);summary['trxSha256']=bridge.digest(trx)
            with self.assertRaises(ValueError):check()
        trx.write_bytes(normal);summary['trxSha256']=bridge.digest(trx)
        report.write_text('<coverage lines-valid="2" lines-covered="1" branches-valid="2" branches-covered="2"/>')
        summary['reportSha256']=bridge.digest(report)
        with self.assertRaises(ValueError): check()

class TargetBridgeChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def test_target_cases_never_fall_back_to_provider(self):
        repair = {'originalCases': ['provider'], 'repairedCases': ['provider', 'extra']}
        self.assertEqual((['provider'], ['provider', 'extra']), bridge.stage_cases(repair, 'provider'))
        for target in ('inventoryreader', 'consumerkit12', 'other'):
            with self.assertRaises(ValueError): bridge.stage_cases(repair, target)
        repair['targetCases'] = {'inventoryreader': {'original': ['reader'], 'repaired': ['reader', 'new']}}
        self.assertEqual((['reader'], ['reader', 'new']), bridge.stage_cases(repair, 'inventoryreader'))

    def test_v3_test_scope_does_not_allow_project_or_production_changes(self):
        fixture = BridgeChecks(); fixture.setUp(); self.addCleanup(fixture.doCleanups)
        for name in ('tests/Exportaramus.ConsumerKit.Tests/A.cs', 'tests/Exportaramus.InventoryReader.Mutation.Tests/A.cs'):
            with self.assertRaises(ValueError):
                bridge.validate_differences({name:'old'}, {name:'new'}, [fixture.change(name)], fixture.root)
            bridge.validate_differences({name:'old'}, {name:'new'}, [fixture.change(name)], fixture.root, multi_suite=True)
        for name in ('src/Contracts/1.2/A.cs', 'tests/Other/A.cs', 'tests/Exportaramus.ConsumerKit.Tests/A.csproj', 'tests/stryker-config.json', 'README.md'):
            with self.assertRaises(ValueError):
                bridge.validate_differences({name:'old'}, {name:'new'}, [fixture.change(name)], fixture.root, multi_suite=True)

    def trx(self):
        fixture = existing.LedgerChecks(); fixture.setUp(); self.addCleanup(fixture.tearDown); fixture.independent_proof()
        source = Path(fixture.independent['baseline']['tests']['trx'])
        doc = ET.parse(source)
        for i,row in enumerate(doc.findall('.//UnitTestResult')): row.set('testName','case'+str(i))
        definitions = ET.SubElement(doc.getroot(), 'TestDefinitions')
        for i in range(2):
            unit = ET.SubElement(definitions, 'UnitTest', storage='Exportaramus.ConsumerKit.Tests.dll')
            ET.SubElement(unit, 'TestMethod', codeBase='Exportaramus.ConsumerKit.Tests.dll')
        trx = self.root/'tests.trx'; doc.write(trx)
        return trx

    def test_trx_rejects_wrong_or_missing_assembly_definitions(self):
        trx = self.trx()
        self.assertEqual(['case0','case1'], bridge.canonical_trx(trx,'consumerkit12'))
        with self.assertRaises(ValueError): bridge.canonical_trx(trx,'inventoryreader')
        doc = ET.parse(trx); doc.getroot().remove(doc.find('TestDefinitions')); doc.write(trx)
        with self.assertRaises(ValueError): bridge.canonical_trx(trx,'consumerkit12')

    def test_only_explicit_removed_offline_timing_policy_is_allowed(self):
        change = dict(path='tests/Exportaramus.Tests/Diagnostics/DiagnosticWorkerTest.cs',
                      preservesExistingAssertions=False, preservesFixtureSemantics=True, noNewSkips=True,
                      noRelaxedDeadlines=False, timingPolicy='NoAbsoluteOfflineTimingGates',
                      noRelaxedExecutionDeadlines=True, preservesFunctionalAssertions=True)
        bridge.test_obligations(change, multi_suite=True)
        with self.assertRaises(ValueError):bridge.test_obligations(change)
        for key,value in [('path','tests/Other.cs'),('timingPolicy','anything'),('noRelaxedExecutionDeadlines',False),('preservesFunctionalAssertions',False),('noNewSkips',False)]:
            altered=dict(change);altered[key]=value
            with self.assertRaises(ValueError):bridge.test_obligations(altered,multi_suite=True)

    def test_both_canonical_targets_required(self):
        for baselines in ({}, {'inventoryreader':{}}, {'inventoryreader':{},'consumerkit12':{},'other':{}}):
            with self.assertRaises(ValueError): bridge.target_baselines({'targetBaselines':baselines}, self.root,self.root,{}, {})

    def coverage(self):
        trx = self.trx(); doc = ET.parse(trx)
        ET.SubElement(ET.SubElement(doc.getroot(),'TestSettings'),'Deployment',runDeploymentRoot='deployment')
        ET.SubElement(ET.SubElement(doc.getroot(),'UriAttachment'),'A',href='coverage.cobertura.xml');doc.write(trx)
        report = self.root/'deployment/In/coverage.cobertura.xml';report.parent.mkdir(parents=True)
        xml = ET.Element('coverage'); classes = ET.SubElement(xml,'classes');summary={};paths={}
        for key,prefix,source in [('consumerClient','Exportaramus.ConsumerKit.V1_2.CollectionDataIpcClient','src/Contracts/1.2/Consumer/CollectionDataIpcClient.cs'),('inventoryReader','CollectionExample.InventoryItemReader','examples/CollectionExample/InventoryItemReader.cs')]:
            path=self.root/source;path.parent.mkdir(parents=True,exist_ok=True);path.write_text('bound source');paths[source]=bridge.digest(path)
            cls=ET.SubElement(classes,'class',name=prefix,filename=source)
            ET.SubElement(ET.SubElement(cls,'lines'),'line',number='1',hits='1',branch='True',attrib={'condition-coverage':'100% (1/1)'})
            summary[key]=dict(component=prefix,report=str(report),trx=str(trx),trxSha256=bridge.digest(trx),linesValid=1,linesCovered=1,branchesValid=1,branchesCovered=1)
        ET.ElementTree(xml).write(report)
        for v in summary.values(): v['reportSha256']=bridge.digest(report)
        return summary,paths,report,trx

    def test_exact_component_coverage_and_attachment_required(self):
        summary,paths,report,trx=self.coverage()
        self.assertEqual(4,len(bridge.component_coverage(summary,self.root,paths)))
        for key,value in [('component','other'),('linesCovered',0),('branchesValid',2),('reportSha256','wrong'),('trxSha256','wrong')]:
            changed=copy.deepcopy(summary);changed['inventoryReader'][key]=value
            with self.assertRaises(ValueError):bridge.component_coverage(changed,self.root,paths)
        raw=report.read_bytes()
        for mutation in ('uncovered','wrong-source','duplicate','bad-branch'):
            xml=ET.fromstring(raw);cls=xml.find('classes/class');line=cls.find('lines/line')
            if mutation=='uncovered':line.set('hits','0')
            if mutation=='wrong-source':cls.set('filename','src/Unrelated.cs')
            if mutation=='duplicate':xml.find('classes').append(copy.deepcopy(cls))
            if mutation=='bad-branch':line.set('condition-coverage','100% (0/0)')
            ET.ElementTree(xml).write(report);changed=copy.deepcopy(summary)
            for v in changed.values():v['reportSha256']=bridge.digest(report)
            with self.assertRaises(ValueError):bridge.component_coverage(changed,self.root,paths)
        report.write_bytes(raw);doc=ET.parse(trx);doc.find('UriAttachment/A').set('href','other.xml');doc.write(trx)
        for v in summary.values():v['trxSha256']=bridge.digest(trx)
        with self.assertRaises(ValueError):bridge.component_coverage(summary,self.root,paths)

if __name__=='__main__': unittest.main()
