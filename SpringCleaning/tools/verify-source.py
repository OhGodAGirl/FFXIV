"""Validate the publication boundary and portable build inputs, without changing files."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
GENERATED = {'bin', 'obj', 'TestResults', 'StrykerOutput', '__pycache__', '.tools', '.venv', '.vs', 'development', 'publish', 'artifacts'}
TOP_FILES = {'.editorconfig', '.gitattributes', '.gitignore', 'Directory.Build.props', 'global.json', 'LICENSE', 'README.md'}
TOP_DIRS = {'.config', 'src', 'data', 'project', 'tests', 'tools', 'images'}
SECRET = re.compile(r'gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----')
LOCAL_PATH = re.compile(r'[A-Za-z]:[\\/]+(?:Users|Development|Obsidian|Codex|Steam)[\\/]+' + '|file:' + '/' * 3 + '|obsidian:' + '/' * 2, re.I)
MACHINE = re.compile(r'Intel Core i[3579]-|GeForce[ ]RTX|\d+ GiB RAM|\d+ logical[ ]processors|logical[ ]displays', re.I)

def source_files(root):
    result = subprocess.run(['git', '-C', str(root), 'ls-files', '-z', '--cached', '--others', '--exclude-standard', '--', '.'], capture_output=True)
    if result.returncode == 0:
        return sorted(set(x.decode('utf-8') for x in result.stdout.split(b'\0') if x))
    return sorted(p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file() and not GENERATED.intersection(p.relative_to(root).parts))

def violations(name, content, public):
    parts = PurePosixPath(name).parts
    errors = []
    allowed_dirs = TOP_DIRS if public else TOP_DIRS | {'docs', 'reference'}
    if not parts or '..' in parts or PurePosixPath(name).is_absolute():
        return ['Path escapes the source tree']
    if GENERATED.intersection(parts) or name.endswith(('.dll', '.pdb', '.zip', '.log', '.trx')):
        errors.append('Generated or runtime artifact')
    if (len(parts) == 1 and name not in TOP_FILES) or (len(parts) > 1 and parts[0] not in allowed_dirs):
        errors.append('Outside the publication allowlist')
    if name.startswith('images/') and name != 'images/icon.png':
        errors.append('Only the selected icon belongs in images')
    if SECRET.search(content):
        errors.append('Possible credential or private key')
    if public and (LOCAL_PATH.search(content) or MACHINE.search(content)):
        errors.append('Machine details or a private local link')
    return errors

def verify(root):
    policy = json.loads((root / 'project/source-policy.json').read_text(encoding='utf-8-sig'))
    if policy['audience'] not in {'private', 'public'}:
        raise ValueError('Unknown publication audience')
    public = policy['audience'] == 'public'
    errors = []
    names = source_files(root)
    for name in names:
        path = root / name
        if not path.is_file() or path.is_symlink():
            errors.append(f'{name}: missing file or symbolic link')
            continue
        content = path.read_bytes().decode('utf-8-sig', errors='replace') if path.suffix != '.png' else ''
        errors.extend(f'{name}: {error}' for error in violations(name, content, public))
    required = {'project/SpringCleaning.csproj', 'project/SpringCleaning.slnx', 'project/packages.lock.json', 'project/SpringCleaning.json', 'images/icon.png', 'LICENSE', 'README.md', 'tools/Get-BuildDependencies.ps1', 'tests/run-quality.ps1'}
    errors.extend(f'Missing required source: {name}' for name in sorted(required - set(names)))
    project = ET.parse(root / 'project/SpringCleaning.csproj').getroot()
    if project.findtext('PropertyGroup/Version') != policy['version']:
        errors.append('Project and source policy versions differ')
    manifest = json.loads((root / 'project/SpringCleaning.json').read_text(encoding='utf-8-sig'))
    if manifest['InternalName'] != 'SpringCleaning' or manifest['DalamudApiLevel'] != 15:
        errors.append('Plugin identity or API level differs')
    # Verify all project links rather than relying on developer-specific search paths.
    for name in names:
        if not name.endswith(('.csproj', '.slnx')):
            continue
        path = root / name
        for element in ET.parse(path).iter():
            value = element.get('Include') if element.tag in {'Compile','EmbeddedResource','ProjectReference','Content'} else element.get('Path')
            if not value or '$(' in value:
                continue
            value = value.replace('\\', '/')
            if ';' in value:
                continue
            target = (path.parent / value).resolve()
            if not target.is_relative_to(root.resolve()):
                errors.append(f'{name}: external source dependency {value}')
            elif '*' not in value and not target.is_file():
                errors.append(f'{name}: missing source dependency {value}')
    # The private full-kit archive is independently pinned and must match compiled copies.
    kit = root / 'reference/ConsumerKit/1.2'
    if not public:
        pin_script = (root / 'tests/test-private-reference.ps1')
        if not pin_script.exists():
            errors.append('Missing private archive verification')
        provenance = json.loads((root / 'project/Contracts/1.2/provenance.json').read_text())
        for destination, entry in provenance['files'].items():
            rel = destination.split('/1.2/',1)[1]
            if destination.startswith('data/'):
                original = rel
            elif destination.startswith('tests/'):
                original = rel
            else:
                original = Path(rel).name
            if hashlib.sha256((kit / original).read_bytes()).hexdigest() != entry['sha256']:
                errors.append(f'Private reference differs: {original}')
    if errors:
        raise ValueError('\n'.join(errors))
    print(f'{policy["audience"].title()} source boundary verified: {len(names)} files.')
    return len(names)

if __name__ == '__main__':
    try:
        verify(ROOT)
    except (ValueError, KeyError, OSError, ET.ParseError) as error:
        print(error, file=sys.stderr)
        sys.exit(1)
