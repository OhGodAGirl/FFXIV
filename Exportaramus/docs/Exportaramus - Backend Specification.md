# Backend Specification

## 1. Purpose

Exportaramus is a Dalamud plugin for Final Fantasy XIV. It reads a character's collections and stored items, saves the observations locally, and supplies them to other plugins and applications.

This document defines the behavior required for an independent implementation. It assumes basic C# knowledge but no previous FFXIV or Dalamud experience. The [Frontend Specification](<Exportaramus - Frontend Specification.md>) defines appearance and layout. The [Consumer Integration Guide](<Consumer Integration Guide.md>) explains how another plugin reads the published data.

The requirements describe the current product. Release history, test-run receipts, and abandoned designs belong outside this specification.

### 1.1 Platform and Data Terms

| Term | Meaning |
| --- | --- |
| Dalamud | The plugin framework loaded into FFXIV. It supplies plugin services, events, commands, and IPC. |
| Lumina | The game-data library used by Dalamud to read installed definition tables. Its typed sheet rows are C# structs such as `Lumina.Excel.Sheets.Mount`. |
| FFXIVClientStructs | C# definitions of native game structures and calls. These definitions must match the reviewed game build. |
| Framework Thread | The game-update thread on which native game state may be inspected. A short update handler runs repeatedly while the game is running. |
| Native State | Game-owned memory exposed through supported Dalamud services or FFXIVClientStructs. Its addresses and contents can change as the game runs. |
| Game Sheet | A table of definitions, such as Item, Mount, or Quest. A row ID identifies a definition, not character ownership. |
| Content ID | The game's unsigned 64-bit character identifier. Use it to separate characters; a display name is not a reliable key. |
| Source | A named group of information tracked with its own health and observation time, such as mount unlocks or carried items. |
| Source Key | The exact string identifying a source, such as `mounts` or `inventory`. The key names the data, not the reader code. |
| Reader | An Exportaramus component that queries game state and produces observations for one or more sources. |
| Observation | Information accepted from a successful read for a particular character and source at a recorded time. A read attempt can fail without producing an observation. |
| Publication | The complete managed result accepted for consumers after preparation and validation. Publishing can combine new observations with retained, restored, reported, or derived information. |
| Provenance | The origin of information: observed from the game, restored from disk, reported by a consumer, derived by calculation, or absent. It is separate from freshness and completeness. |
| Consumer | Another plugin or application that reads Exportaramus output. |
| IPC | Inter-plugin communication: named functions and notifications exposed through Dalamud. |
| Snapshot | A group of observations and related metadata for one character, prepared before publication or saving. Individual sources may have different observation times. |
| Mount / Minion | An unlocked travel creature or vehicle / a collectible companion that follows the character. Track their permanent unlocks separately from any summoning items. |
| NPC | A non-player character controlled by the game. |
| Retainer | An NPC assistant employed by a character, with its own stored items. A Summoning Bell opens the retainer interface. |
| Armoire / Glamour Dresser | Game storage systems for eligible equipment and appearance items. They provide membership information, not reliable copy counts. |
| Armoury Chest / Saddlebag | Equipment storage / additional item storage associated with the character's chocobo companion. The API uses the spelling `ArmoryChest`. |
| Base Item ID / HQ | An Item sheet row ID without a quality-encoding offset / high quality. Inventory keeps HQ and normal-quality quantities separate. |
| Addon | A native game-window component, not a separate Dalamud plugin. Names such as `MiragePrismPrismBox` identify specific game windows. |

### 1.2 Compatibility Basis

Use Windows x64, .NET 10, and Dalamud API 15 for this implementation. Use the SDK selected by [global.json](../global.json) and the dependencies declared in the [plugin project](../project/Exportaramus.csproj).

Game reads depend on the exact FFXIV build. Maintain a reviewed-build table for each source and native operation. An unfamiliar build must make the affected reader unavailable until its definitions, memory layout, and calls have been checked. Successful compilation alone does not establish that a native read or interaction is correct.

The exact supported builds are maintained in [ReaderCompatibility](../src/Features/CollectionData/Capture/ReaderCompatibility.cs). Website mappings and guided interaction have their own compatibility checks; a successful collection read does not bypass them.

### 1.3 Reading This Specification

