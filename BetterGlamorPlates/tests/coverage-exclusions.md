# Coverage scope

The manifest classifies every production file. The expanded gate covers **54 managed files**, each requiring 100% lines and branches. Mutation testing targets the same inventory. These percentages do not describe the whole running plugin.

## Included behavior

- Plugin command dispatch, cancellation, character changes, refresh ownership, preview scheduling, editor lifecycle, Examine saving, theme polling and catalog publication.
- Real configuration mapping and collection-consumer lifecycle, using controlled persistence/transport callbacks.
- Plate planning, native-operation sequencing, partial failures, readback, dye preparation/allocation, pending-allocation rejection and channel capability flags, capture and preview decisions.
- Drawer targets, searches, icon filtering, action binding, text buffers, bounded texture-reference caching and font ownership.
- Existing library, controller, diagnostics, collection, layout and motion decisions.

Each extracted component is called by production code. Tests assert effects, outcomes and forbidden operations using Arrange�Act�Assert structure; source-text architecture checks are only additional backstops.

## Excluded runtime boundaries

`Plugin.cs` composes Dalamud services, registers callbacks and captures native fields. `ConfigurationStore.Dalamud.cs` binds the tested persistence delegates. Native adapters still dereference FFXIV structures, map Lumina fields and invoke game APIs. ImGui files still perform actual rendering, focus and hit testing. Offline fakes cannot establish current-patch ABI correctness, game acceptance, visual fidelity or live IPC behavior.

These adapters contain executable native guards and mappings. Their exclusion does **not** mean they are branch-free or fully tested. See `unpublished-historical-evidence`, `unpublished-historical-evidence` and `unpublished-historical-evidence` for the component map and required live evidence.

## Data and vendor boundaries

Identity constants, command/view/transport records and the configuration DTO are classified separately; their interpretation is exercised by included components. The eight pinned Consumer Kit 1.2 source files remain vendor-owned, protected by exact-byte provenance and consumer fixture/integration tests. Seven compile into the separate consumer assembly; the supplied Dalamud binding is retained for provenance while the plugin uses its own binding. Vendor tests, examples and the historical 1.1 kit are not plugin production inputs. None are counted as plugin-owned mutation coverage.

