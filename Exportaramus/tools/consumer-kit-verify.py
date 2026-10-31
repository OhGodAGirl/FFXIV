"""Read-only, offline checks for kit integrity, canonical parity, and JSON schemas.

Invalid fixtures that pass JSON Schema still require the C# semantic validator.
This command does not claim client lifecycle, native, or in-game verification.
"""
import argparse
import json
from pathlib import Path
from types import ModuleType
from urllib.parse import urljoin

from jsonschema import Draft202012Validator, FormatChecker
from referencing import Registry, Resource
from referencing.exceptions import NoSuchResource

ROOT = Path(__file__).resolve().parents[1]
VERSIONS = ('1.0', '1.1', '1.2')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    return json.loads(path.read_bytes())


def no_network(uri):
    raise NoSuchResource(ref=uri)


def load_checksum_tool():
    path = ROOT / 'tools/contract-checksums.py'
    module = ModuleType('contract_checksums')
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


def load_schemas(include_diagnostics=False):
    schemas = {}
    directories = [ROOT / 'data/Contracts', ROOT / 'data/FFXIVCollect']
    if include_diagnostics:
        directories.append(ROOT / 'data/Diagnostics')
    for directory in directories:
        require(directory.is_dir(), f'Missing canonical schema directory: {directory}')
        for path in sorted(directory.rglob('*.schema.json')):
            schema = read_json(path)
            Draft202012Validator.check_schema(schema)
            identifier = schema['$id']
            if identifier in schemas:
                require(schemas[identifier] == schema, f'Conflicting schema ID: {identifier}')
            schemas[identifier] = schema
    require(bool(schemas), 'No canonical schemas found.')
    require(len(schemas) == (22 if include_diagnostics else 21), 'Canonical schema inventory is incomplete or changed; review supported schemas.')
    registry = Registry(retrieve=no_network).with_resources(
        (key, Resource.from_contents(value)) for key, value in schemas.items())

    def resolve(node, base):
        if isinstance(node, dict):
            base = urljoin(base, node.get('$id', ''))
            if '$ref' in node:
                registry.resolver(base).lookup(node['$ref'])
            for value in node.values():
                resolve(value, base)
        elif isinstance(node, list):
            for value in node:
                resolve(value, base)

    for key, schema in schemas.items():
        resolve(schema, key)
    return schemas, registry


def fixture_schema(name, kit):
    if name.startswith('snapshot-changed-'):
        return kit / 'schemas/snapshot-changed.schema.json'
    if name.startswith('snapshot-withdrawn-'):
        return kit / 'schemas/snapshot-withdrawn.schema.json'
    if name.startswith('capabilities-'):
        return kit / 'schemas/capabilities.schema.json'
    if name.startswith(('compendium-', 'expanded-')):
        return kit / 'schemas/exportaramus-snapshot.schema.json'
    if name.startswith(('compact-', 'snapshot-')):
        return kit / 'schemas/collection-data.schema.json'
    if name.startswith('ffxiv-collect-'):
        local = kit / 'resources/ffxiv-collect-import.schema.json'
        return local if local.is_file() else ROOT / 'data/FFXIVCollect/1.0/ffxiv-collect-import.schema.json'
    raise ValueError(f'Fixture has no assigned schema: {name}')


def verify_canonical_version(version, schemas, registry):
    """Validate the published contract source, schemas and fixtures."""
    kit = ROOT / 'data/Contracts' / version
    fixtures = ROOT / 'tests/fixtures/Contracts' / version / 'fixtures'
    require((ROOT / 'src/Contracts' / version).is_dir(), f'Missing contract source for {version}.')
    source = sorted((ROOT / 'src/Contracts' / version).glob('*.cs'))
    require(len(source) >= 6, f'Incomplete contract source for {version}.')
    counts = {'validSchemaFixtures': 0, 'schemaRejectedFixtures': 0}
    semantic_required = []
    for category in ('valid', 'invalid'):
        paths = sorted((fixtures / category).glob('*.json'))
        require(bool(paths), f'Missing {category} fixtures for {version}.')
        for path in paths:
            schema = read_json(fixture_schema(path.name, kit))
            require(schemas.get(schema['$id']) == schema, f'Fixture schema is not canonical: {path}')
            errors = list(Draft202012Validator(schema, registry=registry,
                format_checker=FormatChecker()).iter_errors(read_json(path)))
            if category == 'valid':
                require(not errors, f'Valid fixture rejected: {path}: ' + '; '.join(e.message for e in errors))
                counts['validSchemaFixtures'] += 1
            elif errors:
                counts['schemaRejectedFixtures'] += 1
            else:
                semantic_required.append(path.relative_to(ROOT).as_posix())
    vectors = sorted(fixtures.glob('*.json'))
    for path in vectors:
        read_json(path)
    if version == '1.2':
        merged = ROOT / 'tests/Exportaramus.ConsumerKit.Tests/fixtures'
        expected = {p.relative_to(fixtures).as_posix().replace('compendium-', 'expanded-'): p
                    for category in ('valid', 'invalid') for p in (fixtures / category).glob('*.json')}
        actual = {p.relative_to(merged).as_posix(): p for p in merged.glob('*/*.json')}
        require(set(actual) == set(expected), 'Standalone ConsumerKit fixture inventory differs from canonical 1.2 fixtures.')
        for key, path in expected.items():
            require(path.read_bytes() == actual[key].read_bytes(), f'Standalone fixture differs: {key}')
    return {'version': version, 'sharedSourceFiles': len(source), **counts,
            'requiresCSharpSemanticValidation': semantic_required, 'vectorFilesParsedOnly': len(vectors)}


def main(arguments=None):
    global ROOT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT, help='Exportaramus source root.')
    parser.add_argument('--kit-version', choices=(*VERSIONS, 'all'), default='all')
    options = parser.parse_args(arguments)
    ROOT = options.root.resolve()
    versions = VERSIONS if options.kit_version == 'all' else (options.kit_version,)
    checksum_tool = load_checksum_tool()
    schemas, registry = load_schemas()
    checksum_files = checksum_tool.verify_canonical(ROOT)
    results = [verify_canonical_version(version, schemas, registry) for version in versions]
    print(json.dumps({
        'status': 'Passed',
        'scope': 'Integrity, canonical byte parity, offline schema validity and fixture shape checks only',
        'canonicalSchemaCount': len(schemas),
        'canonicalChecksumFiles': checksum_files,
        'kits': results,
        'notEstablished': ['C# semantic rejection', 'client lifecycle', 'Dalamud loading', 'live game behavior'],
    }, indent=2))


if __name__ == '__main__':
    main()
