"""Current source-inventory boundary for private qualification; historical inputs stay explicit."""
from pathlib import Path
import hashlib
import os

ROOTS = ('src', 'tests', 'data', 'images', 'tools', 'project', 'examples', 'docs', '.config', '.github')
EXCLUDED_DIRECTORIES = {'bin', 'obj', 'testresults', '__pycache__', '.git', '.vs', 'node_modules', 'development'}
EXCLUDED_EXTENSIONS = {'.tmp', '.bak', '.pyc', '.pyo', '.user', '.suo', '.trx', '.binlog', '.cache'}


def linked(path):
    return path.is_symlink() or getattr(path, 'is_junction', lambda: False)()


def current_paths(root):
    root = root.resolve()
    paths = set()
    def accept(path, top=False):
        if (not linked(path) and path.is_file() and path.name.casefold() != 'agents.md'
                and path.suffix.casefold() not in EXCLUDED_EXTENSIONS
                and (not top or path.suffix.casefold() not in {'.dll', '.pdb', '.zip', '.nupkg'})):
            paths.add(path)
    for path in root.iterdir():
        accept(path, top=True)
    for name in ROOTS:
        directory = root / name
        if not directory.is_dir() or linked(directory):
            continue
        for parent, directories, files in os.walk(directory):
            directories[:] = [name for name in directories if name.casefold() not in EXCLUDED_DIRECTORIES
                              and not linked(Path(parent) / name)]
            for name in files:
                accept(Path(parent) / name)
    return {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sorted(paths)}
