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

    def test_bound_normal_failure_resolves_survived_and_timeout_only_in_bridge(self):
        for status in ('Survived','Timeout'):
            self.assertFalse(self.resolve(status)[2])
            self.assertTrue(self.resolve(status,False)[2])
        for status in ('NoCoverage','RuntimeError','Pending'):
            self.assertTrue(self.resolve(status)[2])

    def test_changed_binding_or_incomplete_execution_fails(self):
        pristine=copy.deepcopy(self.fixture.independent)
        for mutate in [lambda x:x.update(repairBridgeSha256='wrong'),lambda x:x.update(originalSourceSha256='wrong'),lambda x:x.update(inventorySha256='wrong'),lambda x:x.update(executorSha256='wrong'),lambda x:x['results'][0]['tests'].update(timedOut=True),lambda x:x['results'][0].update(outcome='Timeout'),lambda x:x['results'][0]['tests']['command'].append('--filter=OnlyOne')]:
            self.fixture.independent=copy.deepcopy(pristine)
            mutate(self.fixture.independent)
            self.assertTrue(self.resolve()[2])

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

if __name__=='__main__': unittest.main()
