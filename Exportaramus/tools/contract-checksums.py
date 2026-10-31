"""Generate or verify the published ConsumerKit 1.2 source manifest."""
from pathlib import Path
import argparse
import hashlib

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = Path('project/ConsumerKit/1.2/SHA256SUMS')


def canonical_files(root):
    """Inventory the shipped source kit, independently of any existing manifest."""
    root = root.resolve()
    files = set()
    for pattern in (
        'src/Contracts/1.2/*.cs', 'src/Contracts/1.2/Consumer/*.cs',
        'data/Contracts/1.2/schemas/*.json', 'data/Contracts/1.2/resources/*.json',
        'project/ConsumerKit/1.2/*.csproj', 'project/ConsumerKit/1.2/packages.lock.json',
        'tests/Exportaramus.ConsumerKit.Tests/*.cs', 'tests/Exportaramus.ConsumerKit.Tests/*.csproj',
        'tests/Exportaramus.ConsumerKit.Tests/packages.lock.json',
        'tests/Exportaramus.ConsumerKit.Tests/run-tests.ps1',
        'tests/Exportaramus.ConsumerKit.Tests/fixtures/*/*.json',
    ):
        matches = list(root.glob(pattern))
        if not matches:
            raise ValueError(f'Missing canonical source group: {pattern}')
        files.update(matches)
    for name in ('CollectionExample.csproj', 'Plugin.Dalamud.cs', 'InventoryItemReader.cs',
                 'packages.lock.json', 'README.md', '.gitignore'):
        files.add(root / 'examples/CollectionExample' / name)
    for path in files:
        if not path.is_file() or not path.resolve().is_relative_to(root):
            raise ValueError(f'Not an ordinary in-tree source file: {path}')
        if any(parent.is_symlink() for parent in (path, *path.parents) if parent.is_relative_to(root)):
            raise ValueError(f'Linked canonical source is not allowed: {path}')
    return sorted(files, key=lambda path: (path.relative_to(root).as_posix().casefold(),
                                           path.relative_to(root).as_posix()))


def canonical_manifest(root):
    files = canonical_files(root)
    return ''.join(hashlib.sha256(path.read_bytes()).hexdigest() + '  ' +
                   path.relative_to(root.resolve()).as_posix() + '\n' for path in files).encode('utf-8'), len(files)


def verify_canonical(root):
    expected, count = canonical_manifest(root)
    target = root / MANIFEST
    if not target.is_file() or target.read_bytes() != expected:
        raise ValueError('Canonical ConsumerKit SHA256SUMS differs from the complete shipped source inventory. Verification did not modify it.')
    return count

def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verify', action='store_true', help='Check complete exact bytes without rewriting SHA256SUMS.')
    parser.add_argument('--root', type=Path, default=ROOT, help="Exportaramus source root; defaults to this script's repository.")
    parser.add_argument('--kit-version', choices=('1.2', 'all'), default='all',
                        help='Published source kit to process; both choices select ConsumerKit 1.2.')
    options = parser.parse_args(arguments)
    root = options.root.resolve()
    if options.verify:
        count = verify_canonical(root)
        print(f'Canonical ConsumerKit 1.2: {count} complete files verified; manifest unchanged.')
    else:
        expected, count = canonical_manifest(root)
        (root / MANIFEST).write_bytes(expected)
        print(f'Canonical ConsumerKit 1.2: {count} complete files hashed.')


if __name__ == '__main__':
    main()
