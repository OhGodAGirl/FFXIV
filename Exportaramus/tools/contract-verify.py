"""Verify published schemas, fixtures, catalogs and source integrity offline."""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[1]


def load_tool(name):
    path = Path(__file__).with_name(name + '.py')
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.ROOT = ROOT
    return module


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def current_schema_paths():
    """One canonical file per schema ID; identical embedded copies are checked elsewhere."""
    by_id = {}
    for directory in ('data/Contracts', 'data/FFXIVCollect', 'data/Diagnostics'):
        for path in sorted((ROOT / directory).rglob('*.schema.json')):
            schema = json.loads(path.read_bytes())
            identifier = schema['$id']
            if identifier in by_id:
                require(json.loads(by_id[identifier].read_bytes()) == schema,
                        f'Conflicting schema ID: {identifier}')
            else:
                by_id[identifier] = path
    return sorted(by_id.values())


def fixture_schema(path, schemas, suffix):
    """Choose the contract version from canonical fixture paths, without first-match fallback."""
    parts = path.parts
    version = '1.0'
    for marker in ('Contracts', 'ConsumerKit'):
        if marker in parts:
            index = parts.index(marker)
            if index + 1 < len(parts):
                version = parts[index + 1]
                break
    if path.name.startswith('ffxiv-collect-') and version in ('1.1', '1.2'):
        resource = ROOT / 'data/Contracts' / version / 'resources/ffxiv-collect-import.schema.json'
        require(resource.is_file(), f'Missing import schema for {version}.')
        return json.loads(resource.read_bytes())
    candidates = [schema for key, schema in schemas.items() if key.endswith(suffix)]
    if len(candidates) == 1:
        return candidates[0]
    matching = [schema for schema in candidates if f'/{version}/' in schema['$id']]
    require(len(matching) == 1, f'Ambiguous or missing fixture schema for {path}.')
    return matching[0]


def verify_catalog(directory, registry, schemas, extra_keys=()):
    recognized = json.loads((directory / 'recognized-ids.json').read_bytes())
    maps = json.loads((directory / 'identifier-mappings.json').read_bytes())
    derived = json.loads((directory / 'derived-inputs.json').read_bytes())
    keys = [entry['key'] for entry in maps['entries']]
    require(len(keys) == len(set(keys)), f'Duplicate mapping keys: {directory}')
    require(keys + list(extra_keys) == [entry['key'] for entry in recognized['catalogs']],
            f'Mapping/catalog order differs: {directory}')
    for mapping in maps['entries']:
        require(mapping['key'] == mapping['inputKey'] and mapping['transform'] in
                ('Identity', 'DerivedIdentity', 'CabinetRowPlusOne'), f'Invalid mapping: {directory}')
        for upstream in mapping['upstreamFiles']:
            portable = upstream.replace('\\', '/')
            require(not portable.startswith('/') and ':' not in portable and '..' not in portable.split('/'),
                    f'Non-relative mapping path: {upstream}')
        for entry in mapping['exceptions']:
            ids = entry['ids']
            require(entry['operation'] in ('Include', 'Exclude') and ids == sorted(set(ids)) and
                    all(isinstance(value, int) and not isinstance(value, bool) and value > 0 for value in ids),
                    f'Invalid mapping exception: {directory}')
    for catalog in recognized['catalogs']:
        ids = catalog['ids']
        require(bool(ids) and ids == sorted(set(ids)) and len(ids) == catalog['count'] and
                catalog['minimum'] == min(ids) and catalog['maximum'] == max(ids) and
                all(isinstance(value, int) and not isinstance(value, bool) and value > 0 for value in ids),
                f'Invalid recognized-ID catalog: {catalog["key"]}')
        # The Beasts catalog uses a JSON ID array; older catalogs use comma-separated IDs.
        encoded = json.dumps(ids, separators=(',', ':')) if catalog['key'] in extra_keys else ','.join(map(str, ids))
        require(hashlib.sha256(encoded.encode()).hexdigest() == catalog['idsSha256'],
                f'Catalog checksum differs: {catalog["key"]}')
    candidates = [schema for key, schema in schemas.items() if key.endswith('/derived-inputs.schema.json')]
    require(len(candidates) == 1, 'Missing or ambiguous derived-input schema.')
    Draft202012Validator(candidates[0], registry=registry, format_checker=FormatChecker()).validate(derived)
    return keys


