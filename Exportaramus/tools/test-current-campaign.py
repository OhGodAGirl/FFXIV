import unittest,tempfile,hashlib,importlib.util
from pathlib import Path
import current_campaign as current
from unittest.mock import patch
from types import SimpleNamespace

class CurrentCampaignTests(unittest.TestCase):
    def test_exact_reviewed_tool_transition(self):
        change={'path':'tools/individual-mutation.py','beforeSha256':'old','afterSha256':'new','reviewed':True,'reviewer':'reviewer','reviewEvidence':{}}
        with patch.object(current.report_set, 'read', return_value=dict(change, reason='Exact reviewed validator change')):
            current.tool_transition({'src/a.cs':'a','tools/individual-mutation.py':'old'}, {'src/a.cs':'a','tools/individual-mutation.py':'new'}, [change])
    def test_suite_command_validator_requires_exact_review(self):
        path='tools/suite_binding.py'
        change={'path':path,'beforeSha256':'old','afterSha256':'new','reviewed':True,'reviewer':'reviewer','reviewEvidence':{}}
        with patch.object(current.report_set,'read',return_value=dict(change,reason='Exact command validation for unaccepted incomplete timeout records.')):
            current.tool_transition({path:'old'},{path:'new'},[change])
        with self.assertRaises(ValueError):current.tool_transition({path:'old'},{path:'new'},[])
        with self.assertRaises(ValueError):current.tool_transition({path:'old'},{},[change])

    def test_renderer_tools_require_exact_review_and_cannot_be_deleted(self):
        for name in ('mutation_rendering.py','mutation-renderer.cs','test-mutation-rendering.py','repair_bridge.py','test-repair-bridge.py'):
            path='tools/'+name
            change={'path':path,'beforeSha256':'old','afterSha256':'new','reviewed':True,'reviewer':'reviewer','reviewEvidence':{}}
            with self.subTest(path=path), patch.object(current.report_set,'read',return_value=dict(change,reason='Reviewed exact AST rendering transition')):
                current.tool_transition({path:'old'},{path:'new'},[change])
                with self.assertRaises(ValueError): current.tool_transition({path:'old'},{path:'new'},[])
                with self.assertRaises(ValueError): current.tool_transition({path:'old'},{},[change])
                bad=dict(change,beforeSha256='forged')
                with self.assertRaises(ValueError): current.tool_transition({path:'old'},{path:'new'},[bad])

    def test_deleted_tool_rejected(self):
        with self.assertRaises(ValueError):current.tool_transition({'tools/individual-mutation.py':'a'}, {},[{'path':'tools/individual-mutation.py','beforeSha256':'a','afterSha256':None,'reviewed':True,'reviewer':'reviewer'}])
    def test_production_change_rejected(self):
        with self.assertRaises(ValueError):current.tool_transition({'src/a.cs':'a'},{'src/a.cs':'b'},[{'path':'src/a.cs','beforeSha256':'a','afterSha256':'b','reviewed':True,'reviewer':'reviewer'}])
    def test_missing_tool_review_rejected(self):
        with self.assertRaises(ValueError):current.tool_transition({'tools/individual-mutation.py':'a'},{'tools/individual-mutation.py':'b'},[])
    def test_forged_tool_hash_rejected(self):
        with self.assertRaises(ValueError):current.tool_transition({'tools/individual-mutation.py':'a'},{'tools/individual-mutation.py':'b'},[{'path':'tools/individual-mutation.py','beforeSha256':'wrong','afterSha256':'b','reviewed':True,'reviewer':'reviewer'}])
    def test_unreviewed_change_rejected(self):
        with self.assertRaises(ValueError):current.tool_transition({}, {'tools/current_campaign.py':'a'},[{'path':'tools/current_campaign.py','beforeSha256':None,'afterSha256':'a','reviewed':False,'reviewer':'reviewer'}])
    def test_outside_file_even_if_ignored_is_rejected(self):
        with self.assertRaises(ValueError):current.validate_selection({'sourcePaths':{'src/a.cs'}},[('src/b.cs','source',{'status':'Ignored'})])
    def test_exact_selected_file(self):
        current.validate_selection({'sourcePaths':{'src/a.cs'}},[('src/a.cs','source',{'status':'Survived'})])
    def test_missing_baseline_case_rejected(self):
        with self.assertRaises(ValueError):current.validate_cases({'cases':{'a':1,'b':1}},{'a':1})
    def test_duplicate_case_count_rejected(self):
        with self.assertRaises(ValueError):current.validate_cases({'cases':{'a':2}},{'a':1})
    def test_exact_cases(self):current.validate_cases({'cases':{'a':2}},{'a':2})
    def test_forged_report_hash_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'report.json';p.write_text('{}')
            with self.assertRaises(ValueError):current.validate_report_identity({'path':str(p),'sha256':'0'*64},p)
    def test_different_report_path_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'report.json';q=Path(d)/'other.json';p.write_text('{}');q.write_text('{}');h=hashlib.sha256(p.read_bytes()).hexdigest()
            with self.assertRaises(ValueError):current.validate_report_identity({'path':str(p),'sha256':h},q)
    def test_stale_executor_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            paths={}
            for n in ('individual-mutation.py','current_campaign.py','report_set_review.py','repair_bridge.py','suite_binding.py','qualification_inputs.py'):
                p=Path(d)/n;p.write_bytes((Path(__file__).parent/n).read_bytes());paths['tools/'+n]=hashlib.sha256(p.read_bytes()).hexdigest()
            current.validate_tool_identity(paths,d)
            (Path(d)/'individual-mutation.py').write_text('changed')
            with self.assertRaises(ValueError):current.validate_tool_identity(paths,d)

    def test_review_artifact_must_be_affirmative(self):
        change={'path':'tools/current_campaign.py','beforeSha256':None,'afterSha256':'new','reviewed':True,'reviewer':'reviewer','reviewEvidence':{}}
        with patch.object(current.report_set,'read',return_value=dict(change,reviewed=False,reason='Rejected')):
            with self.assertRaises(ValueError):current.tool_transition({}, {'tools/current_campaign.py':'new'},[change])
    def test_required_transitive_validator_must_be_present(self):
        with self.assertRaises(ValueError):current.validate_tool_identity({},Path(__file__).parent)
    def test_actual_runner_metadata_mismatch_rejected(self):
        with tempfile.TemporaryDirectory() as d, patch.object(current.report_set.shutil,'which',return_value='pwsh'), patch.object(current.report_set,'bound',return_value=Path(d)/'runner.dll'), patch.object(current.report_set,'recorded_run',return_value=(SimpleNamespace(returncode=0,stdout='4.15.0'),[])):
            with self.assertRaises(ValueError):current.validate_runner_version({'canonicalRunner':{},'canonicalToolVersion':'4.16.0+exact'},Path(d))

    def test_schema_one_rejects_test_changes(self):
        with self.assertRaises(ValueError):current.validate_input_transition({'tests/Exportaramus.Tests/A.cs':'a'}, {'tests/Exportaramus.Tests/A.cs':'b'}, {'schemaVersion':'1.0','toolChanges':[],'testChanges':[]},Path('.'))
    def test_reviewed_test_strengthening(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);proof=root/'review.json';proof.write_text('independent test source review')
            entry={'path':'tests/Exportaramus.Tests/A.cs','kind':'TestStrengthening','beforeSha256':'a','afterSha256':'b','reason':'Add an immediate public identity assertion before waiting for owned work.','reviewed':True,'reviewer':'reviewer','preservesFixtureSemantics':True,'noNewSkips':True,'noRelaxedDeadlines':True,'preservesExistingAssertions':True,'evidence':[{'path':str(proof),'sha256':hashlib.sha256(proof.read_bytes()).hexdigest()}]}
            receipt={'schemaVersion':'2.0','toolChanges':[],'testChanges':[entry]}
            current.validate_input_transition({entry['path']:'a'},{entry['path']:'b'},receipt,root)
            entry['preservesExistingAssertions']=False
            with self.assertRaises(ValueError):current.validate_input_transition({entry['path']:'a'},{entry['path']:'b'},receipt,root)
    def test_successor_rejects_production_or_project_change(self):
        for path in ('src/A.cs','tests/Exportaramus.Tests/A.csproj','tests/coverage.runsettings'):
            with self.assertRaises(ValueError):current.validate_input_transition({path:'a'},{path:'b'},{'schemaVersion':'2.0','toolChanges':[],'testChanges':[{'path':path,'kind':'TestStrengthening'}]},Path('.'))
    def test_successor_rejects_unnamed_test_change(self):
        with self.assertRaises(ValueError):current.validate_input_transition({'tests/Exportaramus.Tests/A.cs':'a'},{'tests/Exportaramus.Tests/A.cs':'b'},{'schemaVersion':'2.0','toolChanges':[],'testChanges':[]},Path('.'))
    def test_full_case_preservation_including_duplicates(self):
        old={'provider':{'a':2},'consumerkit12':{'b':1},'inventoryreader':{'c':1}}
        current.validate_baseline_preservation(old,dict(old,provider={'a':2,'d':1}))
        with self.assertRaises(ValueError):current.validate_baseline_preservation(old,dict(old,provider={'a':1,'d':2}))
    def test_missing_successor_suite_rejected(self):
        with self.assertRaises(ValueError):current.validate_baseline_preservation({'provider':{'a':1}},{'provider':{'a':1}})

    def test_prior_receipt_validation_is_required(self):
        import json
        with tempfile.TemporaryDirectory() as d:
            root=Path(d); prior=root/'prior.json'; inventory=root/'inventory.json'; report=root/'report.json'
            report.write_text('{}');inventory.write_text('{}')
            artifact=lambda p:{'path':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
            old={'schemaVersion':'1.0','beforeInventory':artifact(inventory),'measurement':{'report':artifact(report)}}
            prior.write_text(json.dumps(old)); receipt={'priorCampaign':{'root':str(root),'receipt':artifact(prior),'inventory':artifact(inventory)}}
            cases={'provider':['a','a'],'consumerkit12':['b'],'inventoryreader':['c']}
            with patch.object(current.bridge,'inventory',return_value=({'sha256':'measured'},{})),patch.object(current,'current_paths',return_value={}),patch.object(current.report_set,'recorded_run',return_value=(SimpleNamespace(returncode=0,stdout=json.dumps({'cases':cases,'evidence':[]})),[])) as child:
                actual,_=current.validate_prior_campaign(receipt,root,{},report)
                self.assertEqual(actual,cases);self.assertEqual(child.call_count,1)
                child.return_value=(SimpleNamespace(returncode=1,stdout=''),[])
                with self.assertRaises(ValueError):current.validate_prior_campaign(receipt,root,{},report)
                with self.assertRaises(ValueError):current.validate_prior_campaign(receipt,root,{'src/different.cs':'changed'},report)
            old['schemaVersion']='2.0';prior.write_text(json.dumps(old));receipt['priorCampaign']['receipt']=artifact(prior)
            with self.assertRaises(ValueError):current.validate_prior_campaign(receipt,root,{},report)

class DerivedManifestTests(unittest.TestCase):
    def test_only_canonical_manifest_and_unchanged_checker(self):
        import multi_stage_review
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'tools').mkdir();checker=root/'tools/contract-checksums.py'
            checker.write_text("def verify_canonical(root):\n    if (root/'project/ConsumerKit/1.2/SHA256SUMS').read_text() != 'canonical': raise ValueError('stale manifest')\n")
            path='project/ConsumerKit/1.2/SHA256SUMS';manifest=root/path;manifest.parent.mkdir(parents=True);manifest.write_text('canonical')
            sha=current.report_set.digest(checker);before={path:'old','tools/contract-checksums.py':sha};after={path:'new','tools/contract-checksums.py':sha}
            change={'path':path,'kind':'DerivedManifest'};receipt={'schemaVersion':'2.0','toolChanges':[],'testChanges':[change]}
            with patch.object(multi_stage_review,'validate_strengthening') as review:
                current.validate_input_transition(before,after,receipt,root);review.assert_called_once()
            manifest.write_text('stale')
            with self.assertRaises(ValueError):current.validate_input_transition(before,after,receipt,root)
            manifest.write_text('canonical')
            for old,new in [(dict(before,**{'tools/contract-checksums.py':'changed'}),after),(before,dict(after,**{'tools/contract-checksums.py':'changed'})),({path:'old'},{path:'new'}),(before,{'tools/contract-checksums.py':sha})]:
                with self.assertRaises(ValueError):current.validate_input_transition(old,new,receipt,root)
            checker.write_text('# altered')
            with self.assertRaises(ValueError):current.validate_input_transition(before,after,receipt,root)
            for candidate,kind in [('project/ConsumerKit/1.1/SHA256SUMS','DerivedManifest'),(path,'TestStrengthening')]:
                with self.assertRaises(ValueError):current.validate_input_transition(before,after,dict(receipt,testChanges=[{'path':candidate,'kind':kind}]),root)
            with self.assertRaises(ValueError):current.validate_input_transition(before,after,dict(receipt,schemaVersion='1.0'),root)


if __name__=='__main__':unittest.main()
