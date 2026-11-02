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

if __name__=='__main__':unittest.main()
