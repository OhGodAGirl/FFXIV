"""Check tracked Exportaramus source; report filenames, never sensitive line contents."""
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys

DRIVE = re.compile(r'(?<![A-Za-z])[A-Za-z]:[\\/]')
PRIVATE = re.compile(r'obsidian://|[A-Za-z]:[\\/]+obsidian\b|file://', re.I)
UNIX_HOME = re.compile(r'(?:/Users/|/home/)[A-Za-z0-9_.-]+/')
GENERATED = {'bin', 'obj', 'TestResults', '__pycache__', '.venv', '.tools', '.vs', 'StrykerOutput'}
BINARY = {'.dll', '.exe', '.pdb', '.zip', '.trx', '.pyc'}
PATH_FIXTURES = {
    'tools/contract-fixtures.py', 'tools/test-reference-inputs.py',
    'tools/test-mutation-orchestration.py',
    'tests/fixtures/Diagnostics/1.1/fixtures/invalid/diagnostics-invalid-identity-or-path.json',
    'tests/Exportaramus.Tests/Diagnostics/DiagnosticTelemetryTests.cs',
    'tests/Exportaramus.Tests/Diagnostics/DiagnosticsTests.cs',
    'docs/Consumer Integration Guide.md',
}

def check_file(relative, data):
    path = PurePosixPath(relative)
    issues = []
    if GENERATED.intersection(path.parts) or path.suffix.lower() in BINARY:
        issues.append('generated output or binary archive')
    if path.parts[0] in {'development', 'snapshots', 'state'} or path.name in {'current-snapshot.json', 'current-snapshot-v1.2.json'}:
        issues.append('live snapshot or local runtime data')
    try:
        text = data.decode('utf-8-sig')
    except UnicodeDecodeError:
        return issues
    for number, line in enumerate(text.splitlines(), 1):
        if PRIVATE.search(line) or (relative not in PATH_FIXTURES and (DRIVE.search(line) or UNIX_HOME.search(line))):
            issues.append(f'line {number}: private link or machine-specific path')
    return issues

def check_repository(root):
    names = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z', '--', 'Exportaramus/']).decode().split('\0')
    issues = []
    for name in filter(None, names):
        path = root / name
        if path.is_symlink():
            issues.append(f'{name}: symbolic link is not allowed')
            continue
        for issue in check_file(name.removeprefix('Exportaramus/'), path.read_bytes()):
            issues.append(f'{name}: {issue}')
    if not any(names):
        issues.append('No tracked Exportaramus files found')
    return issues

if __name__ == '__main__':
    errors = check_repository(Path(__file__).resolve().parents[2])
    print('\n'.join(errors) if errors else 'Publication hygiene passed.')
    sys.exit(bool(errors))