Component names in [Section 3](#3-architecture-and-ownership) describe responsibilities; they are not required C# type names. Code formatting identifies concrete APIs, types, JSON keys, and literal values where those are specified. For example, C# property `SourceStatus`, JSON field `sourceStatus`, and serialized value `"Fresh"` have distinct roles. Preserve the exact contract spelling.

| To Implement or Check | Start Here |
| --- | --- |
| Information meaning, identity, and source health | [Section 4](#4-data-meaning). |
| Catalog definitions, IDs, and game reads | [Sections 5–7](#5-collection-catalog). |
| Capture, accepted publication, and files | [Sections 8–9](#8-scheduling-and-capture), then [Section 12](#12-local-storage). |
| Inventory and guided capture | [Sections 10–11](#10-inventory-integration). |
| Collection or Inventory IPC | [Section 13](#13-collectiondata-ipc) or [Section 14](#14-inventory-ipc). |
| Expanded files, website export, or diagnostics | [Sections 15–17](#15-expanded-snapshot-files). |
| Implementation milestones and verification | [Section 20](#20-suggested-implementation-order) and [Section 19](#19-verification-requirements). |
| Exact JSON fields, types, and allowed values | [Section 21](#21-json-schema-reference); Inventory fields are in [Section 14](#14-inventory-ipc). |

## 2. Functionality

### 2.1 Required

- Track all 25 collection types listed in Section 5.
- Distinguish permanent unlocks, current item presence, calculated collection results, and acquisition reports.
- Capture the logged-in character automatically and through **Sync Now**.
- Read inventory by location, including the employed retainer roster and separate normal/HQ quantities.
- Guide the player through Glamour Dresser, Armoire, and retainer capture without moving the character.
- Preserve useful earlier observations with their original times when a temporary read failure prevents replacement.
- Save per-character data and a bounded snapshot history.
- Publish CollectionData 1.0, 1.1, and 1.2, plus the separate Inventory V1 interface.
- Produce expanded snapshot files for external applications and a distinct clipboard export for FFXIV Collect.
- Provide source-specific health, capture progress, collection counts, retainer coverage, and diagnostics to the frontend.
- Handle logout, character changes, plugin unload, interrupted writes, unavailable sources, and malformed stored data.
- Retain the player's Reduce Motion preference and follow the selected game theme.

### 2.2 Out of Scope

- Cloud synchronization, runtime uploads, telemetry, and direct website-account modification.
- Moving the player to a furnishing or Summoning Bell.
- Buying, selling, discarding, transferring, equipping, or consuming items.
- Managing retainer ventures or market listings.
- Account-wide inventory totals or simultaneous queries across characters.
- Treating acquisition reports as proof of a permanent unlock.
- Recording attached materia, spiritbond, or achievement reward-claim state.
- Unlimited history, automatic deletion of unrelated files, or a player-facing history browser.
- Consumer-controlled capture, refresh, or item-action IPC functions.

Guided capture is in scope: it opens the required windows, advances matching retainer dialogue, reads data, and closes the windows through the game's normal exit behavior. It does not move items.

## 3. Architecture and Ownership

### 3.1 Components

| Component | Responsibility |
| --- | --- |
| Plugin Bootstrap | Acquire Dalamud services; register commands, events, windows, and IPC; own shutdown. |
| Catalog Builder | Read game definitions and reviewed mapping resources into ordinary C# values. |
| Collection Readers | Read unlocks, quests, notebooks, Armoire, and Dresser state. |
| Capture Controller | Schedule reads, handle character identity, combine observations, and reject obsolete work. |
| Inventory Service | Maintain location-specific observations and per-character inventory persistence. |
| Guided Capture Controllers | Advance the two guided workflows one step at a time. |
| Output Preparation | Calculate collection results, validate JSON, calculate changes, and prepare frontend values. |
| Storage | Read and replace local files, recover interrupted writes, and retain history. |
| IPC Providers | Return accepted data and send change or withdrawal notifications. |
| Diagnostics | Collect bounded, redacted operational information on request. |
| Frontend | Render prepared values and submit user intent. |

Keep ordinary C# calculations separate from Dalamud and native adapters. Native adapters have `.Dalamud.cs` filenames. This lets data handling, scheduling, serialization, and workflow decisions run in tests without the game.

### 3.2 Thread Rules

Native reads and UI interactions run on the framework thread. Check character identity and readiness before and after each read. Never carry a native pointer, live game-sheet row, or game-window object into background work.

Copy the needed values first. Background tasks may then sort, calculate results, validate JSON, calculate hashes, and read or write files. A background task receives its own immutable inputs: values that cannot be changed while it is working.

The draw handler renders prepared data and queues intent. It does not read the game, scan a catalog, serialize JSON, wait for a task, or perform file I/O.

IPC getters inspect accepted managed data. They do not start capture or read disk. Inventory item queries may filter the bounded managed snapshot.

### 3.3 Lifetime

Construction must clean up any registrations already made if a later initialization step fails.

On shutdown:

1. Mark the plugin as stopping and prevent new requests.
2. Stop guided actions and native reads.
3. Remove framework, draw, game-event, and addon-event subscriptions.
4. Withdraw published character data and unregister IPC functions.
5. Cancel outstanding work and wait for owned tasks to finish.
6. Finish bounded final inventory-cache writes, then release resources.

Every task has an owner that observes completion or failure. A task must not call a Dalamud service after that service has been released.

## 4. Data Meaning

### 4.1 Four Kinds of Information

| Kind | Example | Meaning |
| --- | --- | --- |
| Permanent Unlock | An unlocked mount | The game reports the character has registered the entry. |
| Current Item Presence | A roll in a retainer's inventory | The item existed in that source when it was read. |
| Derived Result | An outfit with all required pieces present | A result calculated from other accepted observations. |
| Reported Acquisition | A consumer reports obtaining an item | An advisory report from another plugin. |

Buying an orchestrion roll, an item consumed to register music, does not establish that the character used it. Finding an item in storage does not establish permanent ownership. Keep these records separate in memory and output.

### 4.2 Source Health

Every source has its own health record. These are the JSON field names; the values shown in code formatting are literal contract values.

| Field | Values or Meaning |
| --- | --- |
| `dataType` | `PermanentUnlock`, `CurrentItemPresence`, `Derived`, or `ReportedOwnership`. |
| `sourceStatus` | `Fresh`, `Stale`, `Unavailable`, or `NeverObserved`. |
| `completeness` | `Complete`, `Partial`, or `Unknown`. |
| `provenanceType` | `Observed`, `Restored`, `Reported`, `Derived`, or `None`. |
| `capabilityState` | `Supported`, `Degraded`, or `UnavailablePendingValidation`. |
| `observedAtUtc` | Successful game-observation time for observed data; retained/restored data preserves its earlier time. Reported and derived sources use their own evidence times. Omitted when no time is available. |
| `captureGeneration` | Generation associated with this health record. Failed attempts and `NeverObserved` records can also carry a generation; it does not prove a successful observation. |
| `reasonCode`, `reason` | Structured failure/restriction code and its explanation. |

`Fresh` and `Complete` describe the accepted source's status and coverage. They do not by themselves prove a game observation: reports can be `Fresh` and `Complete` with `Reported` provenance, and calculated results can have `Derived` provenance. A current complete game observation also requires `provenanceType: "Observed"`, a valid observation time, and the correct character. `Stale` means earlier information is retained. `Unavailable` describes a source that could not supply accepted data; `NeverObserved` describes a source without a successful observation.

A source is usable for historical reporting when it is `Fresh` or `Stale`, `Complete`, and not `UnavailablePendingValidation`. Features that claim current ownership must additionally require suitable fresh evidence and its provenance. A derived result must be interpreted through the contributing facts in [Section 7](#7-calculate-outfits-and-relics); it is not a direct game read.

A complete empty result establishes emptiness at the observation time. A missing, failed, pending, or incomplete result does not. After a temporary read failure, retaining an earlier observation preserves its generation as well as its time. A new snapshot capture generation does not refresh every included source. Generations belong to one provider session; restoration assigns the current session generation while preserving the earlier observation time and marking the source `Stale` and `Restored`.

Compact JSON places these records in `sourceHealth[sourceKey]`. `sourceCapabilities[sourceKey]` describes support for reading that source; supported code can still lack an observation. The complete field definitions are embedded in the common schemas for [1.0](#2111-common-definitions), [1.1](#2121-common-definitions), and [1.2](#2131-common-definitions).

### 4.3 Replacement and Retention

A complete successful read replaces that source, including when the replacement is empty. Normalize unlock IDs into distinct ascending positive integers.

For the same character, retain an earlier usable observation as Stale after these temporary failures:

- the game has not loaded the data this session;
- a required window is not ready;
- the required containers or retainer set are incomplete; or
- the read failed.

Preserve the earlier IDs, quantities, and observation time. Record the new failure reason.

Do not retain data as usable after a mapping failure, unsupported-build failure, quantity overflow, or size-limit failure. These failures can invalidate the interpretation of the earlier data.

Data restored from disk is Stale with Restored provenance. Never restore one character's data into another character's active state.

### 4.4 Identity and Ordering

Use three identifiers for collection publications:

- `contentId`: the active character, serialized as a decimal string;
- `providerSessionId`: a new GUID for each plugin lifetime; and
- `revision`: an increasing publication number within that lifetime.

Use a separate capture generation to reject unfinished work after cancellation or a character change. Before accepting a background result, compare its character and generation with the controller's current values.

Confirmed logout and character changes withdraw the old publication immediately. A temporary identity gap during zoning may retain the active scope for up to 60 seconds. Do not perform a native capture without a currently valid character ID. Outside zoning, an identity gap withdraws immediately.

A snapshot is `Coherent` only when `captureType` is `Full` and every usable source is `Fresh` from that capture generation. Otherwise label it `MixedObservationTimes`. This rule uses the usability definition in Section 4.2; it does not require every source to be usable or replace source-specific completeness and provenance checks. It prevents consumers from treating earlier storage observations as one simultaneous inventory read.

## 5. Collection Catalog

The catalog describes what can be collected: IDs, display names, and relationships between definitions. Character readers separately determine which entries are unlocked or present. A catalog entry alone is never ownership evidence.

Obtain the `IDataManager` service from Dalamud through the plugin constructor, as in [Plugin.Dalamud.cs](../src/Bootstrap/Plugin.Dalamud.cs). The interface is in `Dalamud.Plugin.Services`. Call `data.GetExcelSheet<T>()` with the appropriate row type from `Lumina.Excel.Sheets`; for example, `data.GetExcelSheet<Mount>()` reads the installed Mount table. These are game-data tables; no spreadsheet application, external download, or hand-written list of every game item is required.

Read the tables in the collection matrix and copy the needed IDs, strings, flags, and relationships into plain managed values. Convert name fields to strings while copying. Keep both the public collection ID and the original lookup ID when the rules below transform one into the other. The current implementation calls these `CatalogItem.Id` and `CatalogItem.SourceRowId`; they are internal C# properties, not additional JSON fields.

Build the catalog from installed sheets plus the reviewed resources in [Section 5.3](#53-reviewed-mapping-resources). Cache it by `IDataManager.Language` and the base-game repository version, obtained from `data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version`. Reuse only a matching completed catalog. If either value changes during projection, discard that work; reject a background result whose version/language no longer matches, then rebuild. An unreadable table or failed projection must not become an empty catalog or an all-unowned result.

Project game rows in framework-thread slices of at most 2,048 rows and approximately one millisecond. Copy plain values before yielding; background work may then deduplicate and sort the copied values. Normalize duplicate IDs and sort results by ID. The Item table also supplies known base item IDs for validation and names for observed Dresser items; scanning those definitions does not read a character's inventory.

### 5.1 Collection Matrix

The expanded model contains 25 collection types. The Game Meaning column explains the display names; it does not change those names or the literal keys.

| Key | Display Name | Game Meaning | Definition and Ownership |
| --- | --- | --- | --- |
| `mounts` | Mounts | Creatures or vehicles unlocked for travel. | Mount rows; Dalamud mount-unlock query. |
| `minions` | Minions | Collectible companions that follow the character. | Companion rows; companion-unlock query. |
| `emotes` | Emotes | Character gestures and animations. | Named Emote rows with a text command; emote-unlock query. |
| `bardings` | Bardings | Appearance equipment for a chocobo companion. | BuddyEquip rows; barding-unlock query. |
| `cards` | Triple Triad Cards | Cards collected for the Triple Triad card game. | TripleTriadCard rows; card-unlock query. |
| `npcs` | Triple Triad NPCs | Card-game opponents the character has defeated. | Valid ENpcBase links to TripleTriad rows; native NPC-beaten query. |
| `facewear` | Facewear | Unlocked glasses styles. | Glasses variants grouped by style; any unlocked mapped variant establishes the style. |
| `beasts` | Beasts | Collectible beast unlocks. | Reviewed XBMPet IDs; native pet-unlock query. |
| `fashions` | Fashion Accessories | Wearable accessories outside ordinary equipment. | Ornament rows; ornament-unlock query. |
| `frames` | Portrait Frames | Decorative portrait options. | BannerBg unlock conditions, plus condition 573; banner-condition query. |
| `occult_records` | Occult Records | Collectible lore records. | MKDLore rows; lore-unlock query. |
| `orchestrions` | Orchestrion Rolls | Music registered for an in-game music player. | Orchestrion rows; orchestrion-unlock query. |
| `spells` | Blue Magic Spells | Spells learned by the Blue Mage job. | AozAction rows with an Action; Blue Magic unlock query. |
| `aetherytes` | Aetherytes | Unlocked teleport destinations. | Visible Aetheryte rows marked as aetherytes; native unlock query. |
| `chocobo_routes` | Chocobo Routes | Unlocked transport routes. | ChocoboTaxiStand rows; route-unlock query. |
| `leves` | Levequests | Optional tasks whose completion is recorded. | Leve rows; leve-completion query. |
| `hairstyles` | Hairstyles | Unlocked character hairstyle options. | CharaMakeCustomize unlock links; hairstyle-unlock query. |
| `field_records` | Field Records | Collectible notebook records. | MYCWarResultNotebook record numbers; mapped item-action query. |
| `survey_records` | Variant Dungeon Records | Notebook entries for dungeon routes. | VVDNotebookContents; notebook series and slot state. |
| `quests` | Quests | Tasks with accepted and completed states. | Quest rows; completion plus accepted state and sequence. |
| `achievements` | Achievements | Recorded accomplishment milestones. | Achievement rows; completion after the achievement list is loaded. |
| `armoires` | Armoire | Eligible equipment held in the Armoire. | Cabinet entries mapped to item IDs; current cabinet membership. |
| `glamour_dresser` | Glamour Dresser | Items and outfit components stored for appearance use. | Observed dresser item membership and physical entries. |
| `outfits` | Outfits | Equipment sets with required constituent pieces. | Reviewed required-piece mappings; all required pieces must be present. |
| `relics` | Relics | Equipment tracked by item and achievement relationships. | Reviewed item/achievement mappings; item presence or specified achievement completion. |

The first 21 are permanent-unlock sources. Armoire and Dresser are physical presence. Outfits and relics are derived. Retainers are inventory coverage, not a twenty-sixth collection type.

These are expanded collection keys. The corresponding compact physical-source keys are `armoire` and `glamourDresser`, as listed in [Section 6.8](#68-physical-presence-for-collectiondata); do not substitute `armoires` or `glamour_dresser` in those source maps.

### 5.2 Identifier and Row Rules

Use a collection's own IDs. Item IDs, mount IDs, action IDs, and notebook IDs identify different definitions. Sheet names and field paths in this table are concrete Lumina types and members. `RowId` below belongs to the named sheet unless another relationship is specified.

| Collection | ID and Display Name | Eligibility and Lookup Rules |
| --- | --- | --- |
| Mounts | `Mount.RowId`; `Singular.ToString()`. | Nonzero row, nonnegative `Order`, nonzero `Icon`, and a nonempty name. |
| Minions | `Companion.RowId`; `Singular.ToString()`. | Nonzero row, nonzero `Icon`, and a nonempty name. |
| Emotes | `Emote.RowId`; `Name.ToString()`. | Nonzero row, nonzero `TextCommand.RowId`, and a nonempty name. |
| Bardings, Cards, Occult Records, Orchestrion Rolls, Levequests, Variant Dungeon Records, Achievements | `RowId` and `Name.ToString()` from BuddyEquip, TripleTriadCard, MKDLore, Orchestrion, Leve, VVDNotebookContents, and Achievement respectively. | Nonzero row ID and nonempty name. |
| Fashion Accessories | `Ornament.RowId`; `Singular.ToString()`. | Nonzero row ID and nonempty name. |
| Chocobo Routes | `ChocoboTaxiStand.RowId`; `PlaceName.ToString()`. | Nonzero row ID and nonempty name. |
| Triple Triad NPCs | Each `ENpcBase.ENpcData` link's `RowId`; matching `ENpcResident` row's `Singular.ToString()` for the ENpcBase row. | The linked TripleTriad row must exist and its ID must be in `[2293760, 2359296)`. Deduplicate repeated links; missing names use a fallback. |
| Portrait Frames | `BannerBg.UnlockCondition.RowId`; `BannerBg.Name.ToString()`. | Require condition ID greater than one. Query BannerCondition using that ID, not the BannerBg row ID. Include condition 573 with a fallback name when needed. |
| Blue Magic Spells | `AozAction.RowId`; linked `Action.Value.Name.ToString()`. | Require nonzero row and `Action.RowId`. Query using the AozAction source row; the Action ID is not the collection ID. |
| Aetherytes | `Aetheryte.RowId`; `PlaceName.Value.Name.ToString()`. | Require nonzero row, `IsAetheryte`, and not `Invisible`. |
| Hairstyles | `CharaMakeCustomize.UnlockLink`; linked `HintItem.Value.Name.ToString()` when an item exists. | Require nonzero unlock link. Preserve the CharaMakeCustomize source row for querying; itemless entries use `Hairstyle <id>`. |
| Field Records | `MYCWarResultNotebook.Number`; `Name.ToString()`. | Require nonzero record number and nonempty name. Use that number as the collection ID, not the source-row or item ID. |
| Quests | `Quest.RowId`; display `Entry <id>`. | Require a nonzero row and nonempty source `Name` before replacing the display name with the fallback. |
| Armoire | `Cabinet.RowId + 1`; linked `Item.Value.Name.ToString()`. | Require nonzero `Order` and `Item.RowId`. Native membership uses the original Cabinet row ID; presence output uses the linked item ID. |
| Facewear | `Glasses.Style.RowId + 1`; `Facewear <id>`. | Group nonzero Glasses variants by style. Variant IDs must fit UInt16; the style-plus-one calculation must not overflow. |
| Beasts | Reviewed IDs 1–50; `Beast <id>`. | Validate the raw XBMPet row directory as required by [Section 6.5](#65-beasts); do not infer typed columns. |
| Outfits and Relics | Reviewed item IDs; corresponding `Item.Name.ToString()`. | Obtain constituents and optional achievement links from [Section 5.3](#53-reviewed-mapping-resources), then apply [Section 7](#7-calculate-outfits-and-relics). |

Use localized names when available. Preserve whether a name is a fallback. Current fallback labels include `Entry <id>`, `Hairstyle <id>`, `Facewear <id>`, and `Beast <id>`. Other named rows require a nonzero row ID and nonempty name unless their mapping explicitly defines a fallback.

The Dresser catalog selects only observed items from the known Item definitions and has `totalKnown: false`; the game does not define a fixed complete dresser collection. Other listed collections have a known catalog total.

The projection and filtering rules above correspond to [CollectionCatalogBuilder.Dalamud.cs](../src/Features/CollectionData/Catalog/CollectionCatalogBuilder.Dalamud.cs) and [CatalogRowPolicy.cs](../src/Features/CollectionData/Catalog/CatalogRowPolicy.cs).

### 5.3 Reviewed Mapping Resources

The installed sheets provide most catalog definitions. Packaged resources supply reviewed relationships and destination-contract constraints:

| Resource | Role |
| --- | --- |
| [ffxiv-collect-contract.json](../data/ffxiv-collect-contract.json) | Baseline website contract: reviewed upstream revision, key order, size limit, expected catalog counts/hashes, and checksums of related resources. |
| [identifier-mappings.json](../data/FFXIVCollect/1.0/identifier-mappings.json) | Source and website identifiers, transforms, and explicit inclusions/exclusions. It does not supply localized display names. |
| [recognized-ids.json](../data/FFXIVCollect/1.0/recognized-ids.json) | Numeric allowlists for the website-import categories. It is not a complete named catalog of all 25 collections. |
| [derived-inputs.json](../data/FFXIVCollect/1.0/derived-inputs.json) | Outfit required-item sets and relic item/achievement relationships. Its full schema is embedded in [Section 21.4.3](#2143-outfit-and-relic-mapping-inputs). |
| [catalog-refresh.json](../data/FFXIVCollect/1.2/catalog-refresh.json) | Reviewed additive website IDs and additional outfit constituent sets applied to the baseline. |
| [beasts-export-contract.json](../data/FFXIVCollect/1.2/beasts-export-contract.json) | Separate reviewed Beasts website-import mapping and ID coverage. |
| [Contract 1.0](#211-collectiondata-10), [1.1](#212-collectiondata-11), and [1.2](#213-collectiondata-12) Schemas and Resources | Frozen output-validation contracts. Their versioned resources constrain the matching output, rather than replacing the installed game's full definitions. |

The Consumer Kit's `resources/recognized-ids.json` likewise supplies numeric import allowlists, has no names, and does not include mounts. Neither those IDs nor IPC capabilities can supply a named mount checklist.

Keep a resource's reviewed version and checksum together. A mapping update requires checking the entire supported catalog, not merely showing that one newly added ID exists. A resource's historical version label does not bypass the current reviewed-build checks in Section 1.2.

Runtime code must not fetch a replacement catalog from the network. Missing or invalid resources make the affected feature unavailable. Do not replace failed outfit or relic mappings with an empty owned collection.

### 5.4 Mount Example

The complete path for mounts is:

1. Obtain `IDataManager` and `IUnlockState` from Dalamud. On the framework thread, enumerate `data.GetExcelSheet<Mount>()`.
2. Apply the Mount row rules in Section 5.2. Copy `RowId` and `Singular.ToString()` into managed catalog entries; preserve the same row ID for later lookup. Finalize a distinct, ascending catalog for the recorded game-data version and language.
3. During a character capture, apply Section 6.1's identity, readiness, and compatibility checks. For every catalog entry, obtain its Mount row and call `unlocks.IsMountUnlocked(row)`. Accept the source only after all required queries succeed.
4. Store the IDs with a true result as the `mounts` observation. An accepted complete read can legitimately produce an empty set. A failed query leaves the source failed or eligible for same-character stale retention; it does not create an empty result.
5. After publication validation, compact JSON supplies `unlocks.mounts.unlockedIds` and `sourceHealth.mounts`. The expanded collection joins the same IDs to catalog names. A current named checklist requires matching character/catalog identity and a complete, fresh, observed mount source.

A mount's collection ID is distinct from the Item ID of a consumable that unlocks it. Acquiring that item does not establish mount registration. A consumer-side version of this example appears in [Consumer Integration Guide, Section 2.5.1](<Consumer Integration Guide.md#251-obtaining-a-catalog>).

## 6. Read the Game

### 6.1 General Read Procedure

1. Verify the framework thread, active nonzero Content ID, and reviewed game build.
2. Check that the required game manager, container, or window is present and ready.
3. Copy the required values.
4. Recheck identity, manager identity, readiness, and any size/count used by the read.
5. Accept the source only if the whole required set was read successfully.

If one row fails, do not publish the preceding rows as a complete collection. Record the failure and apply Section 4.3.

Permanent-unlock capture advances in slices of 128 steps by default, with a supported maximum of 2,048. This bounds repeated game queries without sending them to a background thread.

### 6.2 Ordinary Unlocks

Use Dalamud's `IUnlockState` methods for mounts, companions, emotes, bardings, cards, ornaments, banner conditions, lore, orchestrions, Blue Magic, taxi stands, leves, hairstyles, quests, and achievements.

Require `IsAchievementListLoaded` before achievement queries. An unloaded achievement list is unavailable, not empty. Achievement reward-claim state is unknown and exported as null.

Aetherytes and Triple Triad NPC completion use the corresponding UIState queries after checking the manager exists.

For quests, completion takes precedence over accepted state:

| Conditions | Status | Sequence |
| --- | --- | --- |
| Completed | Completed | Present only if still accepted. |
| Not completed, accepted | InProgress | Current quest sequence. |
| Neither | NotStarted | Absent. |

Read accepted state and sequence from QuestManager. A missing manager fails the quest source.

### 6.3 Field Records

Build a one-to-one map for record IDs 1–50 using ItemAction action ID 19743 and its record-number data. Require all 50 records, unique item IDs, and no duplicate record IDs.

Query `UIState.IsItemActionUnlocked` using the mapped native Item row:

| Return Value | Interpretation |
| --- | --- |
| 1 | Unlocked |
| 2 | Not unlocked |
| 3 | Cache pending; source not yet loaded |
| Any other value | Mapping/read interpretation invalid |

The first query may request server-owned cache data. Pending does not mean the player must unlock the feature. Preserve the distinction in source health and retry after the cache becomes ready.

### 6.4 Facewear

Group Glasses variants by style ID. A style is unlocked when any mapped variant is unlocked through `PlayerState.IsGlassesUnlocked`.

Validate every mapped row even after finding an unlocked variant. A malformed later variant must not be silently skipped. Require loaded PlayerState before and after the read and the same manager identity throughout.

### 6.5 Beasts

Require XBMManager state `Received`. Validate the reviewed XBMPet row directory: sentinel row zero and IDs 1–50, with no extra rows. The current implementation reads the row directory only; it does not depend on unreviewed typed XBMPet columns.

Read all 50 unlock flags in one framework-thread operation. The number of unlocked flags must match the manager's count. Recheck manager identity, received state, and count before accepting the result.

Do not retain native pointers between updates. Any managed cache of those IDs lasts only for that capture and is reset on a new capture or character/session change.

### 6.6 Variant Dungeon Records

Require the notebook module to exist and its character Content ID to match the player.

Find each content ID in the nonzero VVDNotebookSeries rows. Map the series order and content position to the native notebook arrays. Validate both dimensions before reading. A missing relationship or out-of-range index fails the source.

### 6.7 Armoire and Glamour Dresser

Armoire capture requires a loaded cabinet. Query each eligible Cabinet source row and publish the associated item IDs as membership, with null quantities.

Dresser capture requires the visible, ready `MiragePrismPrismBox` window and a loaded MirageManager prism box. Require its reviewed 800-entry shape.

For each nonzero raw entry:

- calculate the base item ID as `rawItemId % 1_000_000`;
- preserve its physical entry index and raw ID;
- for an ordinary item, include one component at slot zero;
- for an outfit, inspect the 11 equipment slots and include only nonzero, unlocked, recognized components.

Ordinary entries have a null outfit ID. Outfit entries use the base ID as outfit ID. Entry indices are distinct and ascending in 0–799; component slots are distinct and ascending in 0–10. Reject unknown items, invalid shapes, and outfits with no accepted components.

The aggregate membership set is the distinct union of all components. Physical entries remain separate even when they contain the same item. This supports both a presence query and a consumer that needs the dresser's actual arrangement.

### 6.8 Physical Presence for CollectionData

The compact collection interface exposes seven physical sources:

| Source | Containers |
| --- | --- |
| `inventory` | Four ordinary inventory pages. |
| `armoryChest` | Equipped items plus all Armoury Chest equipment and soul-crystal containers. |
| `chocoboSaddlebag` | Both ordinary saddlebag containers. |
| `premiumSaddlebag` | Both premium saddlebag containers. |
| `retainers` | Aggregate accepted per-retainer Inventory observations. |
| `armoire` | Cabinet membership. |
| `glamourDresser` | Dresser component membership. |

For quantified sources, combine duplicate item IDs with checked arithmetic and reject UInt32 overflow. Equipment slots count as one instance. For Armoire and Dresser, deduplicate membership and publish null quantities.

Inventory V1 provides a separate, more detailed location model; it includes crystals and separates equipped items from the Armoury Chest.

## 7. Calculate Outfits and Relics

An outfit is owned only when every required constituent is present in a usable physical source.

For each constituent, search the seven presence sources:

- a matching usable observation establishes presence;
- absence is established only when every required source is usable and none contains the item;
- otherwise the answer is unknown.

A relic is present if its item is found or its mapped achievement is complete. If neither is true, both branches must be known before marking it absent.

If any catalog entry remains unknown, omit the complete derived collection and explain the missing input. Do not publish unknown entries as unowned. The frontend may separately show a confirmed lower bound for outfits, such as `327+`, calculated from complete constituent matches in available sources.

Derived health uses the oldest contributing observation time. It is Stale if any contributing observation is Stale. Include the facts that support each result so a consumer can distinguish physical presence from permanent unlock evidence.

## 8. Scheduling and Capture

### 8.1 Capture Types

| Type | Work |
| --- | --- |
| Full | Read all unlocks and physical sources, calculate expanded collections, prepare all output formats. |
| Lightweight | Refresh physical presence while preserving other accepted sources. |
| Targeted | Reread only the sources named by a change hint or fallback scheduler. |
| Reported | Add accepted acquisition reports without claiming a new native read. |
| Restored | Load same-character saved observations and mark them as restored. |

This table describes capture work. `Targeted` is an internal scheduling mode, not a permitted JSON `captureType` value; targeted publications use `Lightweight`. The serialized values are `Full`, `Lightweight`, `Reported`, and `Restored`, as defined in the [common schema](#2131-common-definitions). A lightweight or targeted update may refresh compact IPC data without rewriting the expanded full-snapshot files. Therefore the latest expanded-file time can differ from the latest IPC observation.

### 8.2 Timing and Priority

| Trigger | Schedule |
| --- | --- |
| Login | First full capture after a 15-second grace period. |
| Presence refresh | Every 300 seconds after an accepted scheduled capture. |
| Full refresh | Every 1,800 seconds after an accepted full capture. |
| Manual Sync Now | Queue a full capture and reset automatic retry backoff. |
| Automatic failure | Retry after 30 seconds, then 120 seconds, then 300 seconds. |
| Manual failure or cancellation | No automatic retry of that request. |
| Change hints | Combine for 250 ms; retry within 15 seconds at delays of 1, 3, and 8 seconds. |
| Fallback | Advance one source every 1.5 seconds; quests have their own 300-second fallback. |

A hint requests a read; it never directly changes ownership.

Priority is manual full, scheduled full, scheduled presence, accepted report work, then targeted work. Keep one collection capture/publication operation active at a time. Targeted activity must not postpone scheduled full or presence deadlines.

### 8.3 Change Detection

Repeated reads often produce identical data with a new timestamp. Saving each one as a new history entry would hide meaningful changes in duplicate files.

Create a stable representation of each source, then calculate its SHA-256 hash. Stable means sorted source names and IDs, consistent numeric representation, and a fixed field order.

Include ownership, item quantities, quest status/sequence, supporting evidence, and physical dresser composition. For the health-aware hash, also include capability, completeness, status, and reason code. Exclude display names and read/report timestamps: those alone should not create a data-change history entry.

Keep both data-only and health-aware source hashes. They distinguish a changed item set from a source becoming unavailable. Combine health-aware hashes in ordinal source-name order to decide whether the full result changed.

An unchanged targeted read does not publish merely to advance timestamps. Meaningful health or capability changes can publish even when IDs are unchanged. An accepted full capture can replace the current files without adding a duplicate history file.

## 9. Prepare and Publish a Snapshot

### 9.1 Publication Procedure

1. Assemble accepted observations and source health for the current character.
2. Calculate expanded collections and ownership evidence.
3. Serialize each required contract version.
4. Validate field types, required fields, IDs, limits, and relationships between fields.
5. Prepare counts, deltas, hashes, export availability, and IPC capabilities.
6. Persist the collection files.
7. Recheck character and operation generation.
8. Replace the accepted publication in one operation, then send change notices.

Consumers must never see half-prepared data or a result from an obsolete character. A serialization or persistence failure leaves the previous accepted publication intact and records an error.

### 9.2 Validation

A JSON document can have the expected fields and types yet still contain incorrect claims. For example, a complete-empty unlock set paired with unavailable source health is contradictory.

Validate both structure and meaning:

- exact supported schema version and provider identity;
- nonzero decimal Content ID and matching active character;
- provider session and revision;
- distinct ascending recognized IDs;
- source status, completeness, provenance, and observation time;
- valid capture generations and consistency;
- quantities within limits;
- outfit/relic evidence matching the claimed result; and
- Dresser entries matching aggregate membership.

Use UTF-8 byte counts for size limits. Parse integers exactly; a Content ID must not pass through floating-point conversion.

The complete JSON schemas are embedded in [Section 21](#21-json-schema-reference), alongside links to their canonical files. The embedded definitions and canonical files must agree. Update both together and check their parsed JSON for equality before committing a revision.

### 9.3 Counts and Frontend Values

Prepare owned counts and same-character deltas outside the draw handler. A delta compares with the previous accepted publication, not with a different character or an assumed empty baseline.

Prepare owned counts, deltas, snapshot time, history count, retainer captured/employed count, source-health metadata, and export status. Unknown or incomplete sources must not display an ordinary zero count. The complete internal boundary is specified by the Frontend Specification's [input contract](<Exportaramus - Frontend Specification.md#35-backend-input-contract>), [operations and guided view](<Exportaramus - Frontend Specification.md#36-backend-operations-and-guided-view>), and [display state](<Exportaramus - Frontend Specification.md#37-display-state>). These are internal design contracts, not supplied C# APIs. Public IPC does not supply history counts, action availability, or guided-workflow state. The backend supplies the inputs and operations in Frontend Sections 3.5–3.6. The frontend presenter builds the display state in Section 3.7, including action availability, source labels, and missing-reader details.

Preserve the two distinct outfit lower bounds:

| Prepared Value | When Supplied | Calculation |
| --- | --- | --- |
| Publication's confirmed partial `outfits` count | The complete outfits collection is absent, at least one CollectionData presence source is usable, and the outfits catalog is nonempty. Otherwise omit the map entry. | Count nonempty required-piece sets contained in the union of usable CollectionData presence sources. Supply a computed zero. |
| Inventory's confirmed outfit lower bound | An Inventory snapshot, accepted collection publication, and outfits catalog are present. Otherwise supply null. | Count nonempty required-piece sets contained in the union of Inventory locations with a successful observation and usable CollectionData presence sources. An empty catalog or no matched set produces zero. |

The inventory value can include individually observed retainer storage. It remains a lower bound from last-observed information; it does not establish current complete ownership. When supplied, it overrides the displayed Outfits count as specified in the [frontend row rules](<Exportaramus - Frontend Specification.md#84-construct-the-rows>). It is separate from the publication's partial-count map.

The main-window controls are **Sync Now**, **Retainers**, **Dresser & Armoire**, **Copy JSON**, and **Reduce Motion**. Copy JSON uses the already prepared website export and does not begin capture.

## 10. Inventory Integration

### 10.1 Locations and Quantities

Inventory V1 records these location kinds:

| Kind | Contents |
| --- | --- |
| Inventory | Four inventory pages and crystals. |
| Equipped | Equipped items. |
| ArmoryChest | Armoury equipment and soul-crystal containers, excluding equipped items. |
| ChocoboSaddlebag | Both ordinary saddlebag pages. |
| PremiumSaddlebag | Both premium saddlebag pages. |
| Retainer | Seven retainer pages, equipped items, and retainer crystals. |
| Armoire | Membership only. |
| GlamourDresser | Membership only. |

Key a location by `(kind, retainerId)`. Non-retainer locations use retainer ID zero. Names are display text, at most 64 characters, nonempty, and free of control characters.

Each location contains state, successful observation time, latest attempted-read time, quantity support, and normalized item records. Each record has a positive base item ID below 1,000,000 and separate normal/HQ counts.

Limit a location to 50,000 input records. Merge duplicate IDs; the combined normal-plus-HQ quantity for one item in one location must fit UInt32. Reject zero quantified stacks, unknown item definitions, overflow, or a partially available container set. Zero crystal slots are empty slots rather than failed ordinary stacks.

For membership-only locations, the internal model may use zero numeric placeholders, but IPC emits null quantities. Neither null nor an internal zero means one physical copy.

### 10.2 Passive Capture and Replacement

Inspect one location kind every 200 ms on the framework thread. The retainer kind reads only the active retainer after validating the full roster.

Before accepting containers, validate loaded state, expected sizes, slots, and the active identity. Recheck those facts after reading.

A successful complete read replaces that location independently, including an empty result. A failed attempt preserves earlier contents if present and updates state; it does not replace them with an empty set. These Inventory V1 location-retention rules are separate from CollectionData's source-usability rules in Section 4. Suppress identical successful observations within one minute to avoid unnecessary publications and writes.

| State | Meaning |
| --- | --- |
| Unknown | No successful complete observation. |
| Observed | Complete observation accepted during this run; not continuous live tracking. |
| Retained | Earlier observation preserved or restored. |
| ReadFailed | Latest read failed; earlier data may remain. |

`observedAtUtc` remains the time of the successful read. For ReadFailed, a non-null observation time identifies preserved earlier data; null means there has been no successful observation. `attemptedAtUtc` does not prove a successful observation. An unread retainer is unknown, not empty.

### 10.3 Retainer Roster

Read the actual roster, from zero to ten employed retainers. Require unique nonzero IDs, valid names, availability, and stable ordering through the read. An incomplete roster remains unknown.

Confirm roster membership before accepting an active retainer's inventory. A confirmed roster removes dismissed retainers and includes new retainers as unknown. Restored retainer records do not prove the current roster is known.

Match native retainer identity and the visible inventory window before and after capture. Visiting one retainer must not refresh another retainer's observation time.

### 10.4 Service and Cache

Publish immutable Inventory snapshots with an increasing in-memory revision. Inventory V1 has no provider session ID.

Persist to `snapshots/characters/<contentId>/inventory.json` under the plugin configuration directory, independently of collection history. The cache is at most 4 MiB and must match the requested character. Validate before writing and after reading.

Allow one owned I/O operation at a time. Combine pending writes by character, with at most 16 character scopes. A newer pending value replaces an older one for the same character. Retry cache failures after 30 seconds and expose the failure diagnostically.

A delayed restore must not replace a newer successful location observation. Restored observations become Retained and preserve their original times. Before saving, preserve same-character saved locations that have not yet been observed during startup.

Shutdown cancels the active worker, waits for it, and attempts final immutable-cache writes with a five-second deadline.

### 10.5 Queries and Aggregation

`GetItem` returns matching locations and separate UInt64 normal/HQ totals. Sum only quantified locations. Include membership matches with null quantities.

Mark the quantity meaning `LastObservedQuantifiedLocationsOnly`. The totals may combine old observations and different observation times.

Set `hasUnknownLocations` when required observations or roster coverage are unknown. Set `hasRetainedLocations` when any location with an observation is not Observed, including a nonmatching location.

An unavailable full snapshot has `available: false`, null character and locations, and revision zero. An unavailable item query returns empty matching locations, zero totals, and unknown coverage. Its zero totals do not assert an empty inventory.

CollectionData's `retainers` aggregate comes from this service after confirming the roster. Require complete coverage for a replacement aggregate. Use the oldest contributing observation time; restored/retained contributors make it stale. A partly read replacement roster can leave the earlier same-character aggregate stale until replacement is complete.

## 11. Guided Capture

Both guided workflows run on the framework thread and expose current progress and cancellation. Advance at most one controller step per update. Revalidate identity, readiness, and the specific object/window immediately before each native operation.

An operation request is not completion. Wait for the resulting game state.

Block guided interaction during combat, area transitions, cutscenes, or while mounted. The reviewed condition flags are `InCombat`, `BetweenAreas`, `BetweenAreas51`, `WatchingCutscene`, `OccupiedInCutSceneEvent`, and `Mounted`. The reviewed interaction range is 2.5 yalms. A yalm is the game's distance unit.

### 11.1 Dresser and Armoire

Process the Dresser first, then the Armoire:

1. Find a valid, targetable furnishing from the reviewed allowlist.
2. Prefer the current eligible target; otherwise choose the nearest, with a stable identity tie-break.
3. Ask the player to move into range when necessary.
4. Select the target, request interaction, and wait for the expected window and manager.
5. For the Armoire, choose its matching menu entry when required.
6. Capture the complete source.
7. Close the exact captured window and observe that it closed.
8. Continue to the next source, then request a full snapshot save.

Use reviewed object kinds, furnishing IDs, interaction range, addon names, and unsafe-condition checks. Do not infer a furnishing from its translated display name alone.

| Limit | Value |
| --- | --- |
| Automatic interaction attempts per source | 8 |
| Window appearance wait | 900 ms |
| Delay before another interaction attempt | 150 ms |
| Window/manager readiness wait | 15 seconds |
| Window closure wait | 5 seconds |

After repeated interaction failures, permit the player to open the required window manually. If readiness or closure cannot be confirmed, pause and explain the required action. Continue only after observing the requested state.

Retain a completed Dresser observation when Armoire capture is incomplete. Support saving that partial result. Cancellation stops further navigation and retains completed observations.

### 11.2 Retainers

Use these steps:

```text
Open Bell → Read Roster → Select Retainer → Open Inventory
→ Capture Inventory → Close Inventory → Quit Retainer → Wait for List
→ next retainer, or Close Overview and Inventory → Complete
```

Allow 60 seconds for finding/opening the bell and 15 seconds for each later step. Stop on timeout, cancellation, unsafe conditions, character change, or roster change. Preserve completed captures.

Run the retainer adapter at most once every 100 ms. Discover the nearest valid, targetable Summoning Bell event object from its validated base-ID set; inspect at most 1,024 objects. Refuse to open it over an existing selection menu or retainer inventory. Target the bell and require it to remain valid and within range before interaction.

Select the displayed retainer row by a unique name match only after validating the roster ID and expected active retainer. Support List and TreeList controls. Do not assume the displayed component length is exactly the employed-retainer count.

Use the localized entrust/withdraw inventory menu and quit labels. Require a unique matching enabled entry; do not use a guessed menu index.

Advance Talk dialogue only when the visible speaker matches the selected retainer and the relevant retainer identity is valid. The reviewed route sends a MouseClick event to the Talk receiver with the stage as its target. Throttle attempts to 750 ms and wait for the game to advance; do not claim success merely because the call returned.

### 11.3 Close Retainer Context Correctly

Closing the visible inventory window is insufficient if the game's inventory agent remains in Entrust mode. That state can leave “Entrust to Retainer” in the ordinary inventory menu after the retainer is gone.

Before quitting a retainer:

1. Confirm the captured inventory window still belongs to the selected retainer.
2. Validate its callback mapping points to the retainer agent with the reviewed event kind.
3. Request the native exit callback, currently integer callback `-1`.
4. Wait until the retainer inventory is no longer visible.
5. Confirm the inventory context no longer points to the retainer's context event and its title is not a retainer mode.

Do not replace this with an unverified generic window close. On the reviewed client, generic close can remove the window without clearing the context.

Only after that cleanup may the workflow select Quit, return to the retainer overview, and continue. After the final retainer, close the remaining ordinary inventory variants and RetainerList. Report Complete only after those windows are closed and the retainer context is clear.

## 12. Local Storage

All paths below are relative to the configuration directory supplied by Dalamud. Do not hard-code a user's Windows profile path.

| Path | Content |
| --- | --- |
| `current-snapshot.json` | Latest full expanded snapshot under the retained 1.0 file contract. |
| `current-snapshot-v1.2.json` | Latest full expanded snapshot including facewear and Beasts. |
| `snapshots/automatic-snapshot-<UTC>.json` | Changed automatic full captures. |
| `snapshots/manual-snapshot-<UTC>.json` | Changed manual full captures. |
| `snapshots/characters/<contentId>/collection-data.json` | Current compact per-character collection state. |
| `snapshots/characters/<contentId>/reported-evidence.json` | Accepted acquisition reports for that character. |
| `snapshots/characters/<contentId>/inventory.json` | Independent location-aware inventory cache. |
| `state/write-recovery.json` | Recovery record for an unfinished collection-file update. |

### 12.1 Atomic Replacement

A file update must not expose a partially written JSON document.

For collection writes:

1. Serialize and validate every output before replacing accepted files.
2. Allocate unique temporary and backup paths in the destination directories.
3. Record each intended replacement before creating its temporary file, so recovery can identify files left by an interrupted write.
4. Write and flush the temporary files to disk.
5. Replace or move each destination, keeping backups until the group is committed.
6. Mark the group committed, then remove temporary files and backups.

If the operation fails, restore the prior files. On startup, inspect the recovery record before new reads/writes. Complete cleanup for a committed group or restore an uncommitted group. If recovery fails, report that failure rather than adopting an uncertain file set.

Validate recovery paths against permitted destinations beneath the configuration directory. Reject path traversal, duplicate destinations, unexpected temporary names, and deletion outside recognized history files.

Inventory uses its own validated temporary-file replacement and does not rewrite collection history.

### 12.2 History and Limits

Keep the newest 25 recognized history files. History names include automatic/manual origin and UTC date/time through milliseconds. Reject a timestamp collision rather than overwriting a history entry.

Create history only when a full expanded result changed meaningfully. Continue updating current compact/full output as appropriate even when no new history file is needed.

| Document | Maximum UTF-8 Size |
| --- | --- |
| Compact snapshot / reported-evidence file | 4 MiB |
| Inventory cache | 4 MiB |
| Expanded snapshot | 16 MiB |
| Recovery record | 64 KiB, at most 64 file operations |
| FFXIV Collect export | 1 MiB |
| Diagnostic report | 256 KiB |

Check size before and during reads where a file can grow. Validate strict UTF-8 and bounded JSON depth. Corrupt files must not become empty observations.

## 13. CollectionData IPC

### 13.1 Versions and Functions

A contract version is independent of the plugin's release number.

| Contract | Prefix | Coverage |
| --- | --- | --- |
| 1.0 | `Exportaramus.CollectionData` | Retained original collection interface. |
| 1.1 | `Exportaramus.CollectionData.V1_1` | Adds facewear and detailed Dresser entries. |
| 1.2 | `Exportaramus.CollectionData.V1_2` | Adds Beasts. |

Each prefix exposes:

| Function/Event | Subscriber Types | Signature |
| --- | --- | --- |
| `GetCapabilities` | `<string>` | No arguments → JSON string |
| `GetSnapshot` | `<string>` | No arguments → JSON string, or no usable payload |
| `SnapshotChanged` | `<string, object?>` | Event carrying one change-notice JSON string |
| `SnapshotWithdrawn` | `<string, object?>` | Event carrying one withdrawal-notice JSON string |
| `ReportOwnershipEvidence` | `<string, uint, string, string>` | Content ID string, UInt32 base item ID, reporter string → result-code string |

Dalamud function generic parameters end with the return type. Event gates use `<string, object?>`, while the callback receives only one string.

Register all functions during startup and remove partial registrations if startup fails. Getters return prepared immutable strings.

### 13.2 Capabilities and Snapshot

Capabilities identify the provider, contract version, session, game build, language, exact gate names, supported sources, schema ID/hash, limits, and report availability.

Compact snapshots contain identity and ordering fields, capture metadata, consistency, unlock ID sets, current presence, reports, source capabilities, and source health. Exact response fields appear in the embedded Collection Snapshot schemas for [1.0](#2112-collection-snapshot), [1.1](#2122-collection-snapshot), and [1.2](#2132-collection-snapshot); the corresponding Capabilities schemas are [1.0](#2114-capabilities), [1.1](#2124-capabilities), and [1.2](#2134-capabilities).

Omit unavailable source contents while retaining their health explanation. A usable empty source is present with an empty set. This distinction lets consumers tell “none owned” from “could not read.”

Preserve older contracts exactly. Remove newer keys from older responses and notifications; do not merely relabel a newer JSON document with an older version.

### 13.3 Notifications

After accepting a publication, combine pending changes and send a notice identifying the newest revision, character, session, changed data types, and sources. Include observed sources when relevant even if their item set did not change.

Consumers reread `GetSnapshot`; the notice is not the snapshot. Send a withdrawal on logout, character change, unusable identity, and provider shutdown. Each contract section in [Section 21](#21-json-schema-reference) includes its exact Change Notice and Withdrawal Notice definitions; use the matching version.

Catch notification dispatch failures so they do not roll back an accepted publication. Dispatch is best-effort: Dalamud cannot guarantee delivery to later subscribers after a blocking or throwing subscriber. Do not promise exactly-once delivery.

### 13.4 Acquisition Reports

Accept only reports for the active character and a known base Item ID. Content ID must be a nonzero decimal string without a leading zero. Normalize the reporter label by trimming and Unicode Form C; require nonempty text, no control characters, and at most 64 UTF-8 bytes.

Store reports by `(itemId, reporter)`. Repeated accepted reports update that record rather than producing unlimited duplicates.

| Limit | Value |
| --- | --- |
| Retained distinct records per character | 10,000 |
| Pending reports overall | 256 |
| Pending reports per reporter | 32 |
| Overall rate | 60 per minute; burst 20 |
| Per-reporter rate | 30 per minute; burst 10 |

| Result Code | Meaning and Caller Response |
| --- | --- |
| `Accepted` | Queued for persistence. Do not report the same acquisition repeatedly to seek stronger confirmation. |
| `InvalidRequest` | Invalid character/item/reporter input, including an unrecognized base Item ID. Correct the input before retrying. |
| `WrongCharacter` | The report identifies a character other than the provider's active character. Discard work prepared for the previous character. |
| `RateLimited` | The overall or reporter acceptance rate was exceeded. Delay further reports; do not retry every frame. |
| `CapacityReached` | The character's distinct evidence records reached the limit. A blind retry cannot free capacity. |
| `QueueFull` | The overall or reporter pending queue is full. Wait before attempting further valid reports. |
| `Unavailable` | No healthy active-character report intake is available. Stop reporting until availability returns. |
| `ProviderStopping` | The provider is shutting down. Stop using the connection and wait for a new one. |

Accepted means queued, not observed by the game or saved to disk. If report persistence fails, disable further acceptance until state is healthy and advertise that status. Reporter labels are not authenticated plugin identities.

### 13.5 Contract Limits

Enforce and advertise: 4 MiB snapshots, 16 KiB capabilities, 4 KiB collection events, 50,000 IDs per collection, and UInt32 maximum presence quantities.

The client and validation requirements are defined in [Sections 13.6–13.8](#136-consumer-kit-client-api); the [Consumer Integration Guide](<Consumer Integration Guide.md>) demonstrates their use. A consumer must validate capabilities before snapshots, subscribe before its initial read, handle reconnects, and reject a different character or obsolete session/revision.

### 13.6 Consumer Kit Client API

The Consumer Kit is optional C# code used inside the consuming plugin. Its namespace is `Exportaramus.ConsumerKit.V1_2`; it connects to CollectionData **1.2**. Inventory **1.0** remains a separate interface described in Section 14. The kit does not read game memory or provide an Inventory client.

`CollectionDataIpcClient` owns one connection and its refresh work. Its `View` is a C# property, not an IPC endpoint or a JSON field. Read it once when preparing a display so all values come from the same accepted result. A successful connection does not make every source complete or current; inspect the snapshot's `SourceHealth` as defined in [Section 4.2](#42-source-health) and the [Common Definitions](#2131-common-definitions).

| Public Member | Parameters, Result, and Behavior |
| --- | --- |
| `CollectionDataIpcClient(Func<Action<string>, Action<string>, ICollectionDataTransport> factory, Func<Action, CancellationToken, Task>? startWorker = null)` | `factory` receives the client's change and withdrawal callbacks and returns a connection that owns both subscriptions. The client calls it initially and again after a lost connection. `startWorker` schedules the supplied refresh action and must return a task covering that action's lifetime; the default uses `Task.Run`. Initial connection failures produce an Unavailable view. |
| `CollectionDataClientView View { get; }` | Returns the immutable current view under a lock. It does not start a read. |
| `void Changed(string payload)` | Receives a change-notice string and requests a refresh. Strings exceeding 4 KiB or containing invalid UTF-16 are ignored. This method treats notices as hints; it does not accept their contents as snapshot data. |
| `void Withdrawn(string payload)` | Queues at most 16 bounded withdrawal strings for validation on the next refresh cycle. Overflow causes the current view to be invalidated. A valid notice clears a view only when its character and provider session match. |
| `Task TickAsync(string? activeContentId, DateTimeOffset now)` | Pass the current character's Content ID obtained independently from Dalamud, or null when logged out. `now` controls discovery timing. Call outside rendering and notification callbacks, and observe the returned task. The method processes identity changes, withdrawals, and pending reads; it also attempts discovery every five seconds without requiring a notification. Only one refresh runs at a time. A call during a refresh returns that task. |
| `string ReportConfirmedAcquisition(string contentId, uint itemId, string reporter)` | Calls `ReportOwnershipEvidence` for an acquisition already confirmed by the caller. The arguments and result codes are those in Section 13.4. No Ready view, no connection, or a character mismatch returns `Unavailable`; disposal returns `ProviderStopping`. Invocation failures and unrecognized result strings become `Unavailable`. This does not modify the game or confirm an unlock. |
| `ValueTask DisposeAsync()` | Invalidates pending results, cancels scheduled work, clears the view, awaits the owned refresh, and removes subscriptions. Await it during shutdown; do not block a thread that a custom worker needs. Repeated calls are harmless. Cancellation is handled; other worker or subscription-cleanup failures can propagate to the caller. |

Each character change increments an internal generation number. Work started for an older generation cannot replace the new character's view. Within one character and provider session, a lower revision is rejected; an equal revision is accepted only when its payload bytes match the previously accepted payload. A new provider session establishes new revision ordering. Validation failure clears displayed data rather than preserving it as current.

`CollectionDataClientView` contains:

| Property | Type | Meaning |
| --- | --- | --- |
| `Availability` | `CollectionDataAvailability` | Client connection/read state below; separate from individual source health. |
| `Snapshot` | `CollectionDataSnapshot?` | Accepted typed snapshot, or null. A Refreshing view can still contain the previous accepted snapshot. |
| `ProviderSessionId` | `string?` | Session of the accepted snapshot, or null. |
| `ContentId` | `string?` | Character of the accepted snapshot, or null. |
| `Revision` | `long` | Accepted publication revision, or zero for an empty view. |
| `Failure` | `CollectionDataValidationCode` | Reason for an unavailable or rejected result; `None` after acceptance. |

`CollectionDataClientView.Empty(CollectionDataAvailability availability, CollectionDataValidationCode failure)` creates a view with the supplied state and failure, null snapshot/session/character, and revision zero.

| Availability | Meaning |
| --- | --- |
| `Unavailable` | No accepted data is currently available, including a missing provider, logout, or withdrawal. |
| `Refreshing` | A read is running. Previously accepted data may remain in the view; do not describe it as newly observed. |
| `Ready` | The client accepted the snapshot. Check source health before interpreting missing IDs or quantities. |
| `Rejected` | The response failed validation or ordering checks. `Snapshot` is null and `Failure` identifies the reason. |
| `Disposed` | The client has shut down; its view is empty and it will not refresh again. |

#### Connection Binding

`ICollectionDataTransport : IDisposable` defines `string ReadCapabilities()`, `string ReadSnapshot()`, and `string Report(string contentId, uint itemId, string reporter)`. These return the provider's strings; they do not validate them. Implementations own their subscriptions and release them in `Dispose()`.

`CollectionDataGateBindings(IDalamudPluginInterface pluginInterface, Action<string> changedHandler, Action<string> withdrawnHandler)` implements that interface using Dalamud. Construction subscribes both callbacks and removes the first subscription if the second fails. `ReadCapabilities()`, `ReadSnapshot()`, and `Report(...)` invoke the corresponding gates and can throw when the provider is unavailable. `Dispose()` attempts to remove both subscriptions; cleanup failure throws `InvalidOperationException`. It does not unregister Exportaramus's functions. The binding source is compiled into the consuming plugin because it depends on Dalamud; the remaining kit library does not.

`CollectionDataGates` supplies string constants `Prefix`, `GetCapabilities`, `GetSnapshot`, `SnapshotChanged`, `SnapshotWithdrawn`, and `ReportOwnershipEvidence`. `Prefix` is `Exportaramus.CollectionData.V1_2`; each other constant appends a dot and its member name. These are gate names, not methods.

### 13.7 Typed Collection Values

The following records are declared in [CollectionDataContracts.cs](../src/Contracts/1.2/CollectionDataContracts.cs). Their C# property names use PascalCase; the corresponding JSON properties use camelCase. `ImmutableArray<T>` and `ImmutableDictionary<string, T>` cannot be changed in place. A nullable value (`?`) is deliberately absent; it must not be converted to an empty collection or zero count.

The tables list every declared property. Exact JSON constraints and allowed strings are in the embedded [Collection Snapshot](#2132-collection-snapshot), [Capabilities](#2134-capabilities), [Change Notice](#2135-change-notice), [Withdrawal Notice](#2136-withdrawal-notice), and [Common Definitions](#2131-common-definitions). Constructing a record yourself does not perform validation.

| Record | Properties and Types |
| --- | --- |
| `CollectionDataSnapshot` | `string SchemaVersion`, `ProviderInternalName`, `ProviderVersion`, `ProviderSessionId`, `ContentId`, `CapturedAtUtc`, `CaptureType`, `Consistency`; `long Revision`, `CaptureGeneration`; `ImmutableDictionary<string, UnlockSet> Unlocks`; `ImmutableDictionary<string, PresenceSource> CurrentItemPresence`; `ReportedOwnership ReportedOwnership`; `ImmutableDictionary<string, SourceCapability> SourceCapabilities`; `ImmutableDictionary<string, SourceHealth> SourceHealth`. |
| `SourceCapability` | `string DataType`, `CapabilityState`, `ReasonCode`, `Reason`; `bool SupportsQuantity`. Describes whether the provider has a supported reader for this source. |
| `SourceHealth` | `string DataType`, `SourceStatus`, `Completeness`, `ProvenanceType`, `CapabilityState`, `ReasonCode`, `Reason`; `long CaptureGeneration`; `string? ObservedAtUtc`. Describes the read result and its age. |
| `UnlockSet` | `ImmutableArray<uint> UnlockedIds`. IDs observed as permanent unlocks; an omitted source is different from an empty set. |
| `PresenceSource` | `ImmutableArray<PresenceItem> Items`; optional init-only `ImmutableArray<DresserEntry>? DresserEntries`. Records observed item membership; the optional entries retain Dresser storage details. |
| `PresenceItem` | `uint ItemId`; `uint? Quantity`. A null quantity means the source records membership without a count. |
| `DresserEntry` | `int EntryIndex`; `uint RawItemId`, `BaseItemId`; `uint? OutfitId`; `ImmutableArray<DresserEntryComponent> Components`. Preserves the entry and any outfit components rather than inventing separate storage slots. |
| `DresserEntryComponent` | `int SlotIndex`; `uint ItemId`. One component of the containing Dresser entry. |
| `ReportedOwnership` | `ImmutableArray<ReportedEvidence> Items`. Reports submitted by consumers, separate from game observations. |
| `ReportedEvidence` | `uint ItemId`; `string ReportedAtUtc`, `Reporter`. Acquisition evidence, not proof that an item is still held or has been consumed to unlock a collection entry. |
| `CollectionDataCapabilities` | `string SchemaVersion`, `ProviderInternalName`, `ProviderVersion`, `ProviderSessionId`, `ClientLanguage`, `GameDataVersion`; `GateNames Gates`; `ImmutableArray<string> UnlockKeys`, `CurrentPresenceSources`; `ImmutableDictionary<string, SourceCapability> SourceCapabilities`; `SchemaIdentity CollectionDataSchema`; `OperationalLimits Limits`; `ReportContract ReportedOwnershipEvidence`. |
| `GateNames` | `string GetCapabilities`, `GetSnapshot`, `SnapshotChanged`, `SnapshotWithdrawn`, `ReportOwnershipEvidence`. The advertised complete gate names. |
| `SchemaIdentity` | `string Id`, `Sha256`. Schema identifier and lowercase SHA-256 of its exact file bytes. |
| `OperationalLimits` | `long SnapshotUtf8Bytes`, `QuantityMaximum`; `int CapabilitiesUtf8Bytes`, `EventUtf8Bytes`, `IdsPerCollection`, `ReportedEvidenceRecordsPerCharacter`, `ReporterUtf8Bytes`, `PendingReportsTotal`, `PendingReportsPerReporter`; `ReportRate GlobalReportRate`, `ReporterReportRate`. |
| `ReportRate` | `int AcceptedPerMinute`, `Burst`. Sustained acceptance rate and maximum immediate burst. |
| `ReportContract` | `bool Available`; `ImmutableArray<string> ResultCodes`. Whether reports are currently accepted and the recognized result strings. |
| `CollectionDataChangedNotice` | `string EventType`, `SchemaVersion`, `ProviderSessionId`, `ContentId`, `CapturedAtUtc`; `long Revision`; `ImmutableArray<string> ChangedDataTypes`, `ChangedSources`. Identifies a publication to reread; it contains no collection contents. |
| `CollectionDataWithdrawnNotice` | `string EventType`, `SchemaVersion`, `ProviderSessionId`, `ContentId`, `WithdrawnAtUtc`, `ReasonCode`; `string? Explanation`. Identifies the character/session whose data is no longer available. |
| `RichSnapshotDocument` | `JsonElement Root`. Validated Expanded Snapshot root. The type name remains `RichSnapshotDocument`; its format is described in Section 15. |
| `ValidatedJsonDocument` | `JsonElement Root`. Validated FFXIV Collect import root. |

Timestamps in these records remain strings in the contract's UTC format. Validators enforce their form before the caller receives a successful result. The two root-document wrappers returned by the validators own cloned JSON elements: callers do not need to keep a `JsonDocument` alive or dispose the wrappers.

### 13.8 Validation API

`CollectionDataSemanticValidator` is a static class. Use its complete string-payload validators before accepting data from a provider or file. They combine bounded JSON parsing, the embedded schema, and checks that relate multiple fields; deserialization alone does not establish validity. These methods validate CollectionData 1.2, not Inventory 1.0.

Each complete validator returns `CollectionDataValidationResult<T>` with `bool Success`, `T? Value`, `CollectionDataValidationCode Code`, and `string Detail`. Inspect `Success` before using `Value`. Successful results contain a value, `Code = None`, and empty detail. Failures contain null/default `Value`; `Detail` is the code name, not a field-level error report. The static factories are `Valid(T value)` and `Invalid(CollectionDataValidationCode code)`; they construct results without validating the supplied value.

| Static Method | Result Type `T` | Checks and Limits |
| --- | --- | --- |
| `ValidateSnapshot(string? payload, string expectedContentId)` | `CollectionDataSnapshot` | 4 MiB; schema, identity, source maps, and report semantics. Supply the expected character independently from Dalamud. Provider session/revision comparisons across reads remain the client's responsibility. |
| `ValidateCapabilities(string? payload)` | `CollectionDataCapabilities` | 16 KiB; schema, exact CollectionData schema hash, advertised keys, capabilities, and report limits. |
| `ValidateChanged(string? payload)` | `CollectionDataChangedNotice` | 4 KiB; schema, valid character ID, ordering of changed-key arrays, and consistency between changed types and sources. |
| `ValidateWithdrawn(string? payload)` | `CollectionDataWithdrawnNotice` | 4 KiB; schema and valid character ID. The caller must compare its character/session with the accepted view. |
| `ValidateRich(string? payload)` | `RichSnapshotDocument` | 16 MiB; Expanded Snapshot schema and relationships between its collection, omission, source-health, and item fields. |
| `ValidateFFXIVCollect(string? payload)` | `ValidatedJsonDocument` | 1 MiB; import schema, recognized catalog IDs, and canonical collection/ID ordering. This unversioned import is not a CollectionData snapshot. |
| `ParseDocument(string? payload, int maximumBytes, bool versioned)` | `JsonDocument` | Lower-level parser with caller-supplied UTF-8 byte limit, depth 64, duplicate-property rejection, and no comments/trailing commas. When `versioned` is true, requires `schemaVersion` exactly `"1.2"`. It does not apply a contract schema or semantic checks. The caller owns and must dispose the returned document. |

The additional overload `bool ValidateRich(JsonElement root)` checks Expanded Snapshot field relationships only. It assumes the root already passed parsing and schema validation and can throw for missing or wrongly typed properties. Prefer the complete string overload for external input.

The class exposes immutable `UnlockKeys`, `PresenceKeys`, `RichKeys`, and `ReportCodes` arrays, plus string `CompactSchemaHash`. Unlock keys and presence keys correspond to the 1.2 schema; `RichKeys` adds `armoires`, `glamour_dresser`, `outfits`, and `relics` to the unlock keys. These are recognized key names, not a catalog of every game entry.

| Validation Code | Meaning and Caller Response |
| --- | --- |
| `None` | Validation succeeded. Apply the source-health rules before interpreting data. |
| `NoPayload` | The string was null/empty, or the client had no active character. Clear current results and wait for availability. |
| `PayloadTooLarge` | The applicable UTF-8 limit was exceeded. Reject the response; do not increase the limit to accept it. |
| `InvalidJson` | Malformed JSON, invalid encoding, duplicate properties, or a parser limit violation. Reject the response. |
| `MissingSchemaVersion` | No string `schemaVersion` was supplied where required. Reject the response. |
| `MalformedSchemaVersion` | The version was not a numeric major/minor pair. Reject the response. |
| `UnsupportedSchemaVersion` | The version was well formed but was not `1.2`. Use a kit for the advertised contract rather than relabeling the data. |
| `ContractBindingFailed` | The document failed its schema, could not be converted to the declared C# types, or required validation resources could not be used. Reject it and check the provider/kit pairing. |
| `SemanticValidationFailed` | Individual fields parsed, but their relationships, ordering, or allowed IDs failed checks. Reject the response. |
| `SchemaHashMismatch` | The provider's advertised collection schema hash differs from the kit's embedded schema. Stop accepting its snapshots until the pair is compatible. |
| `InvalidContentId` | The snapshot's character identifier is not valid. Reject the response. |
| `WrongCharacter` | The snapshot identifies a different character from `expectedContentId`. Clear current results. |
| `WrongProviderSession` | Capabilities and snapshot identify different provider sessions. Wait for a consistent refresh. |
| `LowerRevision` | A snapshot is older than the accepted revision for the same character/session. Reject it. |
| `EqualRevisionConflict` | The same character/session/revision returned different payload bytes. Reject it. |
| `ProviderUnavailable` | Connection creation or invocation failed. Clear current results and allow later discovery. |
| `Disposed` | The client has been disposed. Create a new client only if a new consumer lifetime begins. |

#### Schema and Serialization Helpers

`ContractSchemaRegistry.Default` is the shared registry loaded from the kit's embedded schemas. `new ContractSchemaRegistry()` loads the same resources. `new ContractSchemaRegistry(IEnumerable<byte[]> resources)` accepts schema-file byte arrays for a custom registry; malformed schemas, duplicate `$id` values, or a missing collection schema cause construction to fail. `string CompactSchemaHash { get; }` reports the collection schema's exact-byte hash. `bool Validate(JsonElement value, string fileName)` checks the named schema, resolving references within the registry. A missing or ambiguous file name throws; false means the value failed schema checks. This helper does not replace the complete validators' semantic or identity checks.

`CollectionDataJsonContext` derives from `JsonSerializerContext` and supplies source-generated metadata for `CollectionDataSnapshot`, `CollectionDataCapabilities`, `CollectionDataChangedNotice`, and `CollectionDataWithdrawnNotice`. It uses camelCase, case-sensitive property matching, and omits nulls when writing. Its `Default` instance and generated type metadata support serialization; they do not validate an incoming response.

## 14. Inventory IPC

Inventory is an additive contract, separate from `Exportaramus.CollectionData`. It does not change the schemas of collection snapshots or FFXIV Collect exports. Its schema version is `1.0`, and its scope is the current character; it provides no account-wide or cross-character totals.

### 14.1 Functions and Events

Use prefix `Exportaramus.Inventory.V1.` with each name below. The subscriber types are the generic arguments to `GetIpcSubscriber`; function types end with the return type, while each event callback receives one JSON string.

| Function/Event | Subscriber Types | Signature and Result |
| --- | --- | --- |
| `GetCapabilities` | `<string>` | No arguments → JSON contract capabilities. |
| `GetSnapshot` | `<string>` | No arguments → full JSON snapshot of the current character's last-observed locations and items. |
| `GetItem` | `<uint, string>` | UInt32 base item ID → JSON totals and matching locations. |
| `SnapshotChanged` | `<string, object?>` | One string containing the new full snapshot; requery as needed. |
| `SnapshotWithdrawn` | `<string, object?>` | One string containing an unavailable snapshot; clear displayed character data. |

Inventory events contain snapshots, unlike CollectionData's notices. The payload fields are defined in [Section 14.3](#143-full-snapshot-fields); item-query fields are in [Section 14.4](#144-item-query-fields). Calls inspect immutable managed data and never invoke game UI or disk reads. Prepare snapshot JSON when data changes; item queries filter the bounded managed snapshot.

Example:

```csharp
pluginInterface.GetIpcSubscriber<uint, string>("Exportaramus.Inventory.V1.GetItem").InvokeFunc(itemId)
```

Supply a base item ID, not an HQ-encoded ID. Item ID zero returns `{"schemaVersion":"1.0","error":"InvalidItemId"}`. A valid ID with no observations is not proof of absence.

### 14.2 Capabilities

| Field | Value |
| --- | --- |
| `schemaVersion` | `"1.0"` |
| `scope` | `"CurrentCharacter"` |
| `quantityQuality` | `["Normal", "HighQuality"]` |
| `retainedIsCurrent` | `false` |

This interface does not advertise a collection schema hash, operational limits, or provider session ID. Its location and cache limits are specified in Section 10; consumers must apply their own bounded parsing and validation.

### 14.3 Full Snapshot Fields

| Field | Type and Meaning |
| --- | --- |
| `schemaVersion` | String `"1.0"`. |
| `available` | Boolean indicating whether the provider has an active character snapshot; it does not guarantee every location has been observed. |
| `contentId` | Current character's ID as a decimal string, or null when unavailable. |
| `revision` | Signed 64-bit in-memory publication number; zero when unavailable. |
| `retainerRosterKnown` | Boolean indicating whether the current character's retainer roster is confirmed; false when unavailable. |
| `locations` | Array of location records below, or null when unavailable. |

Each location has:

| Field | Type and Meaning |
| --- | --- |
| `kind` | Location-kind string from Section 10.1. |
| `retainerId` | Decimal string; `"0"` for a non-retainer location. |
| `name` | Display name, subject to Section 10.1's name limits. |
| `state` | `"Unknown"`, `"Observed"`, `"Retained"`, or `"ReadFailed"`, as defined in Section 10.2. |
| `observedAtUtc` | Successful observation's UTC timestamp, or null if never successfully observed. |
| `attemptedAtUtc` | Recorded read attempt's UTC timestamp, or null if no attempt has been recorded. A failed attempt does not advance `observedAtUtc`. |
| `supportsQuantity` | Boolean; false for Armoire and GlamourDresser membership-only locations. |
| `items` | Array of normalized item records; an empty array proves emptiness only after a successful complete observation. |

Each item has `itemId` (UInt32 base item ID), `normalQuantity`, and `highQualityQuantity`. Each quantity is a UInt32 value for quantified locations and null for Armoire or GlamourDresser. Membership does not invent a count. Section 10.1 defines valid IDs, normalization, and per-location quantity limits.

### 14.4 Item Query Fields

An item response contains `schemaVersion`, `available`, `contentId`, and `revision` with the same meanings as the full snapshot, plus:

| Field | Type and Meaning |
| --- | --- |
| `itemId` | Requested UInt32 base item ID. |
| `normalQuantity` | UInt64 total of normal-quality quantities from matching quantified locations. |
| `highQualityQuantity` | UInt64 total of high-quality quantities from matching quantified locations. |
| `quantityMeaning` | String `"LastObservedQuantifiedLocationsOnly"`. |
| `hasUnknownLocations` | Boolean; true if the snapshot is unavailable, the roster is unknown, or any location lacks a successful observation. |
| `hasRetainedLocations` | Boolean; true if any observed location is not in state Observed, even if that location does not contain the requested item. |
| `locations` | Array of matching locations that have a successful observation. |

Each match contains `kind`, `retainerId`, `name`, `state`, `observedAtUtc`, `normalQuantity`, and `highQualityQuantity`. These identify the location and its observation age. Quantities are UInt32 values for quantified locations and null for membership-only locations. Include membership matches in `locations`, but exclude them from both totals. Item matches do not include the full snapshot's `attemptedAtUtc`, `supportsQuantity`, or nested `items` fields.

An unavailable item response has null `contentId`, revision zero, zero totals, `hasUnknownLocations: true`, `hasRetainedLocations: false`, and an empty `locations` array. This differs from the full snapshot's null `locations`; neither unavailable response establishes absence.

No grand total is guaranteed live: totals can combine older observations from different times. Consumer copy must say “last observed” and clearly identify missing and retained storage. Apply the state and replacement rules in Section 10.2; an unread retainer is not an empty retainer.

### 14.5 Consumer Lifetime and Safety

Subscribe and unsubscribe with the consumer plugin's lifetime. Handle absent gates, malformed JSON, and unsupported contract versions. Clear Inventory data on withdrawal, logout, character change, provider unload, or another known disconnect.

An unavailable IPC function raises an invocation exception; it does not return a special error JSON document. Catch invocation failures at the consumer boundary and clear that connection's current data. A valid JSON response with `available: false` is a different case: the provider is reachable but has no active character snapshot. The explicit `InvalidItemId` object in Section 14.1 is a third case.

Before accepting a response, bound its UTF-8 size and JSON depth; require the documented types, version, location kinds, state values, IDs, and quantity limits. Compare `contentId` with the character obtained independently from Dalamud, and compare an item response's `itemId` with the requested base ID. Reject duplicate location identities, inconsistent totals, or quantities on membership-only locations. If combining a full snapshot with an item query, require their character and revision to match. No Inventory event, snapshot, or query validates a response on the consumer's behalf. The bounded item-reader example uses a 4 MiB input limit, depth 16, and at most 17 locations; those are consumer parsing limits, not extra capability fields.

Reestablish ordering after a revision regression. Because Inventory has no session identifier, a reload missed between polls cannot always be detected from revision alone. Keep this limitation explicit.

Never use retained ownership to authorize purchases, discards, or other item actions. The per-character inventory cache and retained restore behavior are specified in Section 10.4. See the [Consumer Integration Guide](<Consumer Integration Guide.md>) for identity, ordering, and lifecycle handling.

## 15. Expanded Snapshot Files

An Expanded Snapshot is a saved document for external applications that need names, collection entries, ownership reasons, quests, and source health. Its structure differs from compact IPC JSON; it is not a strict superset of that format. Website-import JSON is a third, separate format.

The name replaces “rich snapshot” in this specification. Existing file names, the `exportaramus-snapshot` format identifier, schema versions, and Consumer Kit APIs such as `ValidateRich` and `RichSnapshotDocument` stay the same. Only the schema titles change; existing snapshot files remain valid.

Include:

- format and schema identifiers, plugin version, export/capture times, and consistency;
- catalog language and game-data version;
- the character's exact display name;
- usable collections with labels, known-total status, item IDs/names, and ownership;
- the facts and rule used to establish each ownership result;
- quest status and accepted-quest sequence;
- current item presence and physical Dresser entries where the contract supports them; and
- explicit reasons for omitted collections.

A listed catalog item is not automatically owned. Only accepted observations and defined derived rules can produce `owned: true`.

Keep the retained expanded 1.0 file compatible with its original schemas. Expanded 1.2 includes facewear and Beasts. Frozen expanded projections exclude outfit IDs 52601 and 52602 because those records are not recognized by the retained expanded contracts; observed physical presence and reviewed website output may still include their items.

An external file consumer should validate a complete replacement before adopting it, bound read size/retries, and retain a previous valid file only as a labelled historical view.

Schemas:

- [Expanded 1.0 Embedded Definition](#2113-expanded-snapshot) and [Canonical File](../data/Contracts/1.0/schemas/exportaramus-snapshot.schema.json).
- [Expanded 1.2 Embedded Definition](#2133-expanded-snapshot) and [Canonical File](../data/Contracts/1.2/schemas/exportaramus-snapshot.schema.json).

## 16. FFXIV Collect Export

Prepare clipboard JSON from accepted collection data and reviewed destination mappings. Do not upload it or change a website account.

The supported destination keys are `armoires`, `bardings`, `cards`, `fashions`, `field_records`, `frames`, `hairstyles`, `leves`, `npcs`, `occult_records`, `orchestrions`, `outfits`, `relics`, `spells`, `survey_records`, and `beasts`.

Require a reviewed game build and complete recognized-ID coverage for a collection. Omit an unsupported or incomplete collection rather than filtering it into a misleading partial replacement.

Use complete usable collection observations. Reject ownership evidence from stale sources except Armoire and Glamour Dresser, whose retained membership is permitted by the current export rules. Beasts consequently requires current complete observed ownership.

Output a nonempty JSON object with sorted, distinct recognized IDs. The output contains no character identity and no expanded-snapshot metadata.

| Included Content | Website Effect |
| --- | --- |
| Key with IDs | Replace that collection's progress. |
| Key with an empty array | Clear that collection's progress. |
| Omitted key | Leave that collection unchanged. |

An empty array is valid only after a complete empty observation. Never substitute an empty array for a failed read.

Prepare the export and included/omitted-category information before Copy JSON. If no category qualifies, the mapping is invalid, the build is unsupported, or output exceeds 1 MiB, make export unavailable with a specific reason.

Use the [reviewed mapping resources in Section 5.3](#53-reviewed-mapping-resources) for recognized destination IDs and item relationships. The complete import shape, including Beasts, is embedded in [Section 21.4.2](#2142-import-contract-including-beasts). Do not merge old website JSON into a new export.

## 17. Diagnostics and Configuration

### 17.1 Commands

`/exportaramus` opens the main window.

`/exportaramus diagnostics` opens diagnostics.

Keep command help on separate lines. Dalamud already displays the registered primary command, so its help text begins with “Open Exportaramus.”

### 17.2 Diagnostics

Diagnostics provides Generate Report, Copy Report, Reset Measurements, and close behavior. Generation is explicit and read-only. It must not trigger a collection capture, game interaction, or network request.

Capture small framework-owned metadata first, then build the report from immutable values in one background worker. Repeated Generate requests do not create overlapping workers. Closing cancels that request; late completion cannot replace a newer report.

Include build/dependency information, reader compatibility, source health, controller state, queue sizes, output sizes, storage status, IPC registration state, task ownership, timings, and bounded redacted failures.

Use at most 64 metric names, 256 samples per metric, 20 recent failures, 64 source-health entries, and 32 owned-task entries. Calculate percentile summaries outside the recording lock.

Reports contain no character name or ID, world, item IDs/quantities, retainer IDs, raw inventory, absolute personal paths, environment values, command line, clipboard content, tokens, or secrets. Use allowlisted fields and safe diagnostic labels, including `source.` prefixes for source fingerprints.

Validate the typed report, its strings, final JSON, and its 256 KiB limit. Calculate a stable report hash excluding generation time and the hash field itself. Ready means no incomplete sections; Partial requires at least one incomplete section. A failure leaves an explicit error and permits a new request.

Generate diagnostic format `1.1`. Use `expanded` for the Expanded Snapshot entries in `collectionPipeline.serializers` and `collectionPipeline.outputBytes`; these record serialization status and encoded byte count, respectively.

Only diagnostic format `1.1` is supported. Reject retired format `1.0`, unsupported versions, and the old `compendium` field name. Generate a new report when current diagnostics are needed; do not migrate an old report. Diagnostic versioning is independent of the collection, inventory, and Expanded Snapshot contracts.

The exact report fields are defined by the [diagnostic report 1.1 schema](../data/Diagnostics/1.1/exportaramus-diagnostics-v1.1.schema.json), embedded in [Section 21.5.1](#2151-diagnostic-report).

### 17.3 Configuration and Theme

Persist configuration format version 1 with Reduce Motion. Unsupported configuration versions use the default false value rather than applying unknown settings. Retain the latest unsaved preference after a write failure and retry after 30 seconds; newer preference changes replace older pending values. Shutdown makes one final save attempt for the latest pending preference.

Observe the game's theme approximately once per second on the framework thread. A failed read retains the last accepted palette; an unknown theme ID uses Dark. Never write game theme settings.

The frontend owns visual palette values, contrast, layout, and animation behavior. The backend supplies the selected theme and preference without mixing them into collection fingerprints.

## 18. Failure Handling

| Failure | Required Result |
| --- | --- |
| Unsupported game build | Mark affected readers unavailable pending review; preserve explanatory health. |
| Window or cache not ready | Keep unknown or eligible same-character stale data; retry through normal scheduling. |
| Character changes during work | Cancel/invalidate the old operation; reject its completion. |
| Missing/invalid mapping | Make the affected source or export unavailable. |
| Quantity or size overflow | Reject the complete affected result; never truncate into valid-looking data. |
| Serializer validation fails | Retain the previous accepted publication and report failure. |
| File write fails | Recover prior accepted files where possible; do not publish the failed candidate. |
| Report persistence fails | Stop accepting reports until healthy state is restored. |
| Guided action times out | Stop or pause with progress retained; do not continue with guessed state. |
| IPC subscriber throws | Record or contain dispatch failure; do not undo publication. |
| Diagnostic generation fails | Show failure and allow another request. |
| Unload during work | Stop new work and complete owned cleanup before releasing services. |

Use structured failure codes plus short explanations. A reader being unavailable does not imply that the player has not unlocked the feature.

## 19. Verification Requirements

### 19.1 Offline Tests

Use synthetic characters, item sets, clocks, storage faults, and native-adapter responses. Test:

- all collection ID mappings and filters;
- complete empty, partial, pending, failed, stale, and restored sources;
- outfit/relic unknown handling and supporting evidence;
- Dresser composition, duplicates, outfit slots, and aggregate membership;
- source-only, health-only, and timestamp-only changes;
- capture priority, retry intervals, cancellation, and identity gaps;
- delayed results after a character or session change;
- each IPC version, schema identity, size limit, and report result;
- normal/HQ quantities, null membership, unknown roster, dismissed/renamed/new retainers;
- write failure and recovery at each replacement step;
- bounded queues, notification bursts, shutdown, and repeated disposal;
- diagnostic redaction, partial reports, hash calculation, and output bounds; and
- frontend prepared values for counts, lower bounds, health, history, and retainer coverage.

Validate emitted documents against their matching schemas and meaningful-data rules. Test a generic consumer that connects before/after the provider, receives updates, survives withdrawal/reload, and releases its subscriptions.

### 19.2 In-Game Tests

Confirm native reads and interactions separately from offline tests:

- compare known unlocked and locked entries with the game;
- test Field Records before and after cache readiness;
- test Dresser ordinary items and stored outfits;
- test Armoire, saddlebag availability, and retained observations;
- test retainer rosters with different employed counts;
- verify dialogue advances only for the selected retainer;
- verify retainer inventory, overview, and Entrust context close correctly;
- cancel or interrupt guided capture at each meaningful stage;
- switch characters and zone while work is active; and
- unload/reload the plugin and confirm no callbacks or tasks remain.

A successful build, test run, or package inspection does not establish in-game correctness. Record built, tested, packaged, loaded, and live-tested states separately.

Routine changes require a successful build and relevant regression tests. Exhaustive configured coverage and mutation review belong to pre-release qualification. Keep those results in separate evidence records rather than appending them to this specification.

## 20. Suggested Implementation Order

Use these milestones with the detailed requirements above. Completion checks describe evidence to obtain, not additional product behavior.

| Step | Implement | Completion Check |
| --- | --- | --- |
| 1 | Plugin entry point, command registration, configuration, and orderly shutdown. | Initialization failure removes prior registrations; repeated disposal leaves no owned work or subscriptions. |
| 2 | Plain data model, health rules, identity checks, and fake-reader tests. | Distinguish complete-empty, failed, retained, restored, reported, and derived information; reject results for the wrong character or generation. |
| 3 | Game catalog and ordinary unlock readers. | The mount path in Section 5.4 preserves IDs/names and separates definitions from ownership; catalog tests cover filters, transformed IDs, and version/language changes. |
| 4 | Window-dependent readers and readiness behavior. | Unloaded windows/managers remain unknown or eligible for stale retention; a complete empty read replaces earlier contents. |
| 5 | Scheduling, publication, hashing, and local storage recovery. | Obsolete workers cannot publish; timestamp-only reads do not create duplicate history; injected write failures restore the prior accepted file set. |
| 6 | All three CollectionData contracts and generic consumer tests. | Validate each version against its own embedded schema and semantic rules; older responses omit newer keys; reconnect and withdrawal clear obsolete data. |
| 7 | Inventory service, native location readers, cache, and Inventory IPC. | Preserve normal/HQ separation, membership nulls, unknown roster coverage, per-retainer times, and delayed-restore protection. |
| 8 | Guided Dresser/Armoire and retainer workflows. | A request is never counted as completion; simulated interruptions preserve completed captures and enforce closure/context checks. |
| 9 | Expanded files, website output, frontend values, and diagnostics. | Keep output formats distinct; satisfy the internal frontend boundary in Section 9.3; verify count nullability, export omissions, and diagnostic redaction. |
| 10 | In-game validation against the reviewed FFXIV/Dalamud build. | Obtain the separate native-read, guided-interaction, character-switch, and unload evidence required by Section 19.2. |

Each step should leave failures explicit. A temporarily unavailable source is an acceptable operating state; fabricated ownership or empty data is not. Apply [Section 19](#19-verification-requirements) throughout, keeping offline checks separate from in-game validation.

## 21. JSON Schema Reference

This appendix contains the complete schemas for the supported collection contracts, expanded snapshots, FFXIV Collect imports and mapping inputs, and diagnostic reports. Each definition links to its canonical file. JSON is expanded for readability without changing its contents.

Identical definitions used by more than one contract are printed once, with all corresponding file links. All referenced common definitions are included below. A schema ID identifies the contract; it is not a URL the plugin needs to download at runtime.

Inventory V1 has no separate canonical JSON Schema file. Its complete response fields and behavior are specified in [Section 14](#14-inventory-ipc), with location, capture, and persistence rules in [Section 10](#10-inventory-integration). The schemas below do not validate Inventory responses.

When changing a contract, update its canonical file and the embedded definition together. Verify that both parse to the same JSON value, and check that every schema reference resolves to an included definition.

| Section | Schemas |
| :--- | :--- |
| [21.1 CollectionData 1.0](#211-collectiondata-10) | Common Definitions, Collection Snapshot, Expanded Snapshot, Capabilities, Change Notice, Withdrawal Notice. |
| [21.2 CollectionData 1.1](#212-collectiondata-11) | Common Definitions, Collection Snapshot, Expanded Snapshot, Capabilities, Change Notice, Withdrawal Notice. |
| [21.3 CollectionData 1.2](#213-collectiondata-12) | Common Definitions, Collection Snapshot, Expanded Snapshot, Capabilities, Change Notice, Withdrawal Notice. |
| [21.4 FFXIV Collect](#214-ffxiv-collect) | Original Import Contract, Import Contract Including Beasts, Outfit and Relic Mapping Inputs. |
| [21.5 Diagnostics](#215-diagnostics) | Diagnostic Report. |

### 21.1 CollectionData 1.0

#### 21.1.1 Common Definitions

Canonical file: [data/Contracts/1.0/schemas/common.schema.json](../data/Contracts/1.0/schemas/common.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.0/common.schema.json",
  "title": "Exportaramus Common Contract Types 1.0",
  "$defs": {
    "schemaVersion": {
      "type": "string",
      "const": "1.0"
    },
    "providerVersion": {
      "type": "string",
      "minLength": 1,
      "maxLength": 64
    },
    "providerSessionId": {
      "type": "string",
      "format": "uuid"
    },
    "contentId": {
      "type": "string",
      "pattern": "^[1-9][0-9]{0,19}$",
      "maxLength": 20
    },
    "utcDateTime": {
      "type": "string",
      "format": "date-time",
      "pattern": "Z$",
      "maxLength": 64
    },
    "revision": {
      "type": "integer",
      "minimum": 1,
      "maximum": 9007199254740991
    },
    "captureGeneration": {
      "type": "integer",
      "minimum": 0,
      "maximum": 9007199254740991
    },
    "positiveId": {
      "type": "integer",
      "minimum": 1,
      "maximum": 4294967295
    },
    "quantity": {
      "type": "integer",
      "minimum": 1,
      "maximum": 4294967295
    },
    "clientLanguage": {
      "type": "string",
      "enum": [
        "Japanese",
        "English",
        "German",
        "French"
      ]
    },
    "captureType": {
      "type": "string",
      "enum": [
        "Full",
        "Lightweight",
        "Restored",
        "Reported"
      ]
    },
    "consistency": {
      "type": "string",
      "enum": [
        "Coherent",
        "MixedObservationTimes"
      ]
    },
    "dataType": {
      "type": "string",
      "enum": [
        "PermanentUnlock",
        "CurrentItemPresence",
        "Derived",
        "ReportedOwnership"
      ]
    },
    "sourceStatus": {
      "type": "string",
      "enum": [
        "Fresh",
        "Stale",
        "Unavailable",
        "NeverObserved"
      ]
    },
    "completeness": {
      "type": "string",
      "enum": [
        "Complete",
        "Partial",
        "Unknown"
      ]
    },
    "provenanceType": {
      "type": "string",
      "enum": [
        "None",
        "Observed",
        "Restored",
        "Reported",
        "Derived"
      ]
    },
    "capabilityState": {
      "type": "string",
      "enum": [
        "Supported",
        "Degraded",
        "UnavailablePendingValidation"
      ]
    },
    "reasonCode": {
      "type": "string",
      "enum": [
        "None",
        "NotLoadedThisSession",
        "WindowNotReady",
        "IncompleteContainerSet",
        "IncompleteRetainerSet",
        "MappingValidationFailed",
        "PatchValidationRequired",
        "RestoredFromDisk",
        "CharacterUnavailable",
        "IdentityUnavailable",
        "QuantityOverflow",
        "CaptureFailed",
        "GameDataVersionUnsupported",
        "OutputSizeLimitExceeded",
        "SerializerValidationFailed"
      ]
    },
    "sourceKey": {
      "type": "string",
      "enum": [
        "mounts",
        "minions",
        "emotes",
        "bardings",
        "cards",
        "npcs",
        "fashions",
        "frames",
        "occult_records",
        "orchestrions",
        "spells",
        "aetherytes",
        "chocobo_routes",
        "leves",
        "hairstyles",
        "field_records",
        "survey_records",
        "quests",
        "achievements",
        "armoires",
        "glamour_dresser",
        "outfits",
        "relics",
        "inventory",
        "armoryChest",
        "chocoboSaddlebag",
        "premiumSaddlebag",
        "retainers",
        "armoire",
        "glamourDresser",
        "reportedOwnership"
      ]
    },
    "sourceCapability": {
      "type": "object",
      "required": [
        "dataType",
        "capabilityState",
        "supportsQuantity",
        "reasonCode",
        "reason"
      ],
      "properties": {
        "dataType": {
          "$ref": "#/$defs/dataType"
        },
        "capabilityState": {
          "$ref": "#/$defs/capabilityState"
        },
        "supportsQuantity": {
          "type": "boolean"
        },
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "reason": {
          "type": "string",
          "maxLength": 160
        }
      },
      "additionalProperties": true
    },
    "sourceHealth": {
      "type": "object",
      "required": [
        "dataType",
        "captureGeneration",
        "sourceStatus",
        "completeness",
        "provenanceType",
        "capabilityState",
        "reasonCode",
        "reason"
      ],
      "properties": {
        "dataType": {
          "$ref": "#/$defs/dataType"
        },
        "observedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "captureGeneration": {
          "$ref": "#/$defs/captureGeneration"
        },
        "sourceStatus": {
          "$ref": "#/$defs/sourceStatus"
        },
        "completeness": {
          "$ref": "#/$defs/completeness"
        },
        "provenanceType": {
          "$ref": "#/$defs/provenanceType"
        },
        "capabilityState": {
          "$ref": "#/$defs/capabilityState"
        },
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "reason": {
          "type": "string",
          "maxLength": 160
        }
      },
      "allOf": [
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "enum": [
                  "Fresh",
                  "Stale"
                ]
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "required": [
              "observedAtUtc"
            ],
            "properties": {
              "provenanceType": {
                "not": {
                  "const": "None"
                }
              }
            }
          }
        },
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "enum": [
                  "Unavailable",
                  "NeverObserved"
                ]
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "not": {
              "required": [
                "observedAtUtc"
              ]
            },
            "properties": {
              "provenanceType": {
                "const": "None"
              }
            }
          }
        }
      ],
      "additionalProperties": true
    },
    "unlockSet": {
      "type": "object",
      "required": [
        "unlockedIds"
      ],
      "properties": {
        "unlockedIds": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/positiveId"
          }
        }
      },
      "additionalProperties": true
    },
    "quantifiedPresenceItem": {
      "type": "object",
      "required": [
        "itemId",
        "quantity"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        },
        "quantity": {
          "$ref": "#/$defs/quantity"
        }
      },
      "additionalProperties": true
    },
    "setPresenceItem": {
      "type": "object",
      "required": [
        "itemId"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        }
      },
      "not": {
        "required": [
          "quantity"
        ]
      },
      "additionalProperties": true
    },
    "quantifiedPresenceSource": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/quantifiedPresenceItem"
          }
        }
      },
      "additionalProperties": true
    },
    "setPresenceSource": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/setPresenceItem"
          }
        }
      },
      "additionalProperties": true
    },
    "reportedEvidence": {
      "type": "object",
      "required": [
        "itemId",
        "reportedAtUtc",
        "reporter"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        },
        "reportedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "reporter": {
          "type": "string",
          "minLength": 1,
          "maxLength": 64
        }
      },
      "additionalProperties": true
    },
    "ownershipFact": {
      "type": "object",
      "required": [
        "dataType",
        "sourceKey",
        "subjectId",
        "state",
        "sourceStatus"
      ],
      "properties": {
        "dataType": {
          "type": "string",
          "enum": [
            "PermanentUnlock",
            "CurrentItemPresence"
          ]
        },
        "sourceKey": {
          "$ref": "#/$defs/sourceKey"
        },
        "subjectId": {
          "$ref": "#/$defs/positiveId"
        },
        "state": {
          "type": "string",
          "enum": [
            "Present",
            "Absent",
            "Unknown"
          ]
        },
        "sourceStatus": {
          "$ref": "#/$defs/sourceStatus"
        }
      },
      "additionalProperties": true
    },
    "ownershipEvidence": {
      "type": "object",
      "required": [
        "ruleCode",
        "facts"
      ],
      "properties": {
        "ruleCode": {
          "type": "string",
          "enum": [
            "DirectPermanentUnlock",
            "DirectCurrentItemPresence",
            "QuestCompletion",
            "AllRequiredConstituents",
            "PermanentUnlockOrCurrentItemPresence"
          ]
        },
        "facts": {
          "type": "array",
          "minItems": 1,
          "maxItems": 64,
          "items": {
            "$ref": "#/$defs/ownershipFact"
          }
        }
      },
      "additionalProperties": true
    },
    "collectionItem": {
      "type": "object",
      "required": [
        "id",
        "name",
        "owned",
        "ownershipEvidence"
      ],
      "properties": {
        "id": {
          "$ref": "#/$defs/positiveId"
        },
        "name": {
          "type": "string",
          "minLength": 1,
          "maxLength": 512
        },
        "owned": {
          "type": "boolean"
        },
        "ownershipEvidence": {
          "$ref": "#/$defs/ownershipEvidence"
        },
        "reportedEvidence": {
          "type": "array",
          "maxItems": 64,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/reportedEvidence"
          }
        },
        "status": {
          "type": "string",
          "enum": [
            "NotStarted",
            "InProgress",
            "Completed"
          ]
        },
        "group": {
          "type": "string",
          "maxLength": 256
        },
        "category": {
          "type": "string",
          "maxLength": 256
        },
        "series": {
          "type": "string",
          "maxLength": 256
        },
        "level": {
          "type": "integer",
          "minimum": 0,
          "maximum": 999
        },
        "location": {
          "type": "string",
          "maxLength": 512
        },
        "expansion": {
          "type": "string",
          "maxLength": 128
        },
        "festival": {
          "type": "boolean"
        },
        "repeatable": {
          "type": "boolean"
        },
        "progress": {
          "type": "object",
          "properties": {
            "current": {
              "type": "integer",
              "minimum": 0,
              "maximum": 2147483647
            },
            "target": {
              "type": "integer",
              "minimum": 1,
              "maximum": 2147483647
            },
            "sequence": {
              "type": "integer",
              "minimum": 0,
              "maximum": 255
            }
          },
          "minProperties": 1,
          "additionalProperties": true
        },
        "description": {
          "type": "string",
          "maxLength": 4096
        },
        "points": {
          "type": "integer",
          "minimum": 0,
          "maximum": 65535
        },
        "rewardCollected": {
          "type": [
            "boolean",
            "null"
          ]
        },
        "rewards": {
          "type": "array",
          "maxItems": 64,
          "items": {
            "type": "object",
            "required": [
              "kind",
              "id"
            ],
            "properties": {
              "kind": {
                "type": "string",
                "minLength": 1,
                "maxLength": 64
              },
              "id": {
                "$ref": "#/$defs/positiveId"
              },
              "quantity": {
                "$ref": "#/$defs/quantity"
              }
            },
            "additionalProperties": true
          }
        }
      },
      "additionalProperties": true
    },
    "collectionResult": {
      "type": "object",
      "required": [
        "label",
        "ownershipKind",
        "totalKnown",
        "sourceStatus",
        "completeness",
        "provenanceType",
        "observedAtUtc",
        "items"
      ],
      "properties": {
        "label": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        },
        "ownershipKind": {
          "type": "string",
          "enum": [
            "PermanentUnlock",
            "CurrentItemPresence",
            "Derived"
          ]
        },
        "totalKnown": {
          "type": "boolean"
        },
        "sourceStatus": {
          "type": "string",
          "enum": [
            "Fresh",
            "Stale"
          ]
        },
        "completeness": {
          "type": "string",
          "const": "Complete"
        },
        "provenanceType": {
          "$ref": "#/$defs/provenanceType"
        },
        "observedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/collectionItem"
          }
        }
      },
      "additionalProperties": true
    },
    "omission": {
      "type": "object",
      "required": [
        "reasonCode",
        "message"
      ],
      "properties": {
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "message": {
          "type": "string",
          "minLength": 1,
          "maxLength": 160
        }
      },
      "additionalProperties": true
    }
  }
}
```

#### 21.1.2 Collection Snapshot

Canonical file: [data/Contracts/1.0/schemas/collection-data.schema.json](../data/Contracts/1.0/schemas/collection-data.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.0/collection-data.schema.json",
  "title": "Exportaramus CollectionData Snapshot 1.0",
  "type": "object",
  "required": [
    "schemaVersion",
    "providerInternalName",
    "providerVersion",
    "providerSessionId",
    "revision",
    "contentId",
    "capturedAtUtc",
    "captureType",
    "captureGeneration",
    "consistency",
    "unlocks",
    "currentItemPresence",
    "reportedOwnership",
    "sourceCapabilities",
    "sourceHealth"
  ],
  "properties": {
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerInternalName": {
      "type": "string",
      "const": "Exportaramus"
    },
    "providerVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "revision": {
      "$ref": "common.schema.json#/$defs/revision"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "captureType": {
      "$ref": "common.schema.json#/$defs/captureType"
    },
    "captureGeneration": {
      "$ref": "common.schema.json#/$defs/captureGeneration"
    },
    "consistency": {
      "$ref": "common.schema.json#/$defs/consistency"
    },
    "unlocks": {
      "type": "object",
      "properties": {
        "mounts": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "minions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "emotes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "bardings": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "cards": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "npcs": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "fashions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "frames": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "occult_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "orchestrions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "spells": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "aetherytes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "chocobo_routes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "leves": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "hairstyles": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "field_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "survey_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "quests": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "achievements": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/unlockSet"
      }
    },
    "currentItemPresence": {
      "type": "object",
      "properties": {
        "inventory": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoryChest": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "chocoboSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "premiumSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "retainers": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoire": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        },
        "glamourDresser": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/setPresenceSource"
      }
    },
    "reportedOwnership": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 10000,
          "uniqueItems": true,
          "items": {
            "$ref": "common.schema.json#/$defs/reportedEvidence"
          }
        }
      },
      "additionalProperties": true
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      }
    },
    "sourceHealth": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceHealth"
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.1.3 Expanded Snapshot

Canonical file: [data/Contracts/1.0/schemas/exportaramus-snapshot.schema.json](../data/Contracts/1.0/schemas/exportaramus-snapshot.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.0/exportaramus-snapshot.schema.json",
  "title": "Exportaramus Expanded Snapshot 1.0",
  "type": "object",
  "required": [
    "format",
    "schemaVersion",
    "exportaramusVersion",
    "exportedAtUtc",
    "capturedAtUtc",
    "captureType",
    "captureGeneration",
    "consistency",
    "catalog",
    "character",
    "collections",
    "currentItemPresence",
    "sourceCapabilities",
    "sourceHealth",
    "omittedCollections"
  ],
  "properties": {
    "format": {
      "type": "string",
      "const": "exportaramus-snapshot"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "exportaramusVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "exportedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "captureType": {
      "$ref": "common.schema.json#/$defs/captureType"
    },
    "captureGeneration": {
      "$ref": "common.schema.json#/$defs/captureGeneration"
    },
    "consistency": {
      "$ref": "common.schema.json#/$defs/consistency"
    },
    "catalog": {
      "type": "object",
      "required": [
        "clientLanguage",
        "gameDataVersion"
      ],
      "properties": {
        "clientLanguage": {
          "$ref": "common.schema.json#/$defs/clientLanguage"
        },
        "gameDataVersion": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        }
      },
      "additionalProperties": true
    },
    "character": {
      "type": "object",
      "required": [
        "displayName"
      ],
      "properties": {
        "displayName": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        }
      },
      "additionalProperties": true
    },
    "collections": {
      "type": "object",
      "properties": {
        "mounts": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "minions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "emotes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "bardings": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "cards": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "npcs": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "fashions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "frames": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "occult_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "orchestrions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "spells": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "aetherytes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "chocobo_routes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "leves": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "hairstyles": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "field_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "survey_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "armoires": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "glamour_dresser": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "outfits": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "relics": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "quests": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "achievements": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/collectionResult"
      }
    },
    "currentItemPresence": {
      "type": "object",
      "properties": {
        "inventory": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoryChest": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "chocoboSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "premiumSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "retainers": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoire": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        },
        "glamourDresser": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/setPresenceSource"
      }
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      }
    },
    "sourceHealth": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceHealth"
      }
    },
    "omittedCollections": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/omission"
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.1.4 Capabilities

Canonical file: [data/Contracts/1.0/schemas/capabilities.schema.json](../data/Contracts/1.0/schemas/capabilities.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.0/capabilities.schema.json",
  "title": "Exportaramus CollectionData Capabilities 1.0",
  "type": "object",
  "required": [
    "schemaVersion",
    "providerInternalName",
    "providerVersion",
    "providerSessionId",
    "clientLanguage",
    "gameDataVersion",
    "gates",
    "unlockKeys",
    "currentPresenceSources",
    "sourceCapabilities",
    "collectionDataSchema",
    "limits",
    "reportedOwnershipEvidence"
  ],
  "properties": {
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerInternalName": {
      "type": "string",
      "const": "Exportaramus"
    },
    "providerVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "clientLanguage": {
      "$ref": "common.schema.json#/$defs/clientLanguage"
    },
    "gameDataVersion": {
      "type": "string",
      "minLength": 1,
      "maxLength": 128
    },
    "gates": {
      "type": "object",
      "required": [
        "getCapabilities",
        "getSnapshot",
        "snapshotChanged",
        "snapshotWithdrawn",
        "reportOwnershipEvidence"
      ],
      "properties": {
        "getCapabilities": {
          "type": "string",
          "const": "Exportaramus.CollectionData.GetCapabilities"
        },
        "getSnapshot": {
          "type": "string",
          "const": "Exportaramus.CollectionData.GetSnapshot"
        },
        "snapshotChanged": {
          "type": "string",
          "const": "Exportaramus.CollectionData.SnapshotChanged"
        },
        "snapshotWithdrawn": {
          "type": "string",
          "const": "Exportaramus.CollectionData.SnapshotWithdrawn"
        },
        "reportOwnershipEvidence": {
          "type": "string",
          "const": "Exportaramus.CollectionData.ReportOwnershipEvidence"
        }
      },
      "additionalProperties": true
    },
    "unlockKeys": {
      "type": "array",
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    },
    "currentPresenceSources": {
      "type": "array",
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      }
    },
    "collectionDataSchema": {
      "type": "object",
      "required": [
        "id",
        "sha256"
      ],
      "properties": {
        "id": {
          "type": "string",
          "const": "https://exportaramus.local/schemas/1.0/collection-data.schema.json"
        },
        "sha256": {
          "type": "string",
          "pattern": "^[0-9a-f]{64}$"
        }
      },
      "additionalProperties": true
    },
    "limits": {
      "type": "object",
      "required": [
        "snapshotUtf8Bytes",
        "capabilitiesUtf8Bytes",
        "eventUtf8Bytes",
        "idsPerCollection",
        "reportedEvidenceRecordsPerCharacter",
        "reporterUtf8Bytes",
        "pendingReportsTotal",
        "pendingReportsPerReporter",
        "quantityMaximum",
        "globalReportRate",
        "reporterReportRate"
      ],
      "properties": {
        "snapshotUtf8Bytes": {
          "type": "integer",
          "const": 4194304
        },
        "capabilitiesUtf8Bytes": {
          "type": "integer",
          "const": 16384
        },
        "eventUtf8Bytes": {
          "type": "integer",
          "const": 4096
        },
        "idsPerCollection": {
          "type": "integer",
          "const": 50000
        },
        "reportedEvidenceRecordsPerCharacter": {
          "type": "integer",
          "const": 10000
        },
        "reporterUtf8Bytes": {
          "type": "integer",
          "const": 64
        },
        "pendingReportsTotal": {
          "type": "integer",
          "const": 256
        },
        "pendingReportsPerReporter": {
          "type": "integer",
          "const": 32
        },
        "quantityMaximum": {
          "type": "integer",
          "const": 4294967295
        },
        "globalReportRate": {
          "$ref": "#/$defs/rateLimit"
        },
        "reporterReportRate": {
          "$ref": "#/$defs/rateLimit"
        }
      },
      "additionalProperties": true
    },
    "reportedOwnershipEvidence": {
      "type": "object",
      "required": [
        "available",
        "resultCodes"
      ],
      "properties": {
        "available": {
          "type": "boolean"
        },
        "resultCodes": {
          "type": "array",
          "minItems": 8,
          "maxItems": 8,
          "uniqueItems": true,
          "items": {
            "type": "string",
            "enum": [
              "Accepted",
              "InvalidRequest",
              "WrongCharacter",
              "RateLimited",
              "CapacityReached",
              "QueueFull",
              "Unavailable",
              "ProviderStopping"
            ]
          }
        }
      },
      "additionalProperties": true
    }
  },
  "$defs": {
    "rateLimit": {
      "type": "object",
      "required": [
        "acceptedPerMinute",
        "burst"
      ],
      "properties": {
        "acceptedPerMinute": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10000
        },
        "burst": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10000
        }
      },
      "additionalProperties": true
    }
  },
  "additionalProperties": true
}
```

#### 21.1.5 Change Notice

Canonical file: [data/Contracts/1.0/schemas/snapshot-changed.schema.json](../data/Contracts/1.0/schemas/snapshot-changed.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.0/snapshot-changed.schema.json",
  "title": "Exportaramus CollectionData SnapshotChanged Notice 1.0",
  "type": "object",
  "required": [
    "eventType",
    "schemaVersion",
    "providerSessionId",
    "revision",
    "contentId",
    "capturedAtUtc",
    "changedDataTypes",
    "changedSources"
  ],
  "properties": {
    "eventType": {
      "type": "string",
      "const": "SnapshotChanged"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "revision": {
      "$ref": "common.schema.json#/$defs/revision"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "changedDataTypes": {
      "type": "array",
      "minItems": 1,
      "maxItems": 5,
      "uniqueItems": true,
      "items": {
        "type": "string",
        "enum": [
          "Unlocks",
          "CurrentItemPresence",
          "ReportedOwnership",
          "SourceCapabilities",
          "SourceHealth"
        ]
      }
    },
    "changedSources": {
      "type": "array",
      "minItems": 1,
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.1.6 Withdrawal Notice

Canonical file: [data/Contracts/1.0/schemas/snapshot-withdrawn.schema.json](../data/Contracts/1.0/schemas/snapshot-withdrawn.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.0/snapshot-withdrawn.schema.json",
  "title": "Exportaramus CollectionData SnapshotWithdrawn Notice 1.0",
  "type": "object",
  "required": [
    "eventType",
    "schemaVersion",
    "providerSessionId",
    "contentId",
    "withdrawnAtUtc",
    "reasonCode"
  ],
  "properties": {
    "eventType": {
      "type": "string",
      "const": "SnapshotWithdrawn"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "withdrawnAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "reasonCode": {
      "type": "string",
      "enum": [
        "LoggedOut",
        "CharacterChanged",
        "IdentityUnavailable",
        "ProviderStopping"
      ]
    },
    "explanation": {
      "type": "string",
      "maxLength": 160
    }
  },
  "additionalProperties": true
}
```

### 21.2 CollectionData 1.1

#### 21.2.1 Common Definitions

Canonical file: [data/Contracts/1.1/schemas/common.schema.json](../data/Contracts/1.1/schemas/common.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.1/common.schema.json",
  "title": "Exportaramus Common Contract Types 1.0",
  "$defs": {
    "schemaVersion": {
      "type": "string",
      "const": "1.1"
    },
    "providerVersion": {
      "type": "string",
      "minLength": 1,
      "maxLength": 64
    },
    "providerSessionId": {
      "type": "string",
      "format": "uuid"
    },
    "contentId": {
      "type": "string",
      "pattern": "^[1-9][0-9]{0,19}$",
      "maxLength": 20
    },
    "utcDateTime": {
      "type": "string",
      "format": "date-time",
      "pattern": "Z$",
      "maxLength": 64
    },
    "revision": {
      "type": "integer",
      "minimum": 1,
      "maximum": 9007199254740991
    },
    "captureGeneration": {
      "type": "integer",
      "minimum": 0,
      "maximum": 9007199254740991
    },
    "positiveId": {
      "type": "integer",
      "minimum": 1,
      "maximum": 4294967295
    },
    "quantity": {
      "type": "integer",
      "minimum": 1,
      "maximum": 4294967295
    },
    "clientLanguage": {
      "type": "string",
      "enum": [
        "Japanese",
        "English",
        "German",
        "French"
      ]
    },
    "captureType": {
      "type": "string",
      "enum": [
        "Full",
        "Lightweight",
        "Restored",
        "Reported"
      ]
    },
    "consistency": {
      "type": "string",
      "enum": [
        "Coherent",
        "MixedObservationTimes"
      ]
    },
    "dataType": {
      "type": "string",
      "enum": [
        "PermanentUnlock",
        "CurrentItemPresence",
        "Derived",
        "ReportedOwnership"
      ]
    },
    "sourceStatus": {
      "type": "string",
      "enum": [
        "Fresh",
        "Stale",
        "Unavailable",
        "NeverObserved"
      ]
    },
    "completeness": {
      "type": "string",
      "enum": [
        "Complete",
        "Partial",
        "Unknown"
      ]
    },
    "provenanceType": {
      "type": "string",
      "enum": [
        "None",
        "Observed",
        "Restored",
        "Reported",
        "Derived"
      ]
    },
    "capabilityState": {
      "type": "string",
      "enum": [
        "Supported",
        "Degraded",
        "UnavailablePendingValidation"
      ]
    },
    "reasonCode": {
      "type": "string",
      "enum": [
        "None",
        "NotLoadedThisSession",
        "WindowNotReady",
        "IncompleteContainerSet",
        "IncompleteRetainerSet",
        "MappingValidationFailed",
        "PatchValidationRequired",
        "RestoredFromDisk",
        "CharacterUnavailable",
        "IdentityUnavailable",
        "QuantityOverflow",
        "CaptureFailed",
        "GameDataVersionUnsupported",
        "OutputSizeLimitExceeded",
        "SerializerValidationFailed"
      ]
    },
    "sourceKey": {
      "type": "string",
      "enum": [
        "mounts",
        "minions",
        "emotes",
        "bardings",
        "cards",
        "npcs",
        "fashions",
        "frames",
        "occult_records",
        "orchestrions",
        "spells",
        "aetherytes",
        "chocobo_routes",
        "leves",
        "hairstyles",
        "field_records",
        "survey_records",
        "quests",
        "achievements",
        "armoires",
        "glamour_dresser",
        "outfits",
        "relics",
        "inventory",
        "armoryChest",
        "chocoboSaddlebag",
        "premiumSaddlebag",
        "retainers",
        "armoire",
        "glamourDresser",
        "reportedOwnership",
        "facewear"
      ]
    },
    "sourceCapability": {
      "type": "object",
      "required": [
        "dataType",
        "capabilityState",
        "supportsQuantity",
        "reasonCode",
        "reason"
      ],
      "properties": {
        "dataType": {
          "$ref": "#/$defs/dataType"
        },
        "capabilityState": {
          "$ref": "#/$defs/capabilityState"
        },
        "supportsQuantity": {
          "type": "boolean"
        },
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "reason": {
          "type": "string",
          "maxLength": 160
        }
      },
      "additionalProperties": true
    },
    "sourceHealth": {
      "type": "object",
      "required": [
        "dataType",
        "captureGeneration",
        "sourceStatus",
        "completeness",
        "provenanceType",
        "capabilityState",
        "reasonCode",
        "reason"
      ],
      "properties": {
        "dataType": {
          "$ref": "#/$defs/dataType"
        },
        "observedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "captureGeneration": {
          "$ref": "#/$defs/captureGeneration"
        },
        "sourceStatus": {
          "$ref": "#/$defs/sourceStatus"
        },
        "completeness": {
          "$ref": "#/$defs/completeness"
        },
        "provenanceType": {
          "$ref": "#/$defs/provenanceType"
        },
        "capabilityState": {
          "$ref": "#/$defs/capabilityState"
        },
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "reason": {
          "type": "string",
          "maxLength": 160
        }
      },
      "allOf": [
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "enum": [
                  "Fresh",
                  "Stale"
                ]
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "required": [
              "observedAtUtc"
            ],
            "properties": {
              "provenanceType": {
                "not": {
                  "const": "None"
                }
              }
            }
          }
        },
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "enum": [
                  "Unavailable",
                  "NeverObserved"
                ]
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "not": {
              "required": [
                "observedAtUtc"
              ]
            },
            "properties": {
              "provenanceType": {
                "const": "None"
              }
            }
          }
        }
      ],
      "additionalProperties": true
    },
    "unlockSet": {
      "type": "object",
      "required": [
        "unlockedIds"
      ],
      "properties": {
        "unlockedIds": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/positiveId"
          }
        }
      },
      "additionalProperties": true
    },
    "quantifiedPresenceItem": {
      "type": "object",
      "required": [
        "itemId",
        "quantity"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        },
        "quantity": {
          "$ref": "#/$defs/quantity"
        }
      },
      "additionalProperties": true
    },
    "setPresenceItem": {
      "type": "object",
      "required": [
        "itemId"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        }
      },
      "not": {
        "required": [
          "quantity"
        ]
      },
      "additionalProperties": true
    },
    "quantifiedPresenceSource": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/quantifiedPresenceItem"
          }
        }
      },
      "additionalProperties": true
    },
    "setPresenceSource": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/setPresenceItem"
          }
        }
      },
      "additionalProperties": true
    },
    "reportedEvidence": {
      "type": "object",
      "required": [
        "itemId",
        "reportedAtUtc",
        "reporter"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        },
        "reportedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "reporter": {
          "type": "string",
          "minLength": 1,
          "maxLength": 64
        }
      },
      "additionalProperties": true
    },
    "ownershipFact": {
      "type": "object",
      "required": [
        "dataType",
        "sourceKey",
        "subjectId",
        "state",
        "sourceStatus"
      ],
      "properties": {
        "dataType": {
          "type": "string",
          "enum": [
            "PermanentUnlock",
            "CurrentItemPresence"
          ]
        },
        "sourceKey": {
          "$ref": "#/$defs/sourceKey"
        },
        "subjectId": {
          "$ref": "#/$defs/positiveId"
        },
        "state": {
          "type": "string",
          "enum": [
            "Present",
            "Absent",
            "Unknown"
          ]
        },
        "sourceStatus": {
          "$ref": "#/$defs/sourceStatus"
        }
      },
      "additionalProperties": true
    },
    "ownershipEvidence": {
      "type": "object",
      "required": [
        "ruleCode",
        "facts"
      ],
      "properties": {
        "ruleCode": {
          "type": "string",
          "enum": [
            "DirectPermanentUnlock",
            "DirectCurrentItemPresence",
            "QuestCompletion",
            "AllRequiredConstituents",
            "PermanentUnlockOrCurrentItemPresence"
          ]
        },
        "facts": {
          "type": "array",
          "minItems": 1,
          "maxItems": 64,
          "items": {
            "$ref": "#/$defs/ownershipFact"
          }
        }
      },
      "additionalProperties": true
    },
    "collectionItem": {
      "type": "object",
      "required": [
        "id",
        "name",
        "owned",
        "ownershipEvidence"
      ],
      "properties": {
        "id": {
          "$ref": "#/$defs/positiveId"
        },
        "name": {
          "type": "string",
          "minLength": 1,
          "maxLength": 512
        },
        "owned": {
          "type": "boolean"
        },
        "ownershipEvidence": {
          "$ref": "#/$defs/ownershipEvidence"
        },
        "reportedEvidence": {
          "type": "array",
          "maxItems": 64,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/reportedEvidence"
          }
        },
        "status": {
          "type": "string",
          "enum": [
            "NotStarted",
            "InProgress",
            "Completed"
          ]
        },
        "group": {
          "type": "string",
          "maxLength": 256
        },
        "category": {
          "type": "string",
          "maxLength": 256
        },
        "series": {
          "type": "string",
          "maxLength": 256
        },
        "level": {
          "type": "integer",
          "minimum": 0,
          "maximum": 999
        },
        "location": {
          "type": "string",
          "maxLength": 512
        },
        "expansion": {
          "type": "string",
          "maxLength": 128
        },
        "festival": {
          "type": "boolean"
        },
        "repeatable": {
          "type": "boolean"
        },
        "progress": {
          "type": "object",
          "properties": {
            "current": {
              "type": "integer",
              "minimum": 0,
              "maximum": 2147483647
            },
            "target": {
              "type": "integer",
              "minimum": 1,
              "maximum": 2147483647
            },
            "sequence": {
              "type": "integer",
              "minimum": 0,
              "maximum": 255
            }
          },
          "minProperties": 1,
          "additionalProperties": true
        },
        "description": {
          "type": "string",
          "maxLength": 4096
        },
        "points": {
          "type": "integer",
          "minimum": 0,
          "maximum": 65535
        },
        "rewardCollected": {
          "type": [
            "boolean",
            "null"
          ]
        },
        "rewards": {
          "type": "array",
          "maxItems": 64,
          "items": {
            "type": "object",
            "required": [
              "kind",
              "id"
            ],
            "properties": {
              "kind": {
                "type": "string",
                "minLength": 1,
                "maxLength": 64
              },
              "id": {
                "$ref": "#/$defs/positiveId"
              },
              "quantity": {
                "$ref": "#/$defs/quantity"
              }
            },
            "additionalProperties": true
          }
        }
      },
      "additionalProperties": true
    },
    "collectionResult": {
      "type": "object",
      "required": [
        "label",
        "ownershipKind",
        "totalKnown",
        "sourceStatus",
        "completeness",
        "provenanceType",
        "observedAtUtc",
        "items"
      ],
      "properties": {
        "label": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        },
        "ownershipKind": {
          "type": "string",
          "enum": [
            "PermanentUnlock",
            "CurrentItemPresence",
            "Derived"
          ]
        },
        "totalKnown": {
          "type": "boolean"
        },
        "sourceStatus": {
          "type": "string",
          "enum": [
            "Fresh",
            "Stale"
          ]
        },
        "completeness": {
          "type": "string",
          "const": "Complete"
        },
        "provenanceType": {
          "$ref": "#/$defs/provenanceType"
        },
        "observedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/collectionItem"
          }
        }
      },
      "additionalProperties": true
    },
    "omission": {
      "type": "object",
      "required": [
        "reasonCode",
        "message"
      ],
      "properties": {
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "message": {
          "type": "string",
          "minLength": 1,
          "maxLength": 160
        }
      },
      "additionalProperties": true
    },
    "dresserEntry": {
      "type": "object",
      "required": [
        "entryIndex",
        "rawItemId",
        "baseItemId",
        "outfitId",
        "components"
      ],
      "properties": {
        "entryIndex": {
          "type": "integer",
          "minimum": 0,
          "maximum": 799
        },
        "rawItemId": {
          "type": "integer",
          "minimum": 1,
          "maximum": 4294967295
        },
        "baseItemId": {
          "type": "integer",
          "minimum": 1,
          "maximum": 4294967295
        },
        "outfitId": {
          "type": [
            "integer",
            "null"
          ],
          "minimum": 1,
          "maximum": 4294967295
        },
        "components": {
          "type": "array",
          "minItems": 1,
          "maxItems": 11,
          "items": {
            "type": "object",
            "required": [
              "slotIndex",
              "itemId"
            ],
            "properties": {
              "slotIndex": {
                "type": "integer",
                "minimum": 0,
                "maximum": 10
              },
              "itemId": {
                "type": "integer",
                "minimum": 1,
                "maximum": 4294967295
              }
            },
            "additionalProperties": true
          }
        }
      },
      "additionalProperties": true
    },
    "dresserPresenceSource": {
      "allOf": [
        {
          "$ref": "#/$defs/setPresenceSource"
        }
      ],
      "properties": {
        "dresserEntries": {
          "type": "array",
          "maxItems": 800,
          "items": {
            "$ref": "#/$defs/dresserEntry"
          }
        }
      }
    }
  }
}
```

#### 21.2.2 Collection Snapshot

Canonical file: [data/Contracts/1.1/schemas/collection-data.schema.json](../data/Contracts/1.1/schemas/collection-data.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.1/collection-data.schema.json",
  "title": "Exportaramus CollectionData Snapshot 1.1",
  "type": "object",
  "required": [
    "schemaVersion",
    "providerInternalName",
    "providerVersion",
    "providerSessionId",
    "revision",
    "contentId",
    "capturedAtUtc",
    "captureType",
    "captureGeneration",
    "consistency",
    "unlocks",
    "currentItemPresence",
    "reportedOwnership",
    "sourceCapabilities",
    "sourceHealth"
  ],
  "properties": {
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerInternalName": {
      "type": "string",
      "const": "Exportaramus"
    },
    "providerVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "revision": {
      "$ref": "common.schema.json#/$defs/revision"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "captureType": {
      "$ref": "common.schema.json#/$defs/captureType"
    },
    "captureGeneration": {
      "$ref": "common.schema.json#/$defs/captureGeneration"
    },
    "consistency": {
      "$ref": "common.schema.json#/$defs/consistency"
    },
    "unlocks": {
      "type": "object",
      "properties": {
        "mounts": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "minions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "emotes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "bardings": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "cards": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "npcs": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "fashions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "frames": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "occult_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "orchestrions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "spells": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "aetherytes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "chocobo_routes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "leves": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "hairstyles": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "field_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "survey_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "quests": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "achievements": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "facewear": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/unlockSet"
      }
    },
    "currentItemPresence": {
      "type": "object",
      "properties": {
        "inventory": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoryChest": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "chocoboSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "premiumSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "retainers": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoire": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        },
        "glamourDresser": {
          "$ref": "common.schema.json#/$defs/dresserPresenceSource"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/setPresenceSource"
      }
    },
    "reportedOwnership": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 10000,
          "uniqueItems": true,
          "items": {
            "$ref": "common.schema.json#/$defs/reportedEvidence"
          }
        }
      },
      "additionalProperties": true
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      }
    },
    "sourceHealth": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceHealth"
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.2.3 Expanded Snapshot

Canonical file: [data/Contracts/1.1/schemas/exportaramus-snapshot.schema.json](../data/Contracts/1.1/schemas/exportaramus-snapshot.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.1/exportaramus-snapshot.schema.json",
  "title": "Exportaramus Expanded Snapshot 1.1",
  "type": "object",
  "required": [
    "format",
    "schemaVersion",
    "exportaramusVersion",
    "exportedAtUtc",
    "capturedAtUtc",
    "captureType",
    "captureGeneration",
    "consistency",
    "catalog",
    "character",
    "collections",
    "currentItemPresence",
    "sourceCapabilities",
    "sourceHealth",
    "omittedCollections"
  ],
  "properties": {
    "format": {
      "type": "string",
      "const": "exportaramus-snapshot"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "exportaramusVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "exportedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "captureType": {
      "$ref": "common.schema.json#/$defs/captureType"
    },
    "captureGeneration": {
      "$ref": "common.schema.json#/$defs/captureGeneration"
    },
    "consistency": {
      "$ref": "common.schema.json#/$defs/consistency"
    },
    "catalog": {
      "type": "object",
      "required": [
        "clientLanguage",
        "gameDataVersion"
      ],
      "properties": {
        "clientLanguage": {
          "$ref": "common.schema.json#/$defs/clientLanguage"
        },
        "gameDataVersion": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        }
      },
      "additionalProperties": true
    },
    "character": {
      "type": "object",
      "required": [
        "displayName"
      ],
      "properties": {
        "displayName": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        }
      },
      "additionalProperties": true
    },
    "collections": {
      "type": "object",
      "properties": {
        "mounts": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "minions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "emotes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "bardings": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "cards": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "npcs": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "fashions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "frames": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "occult_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "orchestrions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "spells": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "aetherytes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "chocobo_routes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "leves": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "hairstyles": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "field_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "survey_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "armoires": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "glamour_dresser": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "outfits": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "relics": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "quests": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "achievements": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/collectionResult"
      }
    },
    "currentItemPresence": {
      "type": "object",
      "properties": {
        "inventory": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoryChest": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "chocoboSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "premiumSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "retainers": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoire": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        },
        "glamourDresser": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/setPresenceSource"
      }
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      }
    },
    "sourceHealth": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceHealth"
      }
    },
    "omittedCollections": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/omission"
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.2.4 Capabilities

Canonical file: [data/Contracts/1.1/schemas/capabilities.schema.json](../data/Contracts/1.1/schemas/capabilities.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.1/capabilities.schema.json",
  "title": "Exportaramus CollectionData Capabilities 1.1",
  "type": "object",
  "required": [
    "schemaVersion",
    "providerInternalName",
    "providerVersion",
    "providerSessionId",
    "clientLanguage",
    "gameDataVersion",
    "gates",
    "unlockKeys",
    "currentPresenceSources",
    "sourceCapabilities",
    "collectionDataSchema",
    "limits",
    "reportedOwnershipEvidence"
  ],
  "properties": {
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerInternalName": {
      "type": "string",
      "const": "Exportaramus"
    },
    "providerVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "clientLanguage": {
      "$ref": "common.schema.json#/$defs/clientLanguage"
    },
    "gameDataVersion": {
      "type": "string",
      "minLength": 1,
      "maxLength": 128
    },
    "gates": {
      "type": "object",
      "required": [
        "getCapabilities",
        "getSnapshot",
        "snapshotChanged",
        "snapshotWithdrawn",
        "reportOwnershipEvidence"
      ],
      "properties": {
        "getCapabilities": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_1.GetCapabilities"
        },
        "getSnapshot": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_1.GetSnapshot"
        },
        "snapshotChanged": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_1.SnapshotChanged"
        },
        "snapshotWithdrawn": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_1.SnapshotWithdrawn"
        },
        "reportOwnershipEvidence": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_1.ReportOwnershipEvidence"
        }
      },
      "additionalProperties": true
    },
    "unlockKeys": {
      "type": "array",
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    },
    "currentPresenceSources": {
      "type": "array",
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      }
    },
    "collectionDataSchema": {
      "type": "object",
      "required": [
        "id",
        "sha256"
      ],
      "properties": {
        "id": {
          "type": "string",
          "const": "https://exportaramus.local/schemas/1.1/collection-data.schema.json"
        },
        "sha256": {
          "type": "string",
          "pattern": "^[0-9a-f]{64}$"
        }
      },
      "additionalProperties": true
    },
    "limits": {
      "type": "object",
      "required": [
        "snapshotUtf8Bytes",
        "capabilitiesUtf8Bytes",
        "eventUtf8Bytes",
        "idsPerCollection",
        "reportedEvidenceRecordsPerCharacter",
        "reporterUtf8Bytes",
        "pendingReportsTotal",
        "pendingReportsPerReporter",
        "quantityMaximum",
        "globalReportRate",
        "reporterReportRate"
      ],
      "properties": {
        "snapshotUtf8Bytes": {
          "type": "integer",
          "const": 4194304
        },
        "capabilitiesUtf8Bytes": {
          "type": "integer",
          "const": 16384
        },
        "eventUtf8Bytes": {
          "type": "integer",
          "const": 4096
        },
        "idsPerCollection": {
          "type": "integer",
          "const": 50000
        },
        "reportedEvidenceRecordsPerCharacter": {
          "type": "integer",
          "const": 10000
        },
        "reporterUtf8Bytes": {
          "type": "integer",
          "const": 64
        },
        "pendingReportsTotal": {
          "type": "integer",
          "const": 256
        },
        "pendingReportsPerReporter": {
          "type": "integer",
          "const": 32
        },
        "quantityMaximum": {
          "type": "integer",
          "const": 4294967295
        },
        "globalReportRate": {
          "$ref": "#/$defs/rateLimit"
        },
        "reporterReportRate": {
          "$ref": "#/$defs/rateLimit"
        }
      },
      "additionalProperties": true
    },
    "reportedOwnershipEvidence": {
      "type": "object",
      "required": [
        "available",
        "resultCodes"
      ],
      "properties": {
        "available": {
          "type": "boolean"
        },
        "resultCodes": {
          "type": "array",
          "minItems": 8,
          "maxItems": 8,
          "uniqueItems": true,
          "items": {
            "type": "string",
            "enum": [
              "Accepted",
              "InvalidRequest",
              "WrongCharacter",
              "RateLimited",
              "CapacityReached",
              "QueueFull",
              "Unavailable",
              "ProviderStopping"
            ]
          }
        }
      },
      "additionalProperties": true
    }
  },
  "$defs": {
    "rateLimit": {
      "type": "object",
      "required": [
        "acceptedPerMinute",
        "burst"
      ],
      "properties": {
        "acceptedPerMinute": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10000
        },
        "burst": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10000
        }
      },
      "additionalProperties": true
    }
  },
  "additionalProperties": true
}
```

#### 21.2.5 Change Notice

Canonical file: [data/Contracts/1.1/schemas/snapshot-changed.schema.json](../data/Contracts/1.1/schemas/snapshot-changed.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.1/snapshot-changed.schema.json",
  "title": "Exportaramus CollectionData SnapshotChanged Notice 1.0",
  "type": "object",
  "required": [
    "eventType",
    "schemaVersion",
    "providerSessionId",
    "revision",
    "contentId",
    "capturedAtUtc",
    "changedDataTypes",
    "changedSources"
  ],
  "properties": {
    "eventType": {
      "type": "string",
      "const": "SnapshotChanged"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "revision": {
      "$ref": "common.schema.json#/$defs/revision"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "changedDataTypes": {
      "type": "array",
      "minItems": 1,
      "maxItems": 5,
      "uniqueItems": true,
      "items": {
        "type": "string",
        "enum": [
          "Unlocks",
          "CurrentItemPresence",
          "ReportedOwnership",
          "SourceCapabilities",
          "SourceHealth"
        ]
      }
    },
    "changedSources": {
      "type": "array",
      "minItems": 1,
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.2.6 Withdrawal Notice

Canonical file: [data/Contracts/1.1/schemas/snapshot-withdrawn.schema.json](../data/Contracts/1.1/schemas/snapshot-withdrawn.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.1/snapshot-withdrawn.schema.json",
  "title": "Exportaramus CollectionData SnapshotWithdrawn Notice 1.0",
  "type": "object",
  "required": [
    "eventType",
    "schemaVersion",
    "providerSessionId",
    "contentId",
    "withdrawnAtUtc",
    "reasonCode"
  ],
  "properties": {
    "eventType": {
      "type": "string",
      "const": "SnapshotWithdrawn"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "withdrawnAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "reasonCode": {
      "type": "string",
      "enum": [
        "LoggedOut",
        "CharacterChanged",
        "IdentityUnavailable",
        "ProviderStopping"
      ]
    },
    "explanation": {
      "type": "string",
      "maxLength": 160
    }
  },
  "additionalProperties": true
}
```

### 21.3 CollectionData 1.2

#### 21.3.1 Common Definitions

Canonical file: [data/Contracts/1.2/schemas/common.schema.json](../data/Contracts/1.2/schemas/common.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.2/common.schema.json",
  "title": "Exportaramus Common Contract Types 1.2",
  "$defs": {
    "schemaVersion": {
      "type": "string",
      "const": "1.2"
    },
    "providerVersion": {
      "type": "string",
      "minLength": 1,
      "maxLength": 64
    },
    "providerSessionId": {
      "type": "string",
      "format": "uuid"
    },
    "contentId": {
      "type": "string",
      "pattern": "^[1-9][0-9]{0,19}$",
      "maxLength": 20
    },
    "utcDateTime": {
      "type": "string",
      "format": "date-time",
      "pattern": "Z$",
      "maxLength": 64
    },
    "revision": {
      "type": "integer",
      "minimum": 1,
      "maximum": 9007199254740991
    },
    "captureGeneration": {
      "type": "integer",
      "minimum": 0,
      "maximum": 9007199254740991
    },
    "positiveId": {
      "type": "integer",
      "minimum": 1,
      "maximum": 4294967295
    },
    "quantity": {
      "type": "integer",
      "minimum": 1,
      "maximum": 4294967295
    },
    "clientLanguage": {
      "type": "string",
      "enum": [
        "Japanese",
        "English",
        "German",
        "French"
      ]
    },
    "captureType": {
      "type": "string",
      "enum": [
        "Full",
        "Lightweight",
        "Restored",
        "Reported"
      ]
    },
    "consistency": {
      "type": "string",
      "enum": [
        "Coherent",
        "MixedObservationTimes"
      ]
    },
    "dataType": {
      "type": "string",
      "enum": [
        "PermanentUnlock",
        "CurrentItemPresence",
        "Derived",
        "ReportedOwnership"
      ]
    },
    "sourceStatus": {
      "type": "string",
      "enum": [
        "Fresh",
        "Stale",
        "Unavailable",
        "NeverObserved"
      ]
    },
    "completeness": {
      "type": "string",
      "enum": [
        "Complete",
        "Partial",
        "Unknown"
      ]
    },
    "provenanceType": {
      "type": "string",
      "enum": [
        "None",
        "Observed",
        "Restored",
        "Reported",
        "Derived"
      ]
    },
    "capabilityState": {
      "type": "string",
      "enum": [
        "Supported",
        "Degraded",
        "UnavailablePendingValidation"
      ]
    },
    "reasonCode": {
      "type": "string",
      "enum": [
        "None",
        "NotLoadedThisSession",
        "WindowNotReady",
        "IncompleteContainerSet",
        "IncompleteRetainerSet",
        "MappingValidationFailed",
        "PatchValidationRequired",
        "RestoredFromDisk",
        "CharacterUnavailable",
        "IdentityUnavailable",
        "QuantityOverflow",
        "CaptureFailed",
        "GameDataVersionUnsupported",
        "OutputSizeLimitExceeded",
        "SerializerValidationFailed"
      ]
    },
    "sourceKey": {
      "type": "string",
      "enum": [
        "mounts",
        "minions",
        "emotes",
        "bardings",
        "cards",
        "npcs",
        "fashions",
        "frames",
        "occult_records",
        "orchestrions",
        "spells",
        "aetherytes",
        "chocobo_routes",
        "leves",
        "hairstyles",
        "field_records",
        "survey_records",
        "quests",
        "achievements",
        "armoires",
        "glamour_dresser",
        "outfits",
        "relics",
        "inventory",
        "armoryChest",
        "chocoboSaddlebag",
        "premiumSaddlebag",
        "retainers",
        "armoire",
        "glamourDresser",
        "reportedOwnership",
        "facewear",
        "beasts"
      ]
    },
    "sourceCapability": {
      "type": "object",
      "required": [
        "dataType",
        "capabilityState",
        "supportsQuantity",
        "reasonCode",
        "reason"
      ],
      "properties": {
        "dataType": {
          "$ref": "#/$defs/dataType"
        },
        "capabilityState": {
          "$ref": "#/$defs/capabilityState"
        },
        "supportsQuantity": {
          "type": "boolean"
        },
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "reason": {
          "type": "string",
          "maxLength": 160
        }
      },
      "additionalProperties": true
    },
    "sourceHealth": {
      "type": "object",
      "required": [
        "dataType",
        "captureGeneration",
        "sourceStatus",
        "completeness",
        "provenanceType",
        "capabilityState",
        "reasonCode",
        "reason"
      ],
      "properties": {
        "dataType": {
          "$ref": "#/$defs/dataType"
        },
        "observedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "captureGeneration": {
          "$ref": "#/$defs/captureGeneration"
        },
        "sourceStatus": {
          "$ref": "#/$defs/sourceStatus"
        },
        "completeness": {
          "$ref": "#/$defs/completeness"
        },
        "provenanceType": {
          "$ref": "#/$defs/provenanceType"
        },
        "capabilityState": {
          "$ref": "#/$defs/capabilityState"
        },
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "reason": {
          "type": "string",
          "maxLength": 160
        }
      },
      "allOf": [
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "enum": [
                  "Fresh",
                  "Stale"
                ]
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "required": [
              "observedAtUtc"
            ],
            "properties": {
              "provenanceType": {
                "not": {
                  "const": "None"
                }
              }
            }
          }
        },
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "enum": [
                  "Unavailable",
                  "NeverObserved"
                ]
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "not": {
              "required": [
                "observedAtUtc"
              ]
            },
            "properties": {
              "provenanceType": {
                "const": "None"
              }
            }
          }
        }
      ],
      "additionalProperties": true
    },
    "unlockSet": {
      "type": "object",
      "required": [
        "unlockedIds"
      ],
      "properties": {
        "unlockedIds": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/positiveId"
          }
        }
      },
      "additionalProperties": true
    },
    "quantifiedPresenceItem": {
      "type": "object",
      "required": [
        "itemId",
        "quantity"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        },
        "quantity": {
          "$ref": "#/$defs/quantity"
        }
      },
      "additionalProperties": true
    },
    "setPresenceItem": {
      "type": "object",
      "required": [
        "itemId"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        }
      },
      "not": {
        "required": [
          "quantity"
        ]
      },
      "additionalProperties": true
    },
    "quantifiedPresenceSource": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/quantifiedPresenceItem"
          }
        }
      },
      "additionalProperties": true
    },
    "setPresenceSource": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/setPresenceItem"
          }
        }
      },
      "additionalProperties": true
    },
    "reportedEvidence": {
      "type": "object",
      "required": [
        "itemId",
        "reportedAtUtc",
        "reporter"
      ],
      "properties": {
        "itemId": {
          "$ref": "#/$defs/positiveId"
        },
        "reportedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "reporter": {
          "type": "string",
          "minLength": 1,
          "maxLength": 64
        }
      },
      "additionalProperties": true
    },
    "ownershipFact": {
      "type": "object",
      "required": [
        "dataType",
        "sourceKey",
        "subjectId",
        "state",
        "sourceStatus"
      ],
      "properties": {
        "dataType": {
          "type": "string",
          "enum": [
            "PermanentUnlock",
            "CurrentItemPresence"
          ]
        },
        "sourceKey": {
          "$ref": "#/$defs/sourceKey"
        },
        "subjectId": {
          "$ref": "#/$defs/positiveId"
        },
        "state": {
          "type": "string",
          "enum": [
            "Present",
            "Absent",
            "Unknown"
          ]
        },
        "sourceStatus": {
          "$ref": "#/$defs/sourceStatus"
        }
      },
      "additionalProperties": true
    },
    "ownershipEvidence": {
      "type": "object",
      "required": [
        "ruleCode",
        "facts"
      ],
      "properties": {
        "ruleCode": {
          "type": "string",
          "enum": [
            "DirectPermanentUnlock",
            "DirectCurrentItemPresence",
            "QuestCompletion",
            "AllRequiredConstituents",
            "PermanentUnlockOrCurrentItemPresence"
          ]
        },
        "facts": {
          "type": "array",
          "minItems": 1,
          "maxItems": 64,
          "items": {
            "$ref": "#/$defs/ownershipFact"
          }
        }
      },
      "additionalProperties": true
    },
    "collectionItem": {
      "type": "object",
      "required": [
        "id",
        "name",
        "owned",
        "ownershipEvidence"
      ],
      "properties": {
        "id": {
          "$ref": "#/$defs/positiveId"
        },
        "name": {
          "type": "string",
          "minLength": 1,
          "maxLength": 512
        },
        "owned": {
          "type": "boolean"
        },
        "ownershipEvidence": {
          "$ref": "#/$defs/ownershipEvidence"
        },
        "reportedEvidence": {
          "type": "array",
          "maxItems": 64,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/reportedEvidence"
          }
        },
        "status": {
          "type": "string",
          "enum": [
            "NotStarted",
            "InProgress",
            "Completed"
          ]
        },
        "group": {
          "type": "string",
          "maxLength": 256
        },
        "category": {
          "type": "string",
          "maxLength": 256
        },
        "series": {
          "type": "string",
          "maxLength": 256
        },
        "level": {
          "type": "integer",
          "minimum": 0,
          "maximum": 999
        },
        "location": {
          "type": "string",
          "maxLength": 512
        },
        "expansion": {
          "type": "string",
          "maxLength": 128
        },
        "festival": {
          "type": "boolean"
        },
        "repeatable": {
          "type": "boolean"
        },
        "progress": {
          "type": "object",
          "properties": {
            "current": {
              "type": "integer",
              "minimum": 0,
              "maximum": 2147483647
            },
            "target": {
              "type": "integer",
              "minimum": 1,
              "maximum": 2147483647
            },
            "sequence": {
              "type": "integer",
              "minimum": 0,
              "maximum": 255
            }
          },
          "minProperties": 1,
          "additionalProperties": true
        },
        "description": {
          "type": "string",
          "maxLength": 4096
        },
        "points": {
          "type": "integer",
          "minimum": 0,
          "maximum": 65535
        },
        "rewardCollected": {
          "type": [
            "boolean",
            "null"
          ]
        },
        "rewards": {
          "type": "array",
          "maxItems": 64,
          "items": {
            "type": "object",
            "required": [
              "kind",
              "id"
            ],
            "properties": {
              "kind": {
                "type": "string",
                "minLength": 1,
                "maxLength": 64
              },
              "id": {
                "$ref": "#/$defs/positiveId"
              },
              "quantity": {
                "$ref": "#/$defs/quantity"
              }
            },
            "additionalProperties": true
          }
        }
      },
      "additionalProperties": true
    },
    "collectionResult": {
      "type": "object",
      "required": [
        "label",
        "ownershipKind",
        "totalKnown",
        "sourceStatus",
        "completeness",
        "provenanceType",
        "observedAtUtc",
        "items"
      ],
      "properties": {
        "label": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        },
        "ownershipKind": {
          "type": "string",
          "enum": [
            "PermanentUnlock",
            "CurrentItemPresence",
            "Derived"
          ]
        },
        "totalKnown": {
          "type": "boolean"
        },
        "sourceStatus": {
          "type": "string",
          "enum": [
            "Fresh",
            "Stale"
          ]
        },
        "completeness": {
          "type": "string",
          "const": "Complete"
        },
        "provenanceType": {
          "$ref": "#/$defs/provenanceType"
        },
        "observedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "items": {
          "type": "array",
          "maxItems": 50000,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/collectionItem"
          }
        }
      },
      "additionalProperties": true
    },
    "omission": {
      "type": "object",
      "required": [
        "reasonCode",
        "message"
      ],
      "properties": {
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "message": {
          "type": "string",
          "minLength": 1,
          "maxLength": 160
        }
      },
      "additionalProperties": true
    },
    "dresserEntry": {
      "type": "object",
      "required": [
        "entryIndex",
        "rawItemId",
        "baseItemId",
        "outfitId",
        "components"
      ],
      "properties": {
        "entryIndex": {
          "type": "integer",
          "minimum": 0,
          "maximum": 799
        },
        "rawItemId": {
          "type": "integer",
          "minimum": 1,
          "maximum": 4294967295
        },
        "baseItemId": {
          "type": "integer",
          "minimum": 1,
          "maximum": 4294967295
        },
        "outfitId": {
          "type": [
            "integer",
            "null"
          ],
          "minimum": 1,
          "maximum": 4294967295
        },
        "components": {
          "type": "array",
          "minItems": 1,
          "maxItems": 11,
          "items": {
            "type": "object",
            "required": [
              "slotIndex",
              "itemId"
            ],
            "properties": {
              "slotIndex": {
                "type": "integer",
                "minimum": 0,
                "maximum": 10
              },
              "itemId": {
                "type": "integer",
                "minimum": 1,
                "maximum": 4294967295
              }
            },
            "additionalProperties": true
          }
        }
      },
      "additionalProperties": true
    },
    "dresserPresenceSource": {
      "allOf": [
        {
          "$ref": "#/$defs/setPresenceSource"
        }
      ],
      "properties": {
        "dresserEntries": {
          "type": "array",
          "maxItems": 800,
          "items": {
            "$ref": "#/$defs/dresserEntry"
          }
        }
      }
    },
    "beastSourceCapability": {
      "type": "object",
      "required": [
        "dataType",
        "capabilityState",
        "supportsQuantity",
        "reasonCode",
        "reason"
      ],
      "properties": {
        "dataType": {
          "const": "PermanentUnlock"
        },
        "capabilityState": {
          "$ref": "#/$defs/capabilityState"
        },
        "supportsQuantity": {
          "const": false
        },
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "reason": {
          "type": "string",
          "maxLength": 160
        }
      },
      "additionalProperties": true
    },
    "beastSourceHealth": {
      "type": "object",
      "required": [
        "dataType",
        "captureGeneration",
        "sourceStatus",
        "completeness",
        "provenanceType",
        "capabilityState",
        "reasonCode",
        "reason"
      ],
      "properties": {
        "dataType": {
          "const": "PermanentUnlock"
        },
        "observedAtUtc": {
          "$ref": "#/$defs/utcDateTime"
        },
        "captureGeneration": {
          "$ref": "#/$defs/captureGeneration"
        },
        "sourceStatus": {
          "$ref": "#/$defs/sourceStatus"
        },
        "completeness": {
          "$ref": "#/$defs/completeness"
        },
        "provenanceType": {
          "enum": [
            "None",
            "Observed",
            "Restored"
          ]
        },
        "capabilityState": {
          "$ref": "#/$defs/capabilityState"
        },
        "reasonCode": {
          "$ref": "#/$defs/reasonCode"
        },
        "reason": {
          "type": "string",
          "maxLength": 160
        }
      },
      "allOf": [
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "enum": [
                  "Fresh",
                  "Stale"
                ]
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "required": [
              "observedAtUtc"
            ],
            "properties": {
              "provenanceType": {
                "not": {
                  "const": "None"
                }
              }
            }
          }
        },
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "enum": [
                  "Unavailable",
                  "NeverObserved"
                ]
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "not": {
              "required": [
                "observedAtUtc"
              ]
            },
            "properties": {
              "provenanceType": {
                "const": "None"
              }
            }
          }
        },
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "const": "Fresh"
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "properties": {
              "provenanceType": {
                "const": "Observed"
              }
            }
          }
        },
        {
          "if": {
            "properties": {
              "sourceStatus": {
                "const": "Stale"
              }
            },
            "required": [
              "sourceStatus"
            ]
          },
          "then": {
            "properties": {
              "reasonCode": {
                "not": {
                  "const": "None"
                }
              },
              "reason": {
                "minLength": 1
              }
            }
          }
        }
      ],
      "additionalProperties": true
    }
  }
}
```

#### 21.3.2 Collection Snapshot

Canonical file: [data/Contracts/1.2/schemas/collection-data.schema.json](../data/Contracts/1.2/schemas/collection-data.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.2/collection-data.schema.json",
  "title": "Exportaramus CollectionData Snapshot 1.2",
  "type": "object",
  "required": [
    "schemaVersion",
    "providerInternalName",
    "providerVersion",
    "providerSessionId",
    "revision",
    "contentId",
    "capturedAtUtc",
    "captureType",
    "captureGeneration",
    "consistency",
    "unlocks",
    "currentItemPresence",
    "reportedOwnership",
    "sourceCapabilities",
    "sourceHealth"
  ],
  "properties": {
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerInternalName": {
      "type": "string",
      "const": "Exportaramus"
    },
    "providerVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "revision": {
      "$ref": "common.schema.json#/$defs/revision"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "captureType": {
      "$ref": "common.schema.json#/$defs/captureType"
    },
    "captureGeneration": {
      "$ref": "common.schema.json#/$defs/captureGeneration"
    },
    "consistency": {
      "$ref": "common.schema.json#/$defs/consistency"
    },
    "unlocks": {
      "type": "object",
      "properties": {
        "mounts": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "minions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "emotes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "bardings": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "cards": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "npcs": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "fashions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "frames": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "occult_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "orchestrions": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "spells": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "aetherytes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "chocobo_routes": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "leves": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "hairstyles": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "field_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "survey_records": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "quests": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "achievements": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "facewear": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        },
        "beasts": {
          "$ref": "common.schema.json#/$defs/unlockSet"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/unlockSet"
      }
    },
    "currentItemPresence": {
      "type": "object",
      "properties": {
        "inventory": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoryChest": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "chocoboSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "premiumSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "retainers": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoire": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        },
        "glamourDresser": {
          "$ref": "common.schema.json#/$defs/dresserPresenceSource"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/setPresenceSource"
      }
    },
    "reportedOwnership": {
      "type": "object",
      "required": [
        "items"
      ],
      "properties": {
        "items": {
          "type": "array",
          "maxItems": 10000,
          "uniqueItems": true,
          "items": {
            "$ref": "common.schema.json#/$defs/reportedEvidence"
          }
        }
      },
      "additionalProperties": true
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      },
      "required": [
        "beasts"
      ],
      "properties": {
        "beasts": {
          "$ref": "common.schema.json#/$defs/beastSourceCapability"
        }
      }
    },
    "sourceHealth": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceHealth"
      },
      "required": [
        "beasts"
      ],
      "properties": {
        "beasts": {
          "$ref": "common.schema.json#/$defs/beastSourceHealth"
        }
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.3.3 Expanded Snapshot

Canonical file: [data/Contracts/1.2/schemas/exportaramus-snapshot.schema.json](../data/Contracts/1.2/schemas/exportaramus-snapshot.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.2/exportaramus-snapshot.schema.json",
  "title": "Exportaramus Expanded Snapshot 1.2",
  "type": "object",
  "required": [
    "format",
    "schemaVersion",
    "exportaramusVersion",
    "exportedAtUtc",
    "capturedAtUtc",
    "captureType",
    "captureGeneration",
    "consistency",
    "catalog",
    "character",
    "collections",
    "currentItemPresence",
    "sourceCapabilities",
    "sourceHealth",
    "omittedCollections"
  ],
  "properties": {
    "format": {
      "type": "string",
      "const": "exportaramus-snapshot"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "exportaramusVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "exportedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "captureType": {
      "$ref": "common.schema.json#/$defs/captureType"
    },
    "captureGeneration": {
      "$ref": "common.schema.json#/$defs/captureGeneration"
    },
    "consistency": {
      "$ref": "common.schema.json#/$defs/consistency"
    },
    "catalog": {
      "type": "object",
      "required": [
        "clientLanguage",
        "gameDataVersion"
      ],
      "properties": {
        "clientLanguage": {
          "$ref": "common.schema.json#/$defs/clientLanguage"
        },
        "gameDataVersion": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        }
      },
      "additionalProperties": true
    },
    "character": {
      "type": "object",
      "required": [
        "displayName"
      ],
      "properties": {
        "displayName": {
          "type": "string",
          "minLength": 1,
          "maxLength": 128
        }
      },
      "additionalProperties": true
    },
    "collections": {
      "type": "object",
      "properties": {
        "mounts": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "minions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "emotes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "bardings": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "cards": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "npcs": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "fashions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "frames": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "occult_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "orchestrions": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "spells": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "aetherytes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "chocobo_routes": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "leves": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "hairstyles": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "field_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "survey_records": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "armoires": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "glamour_dresser": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "outfits": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "relics": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "quests": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "achievements": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "facewear": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        },
        "beasts": {
          "$ref": "common.schema.json#/$defs/collectionResult"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/collectionResult"
      }
    },
    "currentItemPresence": {
      "type": "object",
      "properties": {
        "inventory": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoryChest": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "chocoboSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "premiumSaddlebag": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "retainers": {
          "$ref": "common.schema.json#/$defs/quantifiedPresenceSource"
        },
        "armoire": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        },
        "glamourDresser": {
          "$ref": "common.schema.json#/$defs/setPresenceSource"
        }
      },
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/setPresenceSource"
      }
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      },
      "required": [
        "beasts"
      ],
      "properties": {
        "beasts": {
          "$ref": "common.schema.json#/$defs/beastSourceCapability"
        }
      }
    },
    "sourceHealth": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceHealth"
      },
      "required": [
        "beasts"
      ],
      "properties": {
        "beasts": {
          "$ref": "common.schema.json#/$defs/beastSourceHealth"
        }
      }
    },
    "omittedCollections": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/omission"
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.3.4 Capabilities

Canonical file: [data/Contracts/1.2/schemas/capabilities.schema.json](../data/Contracts/1.2/schemas/capabilities.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.2/capabilities.schema.json",
  "title": "Exportaramus CollectionData Capabilities 1.2",
  "type": "object",
  "required": [
    "schemaVersion",
    "providerInternalName",
    "providerVersion",
    "providerSessionId",
    "clientLanguage",
    "gameDataVersion",
    "gates",
    "unlockKeys",
    "currentPresenceSources",
    "sourceCapabilities",
    "collectionDataSchema",
    "limits",
    "reportedOwnershipEvidence"
  ],
  "properties": {
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerInternalName": {
      "type": "string",
      "const": "Exportaramus"
    },
    "providerVersion": {
      "$ref": "common.schema.json#/$defs/providerVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "clientLanguage": {
      "$ref": "common.schema.json#/$defs/clientLanguage"
    },
    "gameDataVersion": {
      "type": "string",
      "minLength": 1,
      "maxLength": 128
    },
    "gates": {
      "type": "object",
      "required": [
        "getCapabilities",
        "getSnapshot",
        "snapshotChanged",
        "snapshotWithdrawn",
        "reportOwnershipEvidence"
      ],
      "properties": {
        "getCapabilities": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_2.GetCapabilities"
        },
        "getSnapshot": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_2.GetSnapshot"
        },
        "snapshotChanged": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_2.SnapshotChanged"
        },
        "snapshotWithdrawn": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_2.SnapshotWithdrawn"
        },
        "reportOwnershipEvidence": {
          "type": "string",
          "const": "Exportaramus.CollectionData.V1_2.ReportOwnershipEvidence"
        }
      },
      "additionalProperties": true
    },
    "unlockKeys": {
      "type": "array",
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    },
    "currentPresenceSources": {
      "type": "array",
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    },
    "sourceCapabilities": {
      "type": "object",
      "additionalProperties": {
        "$ref": "common.schema.json#/$defs/sourceCapability"
      },
      "required": [
        "beasts"
      ],
      "properties": {
        "beasts": {
          "$ref": "common.schema.json#/$defs/beastSourceCapability"
        }
      }
    },
    "collectionDataSchema": {
      "type": "object",
      "required": [
        "id",
        "sha256"
      ],
      "properties": {
        "id": {
          "type": "string",
          "const": "https://exportaramus.local/schemas/1.2/collection-data.schema.json"
        },
        "sha256": {
          "type": "string",
          "pattern": "^[0-9a-f]{64}$"
        }
      },
      "additionalProperties": true
    },
    "limits": {
      "type": "object",
      "required": [
        "snapshotUtf8Bytes",
        "capabilitiesUtf8Bytes",
        "eventUtf8Bytes",
        "idsPerCollection",
        "reportedEvidenceRecordsPerCharacter",
        "reporterUtf8Bytes",
        "pendingReportsTotal",
        "pendingReportsPerReporter",
        "quantityMaximum",
        "globalReportRate",
        "reporterReportRate"
      ],
      "properties": {
        "snapshotUtf8Bytes": {
          "type": "integer",
          "const": 4194304
        },
        "capabilitiesUtf8Bytes": {
          "type": "integer",
          "const": 16384
        },
        "eventUtf8Bytes": {
          "type": "integer",
          "const": 4096
        },
        "idsPerCollection": {
          "type": "integer",
          "const": 50000
        },
        "reportedEvidenceRecordsPerCharacter": {
          "type": "integer",
          "const": 10000
        },
        "reporterUtf8Bytes": {
          "type": "integer",
          "const": 64
        },
        "pendingReportsTotal": {
          "type": "integer",
          "const": 256
        },
        "pendingReportsPerReporter": {
          "type": "integer",
          "const": 32
        },
        "quantityMaximum": {
          "type": "integer",
          "const": 4294967295
        },
        "globalReportRate": {
          "$ref": "#/$defs/rateLimit"
        },
        "reporterReportRate": {
          "$ref": "#/$defs/rateLimit"
        }
      },
      "additionalProperties": true
    },
    "reportedOwnershipEvidence": {
      "type": "object",
      "required": [
        "available",
        "resultCodes"
      ],
      "properties": {
        "available": {
          "type": "boolean"
        },
        "resultCodes": {
          "type": "array",
          "minItems": 8,
          "maxItems": 8,
          "uniqueItems": true,
          "items": {
            "type": "string",
            "enum": [
              "Accepted",
              "InvalidRequest",
              "WrongCharacter",
              "RateLimited",
              "CapacityReached",
              "QueueFull",
              "Unavailable",
              "ProviderStopping"
            ]
          }
        }
      },
      "additionalProperties": true
    }
  },
  "$defs": {
    "rateLimit": {
      "type": "object",
      "required": [
        "acceptedPerMinute",
        "burst"
      ],
      "properties": {
        "acceptedPerMinute": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10000
        },
        "burst": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10000
        }
      },
      "additionalProperties": true
    }
  },
  "additionalProperties": true
}
```

