"""Compare uninstrumented baseline and proposed code on one machine."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess
import xml.etree.ElementTree as ET

METRICS = {'cached-getters-128', 'cached-getters-4194304', 'presentation', 'report-gate', 'idle'}
HARNESS = Path('tests/Exportaramus.Tests/Performance/OfflinePerformanceTests.cs')
PROJECT = 'tests/Exportaramus.Tests/Exportaramus.Tests.csproj'


def read_metrics(path):
    root = ET.parse(path).getroot()
    results = root.findall('.//{*}UnitTestResult')
    if len(results) != 6 or any(r.get('outcome') != 'Passed' for r in results):
        raise ValueError('All six benchmark correctness/allocation cases must pass.')
    if root.findall('.//{*}CollectorDataEntries'):
        raise ValueError('Comparison must run without a coverage collector.')
    metrics = {}
    for result in results:
        for output in result.findall('.//{*}StdOut'):
            for line in (output.text or '').splitlines():
                if not line.startswith('PERFORMANCE '):
                    continue
                value = json.loads(line[len('PERFORMANCE '):])
                key, number = value['metric'], value['milliseconds']
                if key in metrics or not isinstance(number, (int, float)) or isinstance(number, bool) or not math.isfinite(number) or number <= 0:
                    raise ValueError('Duplicate or invalid timing measurement.')
                metrics[key] = number
    if metrics.keys() != METRICS:
        raise ValueError('Missing or unexpected benchmark metrics.')
    return metrics


def compare(baseline, proposed):
    if len(baseline) != len(proposed) or len(baseline) < 7:
        raise ValueError('At least seven paired runs are required.')
    if any(not math.isfinite(x) or x <= 0 for x in baseline + proposed):
        raise ValueError('Measurements must be finite and positive.')
    ratios = [p / b for b, p in zip(baseline, proposed)]
    ratio = statistics.median(ratios)
    noise = 1.4826 * statistics.median(abs(x - ratio) for x in ratios)
    allowance = max(.20, 3 * noise)
    consistent = sum(x > 1 + allowance for x in ratios) >= math.ceil(.8 * len(ratios))
    return {'baselineMedianMs': statistics.median(baseline), 'proposedMedianMs': statistics.median(proposed),
            'medianRatio': ratio, 'relativeNoiseAllowance': allowance, 'pairedRatios': ratios,
            'assessment': 'Possible regression' if ratio > 1 + allowance and consistent else 'No consistent regression detected'}


def run(command, cwd, log):
    with log.open('w', encoding='utf-8') as output:
        subprocess.run(command, cwd=cwd, stdout=output, stderr=subprocess.STDOUT, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True, help='Disposable baseline Exportaramus checkout')
    parser.add_argument('--proposed', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--rounds', type=int, default=7)
    args = parser.parse_args()
    if args.rounds < 7:
        parser.error('Use at least seven paired rounds.')
    roots = {'baseline': args.baseline.resolve(), 'proposed': args.proposed.resolve()}
    if roots['baseline'] == roots['proposed']:
        parser.error('Baseline and proposed checkouts must be distinct.')
    output = args.output.resolve()
    if any(output == root or root in output.parents for root in roots.values()):
        parser.error('Keep results outside the source checkouts.')
    output.mkdir(parents=True, exist_ok=False)
    harness = (roots['proposed'] / HARNESS).read_bytes()
    original = (roots['baseline'] / HARNESS).read_bytes()
    records = []
    report = {'status': 'Incomplete', 'rounds': args.rounds, 'host': platform.platform(),
              'processors': os.cpu_count(), 'harnessSha256': hashlib.sha256(harness).hexdigest(), 'runs': records}
    for label, root in roots.items():
        report[label + 'Commit'] = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    try:
        # Use identical benchmark code for both production versions.
        (roots['baseline'] / HARNESS).write_bytes(harness)
        for label, root in roots.items():
            run([args.dotnet, 'restore', PROJECT, '--locked-mode'], root, output / (label + '-restore.log'))
            run([args.dotnet, 'build', PROJECT, '-c', 'Release', '--no-restore'], root, output / (label + '-build.log'))
        for index in range(args.rounds):
            for label in (['baseline', 'proposed'] if index % 2 == 0 else ['proposed', 'baseline']):
                destination = output / f'{index:02}-{label}'
                run([args.dotnet, 'test', PROJECT, '-c', 'Release', '--no-build', '--no-restore',
                     '--filter', 'FullyQualifiedName~Exportaramus.Tests.Performance.OfflinePerformanceTests',
                     '--results-directory', str(destination), '--logger', 'trx;LogFileName=performance.trx'],
                    roots[label], output / f'{index:02}-{label}.log')
                trx = destination / 'performance.trx'
                records.append({'round': index, 'version': label, 'metrics': read_metrics(trx),
                                'trxSha256': hashlib.sha256(trx.read_bytes()).hexdigest()})
        report['comparisons'] = {key: compare(
            [r['metrics'][key] for r in records if r['version'] == 'baseline'],
            [r['metrics'][key] for r in records if r['version'] == 'proposed']) for key in sorted(METRICS)}
        report['status'] = 'Complete'
        lines = ['## Performance Comparison', '', 'Timing is advisory; correctness and allocation failures fail this job.',
                 'Seven or more alternating pairs; allowance is the greater of 20% or three robust deviations of paired ratios.',
                 'A flag requires at least 80% of pairs above that allowance. No flag does not prove equivalence.', '',
                 '| Measurement | Baseline ms | Proposed ms | Ratio | Assessment |', '|---|---:|---:|---:|---|']
        for key, value in report['comparisons'].items():
            lines.append(f"| {key} | {value['baselineMedianMs']:.6f} | {value['proposedMedianMs']:.6f} | {value['medianRatio']:.2f} | {value['assessment']} |")
            if value['assessment'] == 'Possible regression':
                print(f'::warning::Performance comparison flagged {key}; review the paired measurements.')
        (output / 'summary.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    finally:
        (roots['baseline'] / HARNESS).write_bytes(original)
        (output / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
