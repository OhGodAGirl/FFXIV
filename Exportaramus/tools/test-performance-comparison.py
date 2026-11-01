import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('comparison', Path(__file__).with_name('performance-comparison.py'))
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


class ComparisonTests(unittest.TestCase):
    def test_stable_slowdown_is_flagged(self):
        self.assertEqual('Possible regression', comparison.compare([1.] * 7, [1.5] * 7)['assessment'])

    def test_machine_speed_does_not_change_assessment(self):
        for scale in [.001, 10, 1000]:
            self.assertEqual('No consistent regression detected', comparison.compare([scale] * 7, [scale * 1.1] * 7)['assessment'])

    def test_single_outlier_and_noisy_runs_are_not_regressions(self):
        for values in [[1.] * 6 + [10.], [.5, 2., .6, 2.1, .7, 2.2, 1.]]:
            self.assertEqual('No consistent regression detected', comparison.compare([1.] * 7, values)['assessment'])

    def test_incomplete_or_invalid_samples_fail(self):
        for values in [[1.] * 6, [0.] * 7, [float('nan')] * 7, [float('inf')] * 7]:
            with self.assertRaises(ValueError):
                comparison.compare([1.] * 7, values)

    def test_receipt_rejects_missing_duplicate_failed_and_instrumented_results(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'performance.trx'
            def receipt(mode):
                root = ET.Element('TestRun')
                for index in range(6):
                    result = ET.SubElement(root, 'UnitTestResult', outcome='Failed' if mode == 'failed' and index == 0 else 'Passed')
                    output = ET.SubElement(result, 'StdOut')
                    if index < 5:
                        output.text = 'PERFORMANCE ' + json.dumps({'metric': sorted(comparison.METRICS)[index], 'milliseconds': 1.})
                if mode == 'missing':
                    root.remove(root[0])
                if mode == 'duplicate':
                    root[5][0].text = root[0][0].text
                if mode == 'instrumented':
                    ET.SubElement(root, 'CollectorDataEntries')
                ET.ElementTree(root).write(path)
            receipt('valid')
            self.assertEqual(comparison.METRICS, comparison.read_metrics(path).keys())
            for mode in ['missing', 'duplicate', 'failed', 'instrumented']:
                receipt(mode)
                with self.assertRaises(ValueError):
                    comparison.read_metrics(path)


if __name__ == '__main__':
    unittest.main()