#### 21.3.5 Change Notice

Canonical file: [data/Contracts/1.2/schemas/snapshot-changed.schema.json](../data/Contracts/1.2/schemas/snapshot-changed.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.2/snapshot-changed.schema.json",
  "title": "Exportaramus CollectionData SnapshotChanged Notice 1.2",
  "type": "object",
  "required": [
    "eventType",
    "schemaVersion",
    "providerSessionId",
    "revision",
    "contentId",
    "capturedAtUtc",
    "changedDataTypes",
    "changedSources"
  ],
  "properties": {
    "eventType": {
      "type": "string",
      "const": "SnapshotChanged"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "revision": {
      "$ref": "common.schema.json#/$defs/revision"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "capturedAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "changedDataTypes": {
      "type": "array",
      "minItems": 1,
      "maxItems": 5,
      "uniqueItems": true,
      "items": {
        "type": "string",
        "enum": [
          "Unlocks",
          "CurrentItemPresence",
          "ReportedOwnership",
          "SourceCapabilities",
          "SourceHealth"
        ]
      }
    },
    "changedSources": {
      "type": "array",
      "minItems": 1,
      "maxItems": 64,
      "uniqueItems": true,
      "items": {
        "$ref": "common.schema.json#/$defs/sourceKey"
      }
    }
  },
  "additionalProperties": true
}
```

#### 21.3.6 Withdrawal Notice

Canonical file: [data/Contracts/1.2/schemas/snapshot-withdrawn.schema.json](../data/Contracts/1.2/schemas/snapshot-withdrawn.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/1.2/snapshot-withdrawn.schema.json",
  "title": "Exportaramus CollectionData SnapshotWithdrawn Notice 1.2",
  "type": "object",
  "required": [
    "eventType",
    "schemaVersion",
    "providerSessionId",
    "contentId",
    "withdrawnAtUtc",
    "reasonCode"
  ],
  "properties": {
    "eventType": {
      "type": "string",
      "const": "SnapshotWithdrawn"
    },
    "schemaVersion": {
      "$ref": "common.schema.json#/$defs/schemaVersion"
    },
    "providerSessionId": {
      "$ref": "common.schema.json#/$defs/providerSessionId"
    },
    "contentId": {
      "$ref": "common.schema.json#/$defs/contentId"
    },
    "withdrawnAtUtc": {
      "$ref": "common.schema.json#/$defs/utcDateTime"
    },
    "reasonCode": {
      "type": "string",
      "enum": [
        "LoggedOut",
        "CharacterChanged",
        "IdentityUnavailable",
        "ProviderStopping"
      ]
    },
    "explanation": {
      "type": "string",
      "maxLength": 160
    }
  },
  "additionalProperties": true
}
```

### 21.4 FFXIV Collect

#### 21.4.1 Original Import Contract

Canonical file: [data/FFXIVCollect/1.0/ffxiv-collect-import.schema.json](../data/FFXIVCollect/1.0/ffxiv-collect-import.schema.json)

Identical copies: [data/Contracts/1.1/resources/ffxiv-collect-import.schema.json](../data/Contracts/1.1/resources/ffxiv-collect-import.schema.json).

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/ffxiv-collect/1.0/import.schema.json",
  "title": "Exportaramus FFXIV Collect Import Payload",
  "type": "object",
  "minProperties": 1,
  "maxProperties": 15,
  "properties": {
    "armoires": {
      "$ref": "#/$defs/idArray"
    },
    "bardings": {
      "$ref": "#/$defs/idArray"
    },
    "cards": {
      "$ref": "#/$defs/idArray"
    },
    "fashions": {
      "$ref": "#/$defs/idArray"
    },
    "field_records": {
      "$ref": "#/$defs/idArray"
    },
    "frames": {
      "$ref": "#/$defs/idArray"
    },
    "hairstyles": {
      "$ref": "#/$defs/idArray"
    },
    "leves": {
      "$ref": "#/$defs/idArray"
    },
    "npcs": {
      "$ref": "#/$defs/idArray"
    },
    "occult_records": {
      "$ref": "#/$defs/idArray"
    },
    "orchestrions": {
      "$ref": "#/$defs/idArray"
    },
    "outfits": {
      "$ref": "#/$defs/idArray"
    },
    "relics": {
      "$ref": "#/$defs/idArray"
    },
    "spells": {
      "$ref": "#/$defs/idArray"
    },
    "survey_records": {
      "$ref": "#/$defs/idArray"
    }
  },
  "$defs": {
    "idArray": {
      "type": "array",
      "maxItems": 50000,
      "uniqueItems": true,
      "items": {
        "type": "integer",
        "minimum": 1,
        "maximum": 4294967295
      }
    }
  },
  "additionalProperties": false
}
```

#### 21.4.2 Import Contract Including Beasts

Canonical file: [data/Contracts/1.2/resources/ffxiv-collect-import.schema.json](../data/Contracts/1.2/resources/ffxiv-collect-import.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/ffxiv-collect/1.1/import.schema.json",
  "title": "Exportaramus FFXIV Collect Import Payload 1.1 (Beasts opt-in)",
  "type": "object",
  "minProperties": 1,
  "maxProperties": 16,
  "properties": {
    "armoires": {
      "$ref": "#/$defs/idArray"
    },
    "bardings": {
      "$ref": "#/$defs/idArray"
    },
    "cards": {
      "$ref": "#/$defs/idArray"
    },
    "fashions": {
      "$ref": "#/$defs/idArray"
    },
    "field_records": {
      "$ref": "#/$defs/idArray"
    },
    "frames": {
      "$ref": "#/$defs/idArray"
    },
    "hairstyles": {
      "$ref": "#/$defs/idArray"
    },
    "leves": {
      "$ref": "#/$defs/idArray"
    },
    "npcs": {
      "$ref": "#/$defs/idArray"
    },
    "occult_records": {
      "$ref": "#/$defs/idArray"
    },
    "orchestrions": {
      "$ref": "#/$defs/idArray"
    },
    "outfits": {
      "$ref": "#/$defs/idArray"
    },
    "relics": {
      "$ref": "#/$defs/idArray"
    },
    "spells": {
      "$ref": "#/$defs/idArray"
    },
    "survey_records": {
      "$ref": "#/$defs/idArray"
    },
    "beasts": {
      "$ref": "#/$defs/idArray"
    }
  },
  "$defs": {
    "idArray": {
      "type": "array",
      "maxItems": 50000,
      "uniqueItems": true,
      "items": {
        "type": "integer",
        "minimum": 1,
        "maximum": 4294967295
      }
    }
  },
  "additionalProperties": false
}
```

