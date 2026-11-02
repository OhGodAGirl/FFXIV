# Third-party notices

## Exportaramus CollectionData Consumer Kit 1.1

EZ Delivery includes source-distributed Consumer Kit 1.1 from Exportaramus. Its eight C# files are unmodified in `src/Contracts/1.1`; schemas and resources are in `data/Contracts/1.1`; fixtures are in `tests/fixtures/Contracts/1.1`. The consumer assembly is `Exportaramus.ConsumerKit.V1_1.dll`; the Dalamud gate binding is compiled into `EZDelivery.dll`.

- Project: [Exportaramus](https://github.com/OhGodAGirl/FFXIV/tree/main/Exportaramus).
- Copyright: Copyright (c) 2026 OhGodAGirl.
- License: MIT. Full copyright and permission notice: [LICENSE](LICENSE).
- Public provider contracts: [CollectionData 1.1](https://github.com/OhGodAGirl/FFXIV/tree/main/Exportaramus/src/Contracts/1.1).
- Original complete 35-file pin manifest: `project/Contracts/1.1/UPSTREAM-SHA256SUMS`.
- Relocation and retained-file hashes: `project/Contracts/1.1/provenance.json`.

The release copy omits the upstream kit README and unused Runtime project. The consumer project is adapted only for relocated source/resource paths with unchanged resource logical names. The development pin remains complete and unmodified. Required contract, client, binding, schema, resource and fixture bytes remain identical to the original pin. The public provider tree does not contain the complete consumer kit.