def verify_canonical():
    consumer = load_tool('consumer-kit-verify')
    checksums = load_tool('contract-checksums')
    checksum_count = checksums.verify_canonical(ROOT)
    schemas, registry = consumer.load_schemas(include_diagnostics=True)
    kits = [consumer.verify_canonical_version(version, schemas, registry) for version in consumer.VERSIONS]
    contract = json.loads((ROOT / 'data/ffxiv-collect-contract.json').read_bytes())
    legacy = ROOT / 'data/FFXIVCollect/1.0'
    for field, name in [('identifierMappingsSha256', 'identifier-mappings.json'),
                        ('recognizedIdsSha256', 'recognized-ids.json'), ('derivedInputsSha256', 'derived-inputs.json')]:
        require(contract['artifacts'][field] == digest(legacy / name), f'Export artifact checksum differs: {name}')
    keys = verify_catalog(legacy, registry, schemas)
    require(contract['orderedKeys'] == keys, 'Export contract key order differs from reviewed resources.')
    for version in ('1.1', '1.2'):
        verify_catalog(ROOT / 'data/Contracts' / version / 'resources', registry, schemas,
                       ('beasts',) if version == '1.2' else ())
    beasts = json.loads((ROOT / 'data/FFXIVCollect/1.2/beasts-export-contract.json').read_bytes())
    recognized = json.loads((ROOT / 'data/Contracts/1.2/resources/recognized-ids.json').read_bytes())
    catalog = next(entry for entry in recognized['catalogs'] if entry['key'] == 'beasts')
    require(beasts['ids'] == catalog['ids'] and beasts['idsSha256'] ==
            hashlib.sha256(','.join(map(str, beasts['ids'])).encode()).hexdigest(),
            'Beasts contract differs from the current recognized-ID catalog.')
    extra_counts = {'validSchemaFixtures': 0, 'schemaRejectedFixtures': 0, 'requiresCSharpSemanticValidation': []}
    for group in ('Diagnostics', 'FFXIVCollect'):
        paths = sorted((ROOT / 'tests/fixtures' / group).rglob('fixtures/*/*.json'))
        require(bool(paths), f'Missing canonical fixtures: {group}')
        for path in paths:
            suffix = '/exportaramus-diagnostics-v1.1.schema.json' if group == 'Diagnostics' else '/import.schema.json'
            schema = fixture_schema(path, schemas, suffix)
            errors = list(Draft202012Validator(schema, registry=registry,
                format_checker=FormatChecker()).iter_errors(json.loads(path.read_bytes())))
            if path.parent.name == 'valid':
                require(not errors, f'Valid fixture rejected: {path}: ' + '; '.join(error.message for error in errors))
                extra_counts['validSchemaFixtures'] += 1
            elif errors:
                extra_counts['schemaRejectedFixtures'] += 1
            else:
                extra_counts['requiresCSharpSemanticValidation'].append(path.relative_to(ROOT).as_posix())
    return {'status': 'Passed', 'scope': 'Canonical shipped schema, fixture, catalog and source-integrity checks',
            'schemas': len(schemas), 'localReferences': 'AllResolvedOffline', 'formats': 'Asserted',
            'canonicalChecksumFiles': checksum_count, 'kits': kits, 'additionalFixtures': extra_counts,
            'notEstablished': ['C# semantic rejection', 'native or live behavior', 'fresh local game-data verification']}


def main(arguments=None):
    global ROOT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT, help='Exportaramus source root.')
    parser.add_argument('--output', type=Path, help='Optional explicit JSON receipt destination; default is stdout only.')
    options = parser.parse_args(arguments)
    ROOT = options.root.resolve()
    result = verify_canonical()
    text = json.dumps(result, indent=2) + '\n'
    if options.output:
        options.output.parent.mkdir(parents=True, exist_ok=True)
        options.output.write_text(text, encoding='utf-8')
    print(text, end='')


if __name__ == '__main__':
    main()