#### 21.4.3 Outfit and Relic Mapping Inputs

Canonical file: [data/FFXIVCollect/1.0/derived-inputs.schema.json](../data/FFXIVCollect/1.0/derived-inputs.schema.json)

Identical copies: [data/Contracts/1.1/resources/derived-inputs.schema.json](../data/Contracts/1.1/resources/derived-inputs.schema.json), [data/Contracts/1.2/resources/derived-inputs.schema.json](../data/Contracts/1.2/resources/derived-inputs.schema.json).

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/ffxiv-collect/1.0/derived-inputs.schema.json",
  "title": "Exportaramus FFXIV Collect Derived Inputs 1.0",
  "type": "object",
  "required": [
    "format",
    "schemaVersion",
    "upstreamRepository",
    "upstreamCommit",
    "verifiedAtUtc",
    "validatedGameDataVersion",
    "relationshipHashes",
    "outfits",
    "relics"
  ],
  "properties": {
    "format": {
      "type": "string",
      "const": "exportaramus-ffxiv-collect-derived-inputs"
    },
    "schemaVersion": {
      "type": "string",
      "const": "1.0"
    },
    "upstreamRepository": {
      "type": "string",
      "const": "https://github.com/skyborn-industries/ffxiv-collect"
    },
    "upstreamCommit": {
      "type": "string",
      "pattern": "^[0-9a-f]{40}$"
    },
    "verifiedAtUtc": {
      "type": "string",
      "format": "date-time",
      "pattern": "Z$",
      "maxLength": 64
    },
    "validatedGameDataVersion": {
      "type": "string",
      "minLength": 1,
      "maxLength": 128
    },
    "relationshipHashes": {
      "type": "object",
      "required": [
        "outfitsSha256",
        "relicsSha256"
      ],
      "properties": {
        "outfitsSha256": {
          "type": "string",
          "pattern": "^[0-9a-f]{64}$"
        },
        "relicsSha256": {
          "type": "string",
          "pattern": "^[0-9a-f]{64}$"
        }
      },
      "additionalProperties": false
    },
    "outfits": {
      "type": "array",
      "maxItems": 50000,
      "items": {
        "type": "object",
        "required": [
          "id",
          "requiredItemIds"
        ],
        "properties": {
          "id": {
            "$ref": "#/$defs/positiveId"
          },
          "requiredItemIds": {
            "type": "array",
            "minItems": 1,
            "maxItems": 11,
            "uniqueItems": true,
            "items": {
              "$ref": "#/$defs/positiveId"
            }
          }
        },
        "additionalProperties": false
      }
    },
    "relics": {
      "type": "array",
      "maxItems": 50000,
      "items": {
        "type": "object",
        "required": [
          "id",
          "achievementId"
        ],
        "properties": {
          "id": {
            "$ref": "#/$defs/positiveId"
          },
          "achievementId": {
            "oneOf": [
              {
                "$ref": "#/$defs/positiveId"
              },
              {
                "type": "null"
              }
            ]
          }
        },
        "additionalProperties": false
      }
    }
  },
  "$defs": {
    "positiveId": {
      "type": "integer",
      "minimum": 1,
      "maximum": 4294967295
    }
  },
  "additionalProperties": false
}
```

### 21.5 Diagnostics

#### 21.5.1 Diagnostic Report

Canonical file: [data/Diagnostics/1.1/exportaramus-diagnostics-v1.1.schema.json](../data/Diagnostics/1.1/exportaramus-diagnostics-v1.1.schema.json)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://exportaramus.local/schemas/diagnostics/1.1/exportaramus-diagnostics-v1.1.schema.json",
  "title": "Exportaramus Diagnostics Report 1.1",
  "type": "object",
  "required": [
    "format",
    "schemaVersion",
    "generatedAtUtc",
    "reportStatus",
    "fingerprintSha256",
    "incompleteSections",
    "build",
    "runtime",
    "lifecycle",
    "operations",
    "collectionPipeline",
    "sourceHealth",
    "storage",
    "ipc",
    "performance",
    "ui",
    "recentFailures",
    "redaction"
  ],
  "properties": {
    "format": {
      "type": "string",
      "const": "exportaramus-diagnostics"
    },
    "schemaVersion": {
      "type": "string",
      "const": "1.1"
    },
    "generatedAtUtc": {
      "$ref": "#/$defs/utcDateTime"
    },
    "reportStatus": {
      "type": "string",
      "enum": [
        "Ready",
        "Partial",
        "Failed"
      ]
    },
    "fingerprintSha256": {
      "$ref": "#/$defs/sha256"
    },
    "incompleteSections": {
      "type": "array",
      "maxItems": 11,
      "uniqueItems": true,
      "items": {
        "type": "string",
        "enum": [
          "build",
          "runtime",
          "lifecycle",
          "operations",
          "collectionPipeline",
          "sourceHealth",
          "storage",
          "ipc",
          "performance",
          "ui",
          "redaction"
        ]
      }
    },
    "build": {
      "type": "object",
      "required": [
        "pluginVersion",
        "buildConfiguration",
        "assemblyInformationalVersion"
      ],
      "properties": {
        "pluginVersion": {
          "$ref": "#/$defs/boundedText"
        },
        "buildConfiguration": {
          "type": "string",
          "enum": [
            "Debug",
            "Release"
          ]
        },
        "sourceRevision": {
          "type": [
            "string",
            "null"
          ],
          "maxLength": 128
        },
        "assemblyInformationalVersion": {
          "$ref": "#/$defs/boundedText"
        },
        "packageSha256": {
          "oneOf": [
            {
              "$ref": "#/$defs/sha256"
            },
            {
              "type": "null"
            }
          ]
        }
      },
      "additionalProperties": true
    },
    "runtime": {
      "type": "object",
      "required": [
        "dalamudApiVersion",
        "dotnetRuntimeVersion",
        "osVersion",
        "ffxivClientVersion",
        "gameDataVersion",
        "clientLanguage",
        "dependencies"
      ],
      "properties": {
        "dalamudApiVersion": {
          "$ref": "#/$defs/boundedText"
        },
        "dotnetRuntimeVersion": {
          "$ref": "#/$defs/boundedText"
        },
        "osVersion": {
          "$ref": "#/$defs/boundedText"
        },
        "ffxivClientVersion": {
          "$ref": "#/$defs/boundedText"
        },
        "gameDataVersion": {
          "$ref": "#/$defs/boundedText"
        },
        "clientLanguage": {
          "type": "string",
          "enum": [
            "Japanese",
            "English",
            "German",
            "French"
          ]
        },
        "dependencies": {
          "type": "object",
          "maxProperties": 32,
          "additionalProperties": {
            "$ref": "#/$defs/boundedText"
          }
        }
      },
      "additionalProperties": true
    },
    "lifecycle": {
      "type": "object",
      "required": [
        "state",
        "providerSessionId",
        "revision",
        "registrations",
        "ownedTasks",
        "cancellationRequested"
      ],
      "properties": {
        "state": {
          "type": "string",
          "enum": [
            "Constructed",
            "Running",
            "Stopping",
            "Disposed"
          ]
        },
        "providerSessionId": {
          "type": [
            "string",
            "null"
          ],
          "format": "uuid"
        },
        "revision": {
          "type": "integer",
          "minimum": 0,
          "maximum": 9007199254740991
        },
        "registrations": {
          "type": "object",
          "required": [
            "frameworkUpdate",
            "conditionChange",
            "command",
            "draw",
            "openMainUi",
            "openConfigUi",
            "ipcGates",
            "windows"
          ],
          "additionalProperties": {
            "type": "boolean"
          }
        },
        "ownedTasks": {
          "type": "array",
          "maxItems": 32,
          "items": {
            "type": "object",
            "required": [
              "name",
              "state"
            ],
            "properties": {
              "name": {
                "$ref": "#/$defs/boundedText"
              },
              "state": {
                "type": "string",
                "enum": [
                  "NotStarted",
                  "Waiting",
                  "Running",
                  "Completed",
                  "Cancelled",
                  "Faulted"
                ]
              }
            },
            "additionalProperties": true
          }
        },
        "cancellationRequested": {
          "type": "boolean"
        }
      },
      "additionalProperties": true
    },
    "operations": {
      "type": "object",
      "required": [
        "controllerState",
        "captureGeneration",
        "elapsedMilliseconds",
        "retryCount",
        "pendingRequestPriority",
        "staleCompletionRejected"
      ],
      "properties": {
        "controllerState": {
          "$ref": "#/$defs/boundedText"
        },
        "captureType": {
          "type": [
            "string",
            "null"
          ],
          "enum": [
            "Full",
            "Lightweight",
            "Restored",
            "Reported",
            null
          ]
        },
        "captureGeneration": {
          "type": "integer",
          "minimum": 0,
          "maximum": 9007199254740991
        },
        "elapsedMilliseconds": {
          "$ref": "#/$defs/nonnegativeNumber"
        },
        "deadlineAtUtc": {
          "oneOf": [
            {
              "$ref": "#/$defs/utcDateTime"
            },
            {
              "type": "null"
            }
          ]
        },
        "retryCount": {
          "type": "integer",
          "minimum": 0,
          "maximum": 1000
        },
        "pendingRequestPriority": {
          "type": "string",
          "enum": [
            "None",
            "AutomaticLightweight",
            "AutomaticFull",
            "ManualFull",
            "GuidedExport"
          ]
        },
        "staleCompletionRejected": {
          "type": "boolean"
        }
      },
      "additionalProperties": true
    },
    "collectionPipeline": {
      "type": "object",
      "required": [
        "catalogCache",
        "acceptedCaptureGeneration",
        "fingerprints",
        "serializers",
        "omissions",
        "outputBytes",
        "ffxivCollect"
      ],
      "properties": {
        "catalogCache": {
          "type": "object",
          "required": [
            "keyHash",
            "status",
            "buildMilliseconds"
          ],
          "properties": {
            "keyHash": {
              "oneOf": [
                {
                  "$ref": "#/$defs/sha256"
                },
                {
                  "type": "null"
                }
              ]
            },
            "status": {
              "type": "string",
              "enum": [
                "Empty",
                "Building",
                "Ready",
                "Failed",
                "Invalidated"
              ]
            },
            "buildMilliseconds": {
              "$ref": "#/$defs/nonnegativeNumber"
            }
          },
          "additionalProperties": true
        },
        "acceptedCaptureGeneration": {
          "type": "integer",
          "minimum": 0,
          "maximum": 9007199254740991
        },
        "fingerprints": {
          "type": "object",
          "maxProperties": 64,
          "additionalProperties": {
            "oneOf": [
              {
                "$ref": "#/$defs/sha256"
              },
              {
                "type": "null"
              }
            ]
          }
        },
        "serializers": {
          "type": "object",
          "required": [
            "compact",
            "expanded",
            "ffxivCollect"
          ],
          "not": {
            "required": [
              "compendium"
            ]
          },
          "additionalProperties": {
            "type": "string",
            "enum": [
              "NotBuilt",
              "Ready",
              "Unavailable",
              "Failed"
            ]
          }
        },
        "omissions": {
          "type": "array",
          "maxItems": 64,
          "items": {
            "type": "object",
            "required": [
              "sourceKey",
              "reasonCode"
            ],
            "properties": {
              "sourceKey": {
                "$ref": "#/$defs/sourceKey"
              },
              "reasonCode": {
                "$ref": "#/$defs/stableCode"
              }
            },
            "additionalProperties": true
          }
        },
        "outputBytes": {
          "type": "object",
          "required": [
            "compact",
            "expanded",
            "ffxivCollect"
          ],
          "not": {
            "required": [
              "compendium"
            ]
          },
          "additionalProperties": {
            "type": "integer",
            "minimum": 0,
            "maximum": 16777216
          }
        },
        "ffxivCollect": {
          "type": "object",
          "required": [
            "upstreamCommit",
            "verifiedAtUtc",
            "contractSha256",
            "mappingSha256",
            "includedKeyCount",
            "omittedKeyCount"
          ],
          "properties": {
            "upstreamCommit": {
              "type": "string",
              "pattern": "^[0-9a-f]{40}$"
            },
            "verifiedAtUtc": {
              "$ref": "#/$defs/utcDateTime"
            },
            "contractSha256": {
              "$ref": "#/$defs/sha256"
            },
            "mappingSha256": {
              "$ref": "#/$defs/sha256"
            },
            "includedKeyCount": {
              "type": "integer",
              "minimum": 0,
              "maximum": 16
            },
            "omittedKeyCount": {
              "type": "integer",
              "minimum": 0,
              "maximum": 16
            }
          },
          "additionalProperties": true
        }
      },
      "additionalProperties": true
    },
    "sourceHealth": {
      "type": "array",
      "maxItems": 64,
      "items": {
        "type": "object",
        "required": [
          "sourceKey",
          "capabilityState",
          "sourceStatus",
          "completeness",
          "provenanceType",
          "observationAgeSeconds",
          "captureMilliseconds",
          "reasonCode"
        ],
        "properties": {
          "sourceKey": {
            "$ref": "#/$defs/sourceKey"
          },
          "capabilityState": {
            "type": "string",
            "enum": [
              "Supported",
              "Degraded",
              "UnavailablePendingValidation"
            ]
          },
          "sourceStatus": {
            "type": "string",
            "enum": [
              "Fresh",
              "Stale",
              "Unavailable",
              "NeverObserved"
            ]
          },
          "completeness": {
            "type": "string",
            "enum": [
              "Complete",
              "Partial",
              "Unknown"
            ]
          },
          "provenanceType": {
            "type": "string",
            "enum": [
              "None",
              "Observed",
              "Restored",
              "Reported",
              "Derived"
            ]
          },
          "observationAgeSeconds": {
            "type": [
              "number",
              "null"
            ],
            "minimum": 0
          },
          "captureMilliseconds": {
            "$ref": "#/$defs/nonnegativeNumber"
          },
          "reasonCode": {
            "$ref": "#/$defs/stableCode"
          }
        },
        "additionalProperties": true
      }
    },
    "storage": {
      "type": "object",
      "required": [
        "canonicalAvailable",
        "historyFileCount",
        "historyBytes",
        "lastRead",
        "lastWrite",
        "recoveryState"
      ],
      "properties": {
        "canonicalAvailable": {
          "type": "boolean"
        },
        "canonicalBytes": {
          "type": "integer",
          "minimum": 0,
          "maximum": 16777216
        },
        "historyFileCount": {
          "type": "integer",
          "minimum": 0,
          "maximum": 25
        },
        "historyBytes": {
          "type": "integer",
          "minimum": 0,
          "maximum": 419430400
        },
        "lastRead": {
          "$ref": "#/$defs/ioResult"
        },
        "lastWrite": {
          "$ref": "#/$defs/ioResult"
        },
        "recoveryState": {
          "type": "string",
          "enum": [
            "None",
            "Pending",
            "Recovered",
            "Discarded",
            "Failed"
          ]
        }
      },
      "additionalProperties": true
    },
    "ipc": {
      "type": "object",
      "required": [
        "gatesRegistered",
        "payloadBytes",
        "changedNoticePending",
        "reportQueue",
        "reportCounters",
        "lastWithdrawalReason"
      ],
      "properties": {
        "gatesRegistered": {
          "type": "object",
          "maxProperties": 5,
          "additionalProperties": {
            "type": "boolean"
          }
        },
        "payloadBytes": {
          "type": "object",
          "required": [
            "capabilities",
            "snapshot",
            "pendingEvent"
          ],
          "additionalProperties": {
            "type": "integer",
            "minimum": 0,
            "maximum": 4194304
          }
        },
        "changedNoticePending": {
          "type": "boolean"
        },
        "reportQueue": {
          "type": "object",
          "required": [
            "depth",
            "capacity"
          ],
          "properties": {
            "depth": {
              "type": "integer",
              "minimum": 0,
              "maximum": 256
            },
            "capacity": {
              "type": "integer",
              "const": 256
            }
          },
          "additionalProperties": true
        },
        "reportCounters": {
          "type": "object",
          "required": [
            "accepted",
            "rejected",
            "throttled"
          ],
          "additionalProperties": {
            "type": "integer",
            "minimum": 0,
            "maximum": 9007199254740991
          }
        },
        "lastWithdrawalReason": {
          "type": [
            "string",
            "null"
          ],
          "enum": [
            "LoggedOut",
            "CharacterChanged",
            "IdentityUnavailable",
            "ProviderStopping",
            null
          ]
        }
      },
      "additionalProperties": true
    },
    "performance": {
      "type": "object",
      "maxProperties": 64,
      "additionalProperties": {
        "$ref": "#/$defs/performanceMetric"
      }
    },
    "ui": {
      "type": "object",
      "required": [
        "windowPosition",
        "windowSize",
        "contentWidth",
        "uiScale",
        "wideActions",
        "collectionColumnCount",
        "fontReady",
        "pushedStyleVarCount",
        "pushedColorCount",
        "reduceMotion",
        "viewStateRevision"
      ],
      "properties": {
        "windowPosition": {
          "$ref": "#/$defs/vector2"
        },
        "windowSize": {
          "$ref": "#/$defs/vector2"
        },
        "contentWidth": {
          "$ref": "#/$defs/nonnegativeNumber"
        },
        "uiScale": {
          "type": "number",
          "exclusiveMinimum": 0,
          "maximum": 16
        },
        "wideActions": {
          "type": "boolean"
        },
        "collectionColumnCount": {
          "type": "integer",
          "minimum": 1,
          "maximum": 3
        },
        "fontReady": {
          "type": "boolean"
        },
        "pushedStyleVarCount": {
          "type": "integer",
          "minimum": 0,
          "maximum": 64
        },
        "pushedColorCount": {
          "type": "integer",
          "minimum": 0,
          "maximum": 128
        },
        "reduceMotion": {
          "type": "boolean"
        },
        "viewStateRevision": {
          "type": "integer",
          "minimum": 0,
          "maximum": 9007199254740991
        }
      },
      "additionalProperties": true
    },
    "recentFailures": {
      "type": "array",
      "maxItems": 20,
      "items": {
        "type": "object",
        "required": [
          "occurredAtUtc",
          "component",
          "operation",
          "code",
          "exceptionType",
          "message",
          "frames"
        ],
        "properties": {
          "occurredAtUtc": {
            "$ref": "#/$defs/utcDateTime"
          },
          "component": {
            "$ref": "#/$defs/boundedText"
          },
          "operation": {
            "$ref": "#/$defs/boundedText"
          },
          "code": {
            "$ref": "#/$defs/stableCode"
          },
          "exceptionType": {
            "type": [
              "string",
              "null"
            ],
            "maxLength": 256
          },
          "message": {
            "type": "string",
            "maxLength": 512
          },
          "frames": {
            "type": "array",
            "maxItems": 32,
            "items": {
              "type": "object",
              "required": [
                "assembly",
                "type",
                "method"
              ],
              "properties": {
                "assembly": {
                  "$ref": "#/$defs/boundedText"
                },
                "type": {
                  "$ref": "#/$defs/boundedText"
                },
                "method": {
                  "$ref": "#/$defs/boundedText"
                },
                "sourceFileName": {
                  "type": [
                    "string",
                    "null"
                  ],
                  "maxLength": 128,
                  "pattern": "^[^/\\\\]*$"
                },
                "line": {
                  "type": [
                    "integer",
                    "null"
                  ],
                  "minimum": 1,
                  "maximum": 2147483647
                }
              },
              "additionalProperties": true
            }
          }
        },
        "additionalProperties": true
      }
    },
    "redaction": {
      "type": "object",
      "required": [
        "schemaVersion",
        "appliedRules",
        "droppedFieldCount",
        "truncatedFieldCount",
        "prohibitedDataAssertions"
      ],
      "properties": {
        "schemaVersion": {
          "type": "string",
          "const": "1.0"
        },
        "appliedRules": {
          "type": "array",
          "maxItems": 64,
          "uniqueItems": true,
          "items": {
            "$ref": "#/$defs/stableCode"
          }
        },
        "droppedFieldCount": {
          "type": "integer",
          "minimum": 0,
          "maximum": 9007199254740991
        },
        "truncatedFieldCount": {
          "type": "integer",
          "minimum": 0,
          "maximum": 9007199254740991
        },
        "prohibitedDataAssertions": {
          "type": "object",
          "required": [
            "characterIdentity",
            "collectionOrItemData",
            "retainerIdentity",
            "chat",
            "nativeAddresses",
            "processInputs",
            "fullPaths",
            "personalIdentifiers",
            "secrets",
            "clipboard"
          ],
          "properties": {
            "characterIdentity": {
              "type": "boolean",
              "const": false
            },
            "collectionOrItemData": {
              "type": "boolean",
              "const": false
            },
            "retainerIdentity": {
              "type": "boolean",
              "const": false
            },
            "chat": {
              "type": "boolean",
              "const": false
            },
            "nativeAddresses": {
              "type": "boolean",
              "const": false
            },
            "processInputs": {
              "type": "boolean",
              "const": false
            },
            "fullPaths": {
              "type": "boolean",
              "const": false
            },
            "personalIdentifiers": {
              "type": "boolean",
              "const": false
            },
            "secrets": {
              "type": "boolean",
              "const": false
            },
            "clipboard": {
              "type": "boolean",
              "const": false
            }
          },
          "additionalProperties": false
        }
      },
      "additionalProperties": true
    }
  },
  "$defs": {
    "utcDateTime": {
      "type": "string",
      "format": "date-time",
      "pattern": "Z$",
      "maxLength": 64
    },
    "sha256": {
      "type": "string",
      "pattern": "^[0-9a-f]{64}$"
    },
    "boundedText": {
      "type": "string",
      "minLength": 1,
      "maxLength": 256
    },
    "stableCode": {
      "type": "string",
      "pattern": "^[A-Za-z][A-Za-z0-9]{0,63}$"
    },
    "sourceKey": {
      "type": "string",
      "pattern": "^[A-Za-z][A-Za-z0-9_]{0,63}$"
    },
    "nonnegativeNumber": {
      "type": "number",
      "minimum": 0,
      "maximum": 3600000
    },
    "vector2": {
      "type": "object",
      "required": [
        "x",
        "y"
      ],
      "properties": {
        "x": {
          "type": "number",
          "minimum": -100000,
          "maximum": 100000
        },
        "y": {
          "type": "number",
          "minimum": -100000,
          "maximum": 100000
        }
      },
      "additionalProperties": false
    },
    "ioResult": {
      "type": "object",
      "required": [
        "status",
        "byteCount",
        "elapsedMilliseconds",
        "failureCode"
      ],
      "properties": {
        "status": {
          "type": "string",
          "enum": [
            "Never",
            "Succeeded",
            "Failed"
          ]
        },
        "byteCount": {
          "type": "integer",
          "minimum": 0,
          "maximum": 419430400
        },
        "elapsedMilliseconds": {
          "$ref": "#/$defs/nonnegativeNumber"
        },
        "failureCode": {
          "oneOf": [
            {
              "$ref": "#/$defs/stableCode"
            },
            {
              "type": "null"
            }
          ]
        }
      },
      "additionalProperties": true
    },
    "performanceMetric": {
      "type": "object",
      "required": [
        "sampleCount",
        "durationMilliseconds",
        "allocatedBytes"
      ],
      "properties": {
        "sampleCount": {
          "type": "integer",
          "minimum": 0,
          "maximum": 10000
        },
        "durationMilliseconds": {
          "$ref": "#/$defs/percentiles"
        },
        "allocatedBytes": {
          "$ref": "#/$defs/percentiles"
        }
      },
      "additionalProperties": true
    },
    "percentiles": {
      "type": "object",
      "required": [
        "p50",
        "p95",
        "maximum"
      ],
      "properties": {
        "p50": {
          "$ref": "#/$defs/nonnegativeNumber"
        },
        "p95": {
          "$ref": "#/$defs/nonnegativeNumber"
        },
        "maximum": {
          "$ref": "#/$defs/nonnegativeNumber"
        }
      },
      "additionalProperties": false
    }
  },
  "additionalProperties": true
}
```
