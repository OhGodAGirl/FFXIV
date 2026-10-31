# Exportaramus — Frontend Specification

## 1. Purpose

This document is the implementation guide for Exportaramus's main window and Diagnostics window. A frontend developer should be able to build both windows, connect them to a conforming backend, and reproduce their behavior without reading the existing frontend source. It specifies the data boundary, state calculations, user actions, drawing rules, and acceptance cases.

Basic C# knowledge is assumed; previous experience with FFXIV or Dalamud is not required. Current behavior is the intended result. Implement the rules below rather than adding controls suggested by old mockups or by unused names in the existing code. Screenshots show particular observations and window sizes, not fixed collection totals or required health states. Source links in [Section 15.3](#153-current-implementation-map) are optional evidence for reviewers; no implementation step depends on opening them.

The [Backend Specification](<Exportaramus - Backend Specification.md>) supplies the other half of the application: game reads, storage, exports, and native guided capture. This guide defines everything the frontend exchanges with that backend. It does not ask the frontend developer to implement game-memory reads or infer capture completion from visible game windows.

### 1.1 Terms

| Term | Meaning |
| --- | --- |
| FFXIV / Character | Final Fantasy XIV, the game hosting this plugin. A character is the player's in-game identity; observations belong to that character, not to the whole account. |
| Dalamud | The plugin framework running inside FFXIV. It supplies window management, fonts, commands, and UI callbacks. |
| ImGui | The UI library used to draw the plugin's windows and receive input each frame. |
| Collection / Catalog | A collection groups entries whose unlocks or item presence can be counted. A catalog defines the possible entries, IDs, and names; it does not establish which entries the character owns. |
| Source / Source Key | A source is a named group of data tracked separately, such as travel unlocks (`mounts`) or carried items (`inventory`). Its key is an exact, case-sensitive string naming that data, not the reader code. |
| Reader | The part of Exportaramus that obtains one kind of game data. An unavailable reader cannot establish whether a collection is empty. |
| Snapshot | Observations grouped together for one character. Individual sources may have been read at different times. A snapshot can exist in memory before, or without, a successful file save. |
| Accepted Publication | The backend's immutable internal result: a snapshot accepted after validation, its publication revision, and related prepared counts, history metadata, and export text. This is the input envelope in Section 3.5, not the public IPC response type. |
| Provenance | How evidence was obtained: observed in the game, restored from disk, reported by a plugin, or derived from other evidence. It is separate from support, freshness, and completeness. |
| Capture / Refresh | Capture reads and prepares observations. Refresh requests another capture; it does not rebuild or reload the plugin. |
| Consumer | Another plugin or application that uses Exportaramus data. |
| IPC | Inter-plugin communication: the local interface through which another Dalamud plugin requests data. |
| CollectionData / Inventory | Exportaramus's separate public data interfaces: collection evidence, and last-observed item quantities by storage location, respectively. Neither is the frontend's internal control interface. |
| FFXIV Collect | The external collection-tracking website. Copy JSON copies its prepared import format; the frontend does not upload it. |
| Retainer / NPC / Summoning Bell | A retainer is a game-controlled character (NPC) employed for separate item storage. A player can employ up to ten. A Summoning Bell is the game object used to access them. |
| Glamour Dresser / Armoire | The Glamour Dresser stores equipment appearances for applying to other equipment. The Armoire stores eligible clothing and gear. Guided capture opens these storage windows so their contents can be read. |
| Outfits / Lower Bound | Outfits are defined sets of required appearance items. A lower bound counts only sets whose required items are confirmed; `327+` means at least 327, with the complete total unknown. |
| Presenter | Code that converts backend results into display text, colors, and action availability. |
| Renderer | Code that draws the prepared display and reports button presses. It does not collect or interpret game data. |

Other collection names in the row table are game display labels. Implement their keys and count rules as specified; the frontend does not need to reproduce their gameplay mechanics. The [Consumer Integration Guide's concepts](<Consumer Integration Guide.md#21-providers-readers-and-consumers>) use the same source, reader, and observation terminology.

### 1.2 Find a Requirement

| Task | Sections |
| --- | --- |
| Build the frontend in order | [Implementation Order](#33-implementation-order), then [Startup and First Display](#142-startup-and-first-display) |
| Connect the backend | [Input Contract](#35-backend-input-contract), [Operations and Guided View](#36-backend-operations-and-guided-view), and [Display State](#37-display-state) |
| Calculate what players see | [Snapshot Summary](#8-snapshot-summary) and [Overall States](#9-overall-states) |
| Draw and route input | [Windows and Sizing](#4-windows-and-sizing), [Theme and Style](#6-theme-and-style), [Rendering and State Ownership](#14-rendering-and-state-ownership), and [Vector Drawing Reference](#16-vector-drawing-reference) |
| Connect guided capture or reports | [Guided Workflows](#10-guided-workflows) and [Diagnostics Window](#12-diagnostics-window) |
| Verify an implementation | [Acceptance Scenarios](#152-acceptance-scenarios) |

## 2. Current Visual Reference

![Local collection snapshots and sharing.](<assets/Exportaramus - Main Window.png>)

The current reference shows the main window at a size that uses two summary columns. Its counts, timestamps, reader states, and retainer message are illustrative. In particular, `23 Collections` counts the collection rows shown in this window; the backend supports 25 collection types. Retainers is additional storage coverage, not a collection type.

The screenshot's SHA-256 is `f934ffe53cafa73eb13d4091ca29bdd6225f6f832475c2240680b6db1980b764`. Preserve its pixels when updating documentation.

The [original mockup](<assets/Exportaramus - Frontend Mockup.png>) is historical reference only. Its header icon, status dropdown, View History action, and other superseded controls are not requirements. Preserve the original bytes: SHA-256 `e7a50cf022528cb946153f1540a8b940a92951a95c400081e93a8ba6f876338a`.

## 3. Functionality

### 3.1 Required

- Show collection counts, missing-data states, refresh time, retained snapshot count, and export availability for the active character.
- Start or cancel Retainers and Dresser & Armoire capture. Show progress, pauses, partial completion, and failures without hiding previously accepted data.
- Provide Sync Now and Copy JSON as separate actions. Refreshing reads data; copying uses an export already prepared by the backend.
- Explain source problems through the health tooltip and collection-row details. Do not equate unavailable or unobserved data with zero ownership.
- Follow the game's selected color theme and the host body-font size. Reflow panels and summary columns when the available width changes.
- Persist Reduce Motion, the only player-facing setting.
- Provide a separate Diagnostics window for generating and copying a bounded technical report.

### 3.2 Out of Scope

The main window has no character selector, item browser, inventory-location disclosure, history browser, settings page, consumer-management screen, or direct website upload. Collection rows explain results; clicking a row does not open another view. There is no header status dropdown or header product icon.

Theme selection belongs to FFXIV. Exportaramus reads that setting and does not change it. A plugin-listing icon is separate from the main-window header.

### 3.3 Implementation Order

Build in the following order. Each step has a result that can be checked before connecting to live game data.

| Step | Implement | Completion Check |
| --- | --- | --- |
| 1 | Host integration and a fake backend using Sections 3.4–3.6. | The command opens a window; closing and reopening it preserves position and size. |
| 2 | Immutable input and display records, then the presentation rules in Sections 8–9. | Fixed inputs produce the expected labels, counts, colors, and button availability without Dalamud. |
| 3 | Fonts, theme tokens, vector icons, measurement, panels, and buttons in Sections 4–6 and 11. | A static window matches the reference structure at the recorded size and font. |
| 4 | Main-window composition in Sections 7–8 and the draw sequence in Section 14. | Panels reflow; labels, values, tooltips, and scrolling remain usable. |
| 5 | Queued actions and guided views in Sections 10 and 14. | A fake capture can start, pause, continue, save partial data, cancel, and complete. |
| 6 | Diagnostics in Section 12. | A fake report can succeed, fail, be copied, and finish after closure without replacing the display. |
| 7 | Replace the fake backend with the adapter defined below. | The same frontend accepts real backend observations without moving game reads into drawing code. |
| 8 | Run the acceptance scenarios in Section 15. | Record the expected and actual output for each affected state and interaction. |

Keep the presenter, layout calculations, and action policies in ordinary C# types. Keep Dalamud window, font, clipboard, and drawing calls in the host layer. This separation lets most behavior be checked before entering the game.

### 3.4 Host Integration

Dalamud loads a plugin entry point implementing `IDalamudPlugin`. Create a `WindowSystem`, add the main and Diagnostics windows, and connect its `Draw()` method to `IDalamudPluginInterface.UiBuilder.Draw`. Connect the controller update to `IFramework.Update`. The framework callback advances work even when both windows are closed.

Use `ICommandManager` for `/exportaramus`. Connect both `UiBuilder.OpenMainUi` and `OpenConfigUi` to opening the main window. Opening means setting its open state to true; it does not toggle a visible window closed. The trimmed, case-insensitive argument `diagnostics` opens Diagnostics. Empty and other arguments open the main window. Section 13 gives the listing text.

The main window's stable host ID is `Exportaramus##Exportaramus.Main`, with `NoTitleBar`. Diagnostics uses `Exportaramus Diagnostics##Exportaramus.Diagnostics` with its normal host title bar. Text after `##` identifies a control or window without appearing in its visible label. Keep those IDs stable when action labels change.

Obtain the two Jupiter font handles from the host's managed font atlas. Draw body text with the host's current body font. Use the host's plugin configuration API for the initial Reduce Motion value, and a serialized configuration writer for subsequent saves. Do not block Draw on a save.

The frontend needs only these host facilities: windows and draw callbacks, a framework update callback, command registration, managed fonts, clipboard access, configuration storage, and the current game theme. Player identity, snapshot data, and native capture operations come through the backend adapter. The current host integration uses `Dalamud.Bindings.ImGui`; it is not a browser interface or a separate desktop application.

### 3.5 Backend Input Contract

Expose one immutable input frame to the frontend. This is a conceptual internal interface that the implementation must provide, not a supplied C# API or a new public IPC endpoint. An implementation may use records, interfaces, or equivalent types while preserving the values below. Public CollectionData IPC alone does not supply the history metadata, action controls, and guided state required here. The backend's [publication procedure](<Exportaramus - Backend Specification.md#91-publication-procedure>) and [frontend values](<Exportaramus - Backend Specification.md#93-counts-and-frontend-values>) define how accepted data reaches this boundary.

Types use C# notation: `?` allows null; an immutable map or list cannot be changed after the frame is published. Unless marked nullable, records, strings, and containers must be present. An empty map is distinct from a missing entry; a nullable count is distinct from numeric zero. Map keys use ordinal, case-sensitive comparison. Named state values below are exact values; the adapter may represent them as enums or validated strings.

#### 3.5.1 Input Frame

| Input | Type | Meaning |
| --- | --- | --- |
| Character available | `bool` | True when the backend has a nonzero active character identity. The frontend does not display that identity. |
| Refresh busy | `bool` | The collection controller is processing a refresh, independently of either guided workflow's active flag. |
| Refresh error | `string?` | Null means no refresh error. Any non-null value takes precedence over busy and snapshot status; an empty string is still an error value. |
| Accepted publication | Nullable publication record | Null means no accepted collection snapshot. Use revision 0, history count 0, absent refresh/history times, no export, and no collection rows in that case. Inventory remains a separate input. |
| IPC available | `bool` | Controls the health tooltip, not ownership or Copy JSON. The running provider currently supplies true after successful registration. |
| Dresser & Armoire | Guided-view record | Required even while idle; see [Section 3.6](#36-backend-operations-and-guided-view). |
| Retainers active / message | `bool` / `string` | Workflow activity and prepared progress, completion, cancellation, or failure text. Empty text hides the message area. The message may remain after activity ends. |
| Inventory summary | Inventory-summary record | Required even without an inventory snapshot; fields are defined below. |
| Reduce Motion | `bool` | Current in-memory setting, initially restored from configuration or false. Persistence failure does not revert it. |
| Current UTC time | `DateTimeOffset` | Time used to calculate observation ages. All timestamps supplied to this boundary identify UTC instants. |
| Monotonic seconds | `long` | Whole elapsed seconds for [presentation scheduling](#144-framework-update); unaffected by system-clock adjustment. |

#### 3.5.2 Accepted Publication

| Field | Type | Meaning |
| --- | --- | --- |
| Revision | `long` | Positive accepted-publication revision. It orders publications, not observation times. |
| Captured time / restored | `DateTimeOffset` / `bool` | Capture timestamp and whether this snapshot was restored from disk. Last Refresh uses this time, not an unsuccessful attempt's time. |
| Collections | Immutable map from collection key to collection record | Entry presence means the publication includes that collection. A missing key is not an empty collection. Use the 23 keys in [Section 8.1](#81-rows-and-order) for visible rows; additional keys do not add rows. |
| All source health | Immutable map from source key to source-health record | Includes sources without visible rows. Used for missing-collection reasons, overall completeness, and the full health tooltip. A missing health entry uses the explicit fallbacks in [Sections 8.4](#84-construct-the-rows) and [9](#9-overall-states). |
| Confirmed partial counts | Immutable map from collection key to nonnegative `int` | Backend-derived lower bounds. No key means no lower bound; zero means a confirmed lower bound of zero. The current visible use is `outfits`. |
| History count / latest time | `int` / `DateTimeOffset?` | Retained-entry count and most recent retained timestamp. Clamp the displayed count to 0–25; null time displays Never. History time can lag capture time. |
| Export JSON / copy-available | `string?` / `bool` | Accepted FFXIV Collect import text, or null when unavailable. Copy-available is true exactly when this validated text is present. Copy that string unchanged. |
| Export error | `string?` | Null means no export explanation. A non-null value, including an empty string, participates in the general-message precedence in [Section 9](#9-overall-states). Refresh can succeed while export is unavailable. |

Each collection record contains a nonnegative `int?` owned count, signed `int?` delta, ownership kind (`PermanentUnlock`, `CurrentItemPresence`, or `Derived`), and a required source-health record. Null count means no numeric result was supplied; null delta means no comparison was supplied. A zero delta is a real comparison. The backend prepares these counts; Draw does not calculate them from item arrays or serialized JSON. Use the collection record's health for its row, and the all-source map for the cross-source rules.

The publication's partial `outfits` count exists only when the complete outfits collection is absent, at least one CollectionData presence source is usable, and the outfits catalog is nonempty. It counts sets whose nonempty required-item lists are contained in the union of those usable sources; a computed zero is still supplied. This is separate from the inventory-derived override below, which can include individually observed retainer storage.

#### 3.5.3 Source Health

| Field | Type or Allowed Values | Meaning |
| --- | --- | --- |
| Data type | `PermanentUnlock`, `CurrentItemPresence`, `Derived`, `ReportedOwnership` | What kind of evidence the source holds. The closed-window exception tests this field. |
| Source status | `Fresh`, `Stale`, `Unavailable`, `NeverObserved` | Accepted as current under the source's rules; retained historical evidence; no usable observation available; or no observation yet obtained, respectively. |
| Completeness | `Complete`, `Partial`, `Unknown` | Required source coverage is complete, partial, or unknown. This describes coverage, not how the evidence was obtained. |
| Provenance | `None`, `Observed`, `Restored`, `Reported`, `Derived` | No evidence origin; read from the game; restored from disk; reported by a plugin; or calculated from other evidence. |
| Capability | `Supported`, `Degraded`, `UnavailablePendingValidation` | Implementation available; support limited; or support withheld pending review. None establishes that a usable observation exists. |
| Usable | `bool` | True exactly when status is Fresh or Stale, completeness is Complete, and capability is not UnavailablePendingValidation. It allows historical display; it does not prove current possession. |
| Reason code | Exact value listed below | Machine-readable explanation used in display decisions. |
| Reason text | `string` | Backend explanation; may be empty. Use it in the stale-row tooltip without interpreting it as a command or state. |
| Observation time | `DateTimeOffset?` | Underlying observation time, or null when unknown. Null formats as Never in row details. |

Reason codes are `None`, `NotLoadedThisSession`, `WindowNotReady`, `IncompleteContainerSet`, `IncompleteRetainerSet`, `MappingValidationFailed`, `PatchValidationRequired`, `RestoredFromDisk`, `CharacterUnavailable`, `IdentityUnavailable`, `QuantityOverflow`, `CaptureFailed`, `GameDataVersionUnsupported`, `OutputSizeLimitExceeded`, and `SerializerValidationFailed`. [Section 8.4](#84-construct-the-rows) specifies the fallback for any code without a dedicated display rule. Preserve capability, status, completeness, and provenance separately; `Degraded` alone cannot establish usable evidence. See [Backend Source Health](<Exportaramus - Backend Specification.md#42-source-health>) for observation acceptance and retention.

#### 3.5.4 Inventory Summary

| Field | Type | Meaning |
| --- | --- | --- |
| Has inventory snapshot | `bool` | Distinguishes no inventory snapshot from an existing snapshot whose retainer roster is unknown. It is independent of collection-publication presence and character-available status. |
| Roster known | `bool` | True only when the employed-retainer roster is known in the inventory snapshot. False when no snapshot exists. |
| Employed / observed retainers | `int` / `int` | Known employed locations, 0–10, and those with a prior successful observation, 0–employed. When the roster is unknown, these values do not establish coverage and the display is Unknown. |
| Confirmed outfit lower bound | `int?` | Nonnegative count, including zero. Null when the inventory snapshot, accepted collection publication, or outfits catalog is absent; otherwise supplies the inventory override in Section 8.4, even when a collection count already exists. |
| Inventory-cache failure | `bool` | True while the adapter reports a save or restore failure; adds the exact tooltip suffix in Section 8.4 even if no snapshot exists. |

The inventory adapter calculates retainer coverage from the current employed roster and retains successful observations regardless of age. It calculates the outfit lower bound from the union of item IDs in all observed inventory locations and usable CollectionData presence sources. An outfit is confirmed only when its nonempty required-item set is contained in that union. Perform these calculations outside Draw. The frontend does not turn retained observations into fresh ownership. The snapshot-presence and roster-known flags select distinct tooltip text in [Section 8.4](#84-construct-the-rows); neither is inferred from the ratio or from whether a character is available.

### 3.6 Backend Operations and Guided View

These are internal requests routed by the frontend's action controller, not public IPC function names. A request does not establish completion; the backend reports subsequent progress and results through later input frames. Revalidate requests using [Section 9.1](#91-action-availability) and route them using [Section 14.3](#143-accept-and-route-actions).

| Operation | Frontend Request | Backend Responsibility |
| --- | --- | --- |
| Full refresh | Request one full collection refresh. | Coalesce and execute the request; later supply busy, error, and accepted publication state. |
| Dresser & Armoire | Start, Continue, Save Partial, or Cancel. | Own the native sequence, validate its conditions, preserve completed observations, and publish its guided view. |
| Retainers | Start or Cancel. | Own bell/retainer navigation and cleanup; supply active state and message. |
| Copy collection JSON | Read the currently prepared export text. | Supply accepted text; copying does not request another read or export. |
| Save Reduce Motion | Save the newest boolean value. | Serialize writes and flush the last pending value during orderly disposal. |
| Generate diagnostic report | Request a report from immutable diagnostic metadata, with cancellation. | Produce the validated report and prepared rows described in [Section 12](#12-diagnostics-window). |
| Reset measurements | Clear current measurement samples and counters. | Preserve failure history and the already prepared report; a new report reflects the reset. |

The Dresser & Armoire view has these fields:

| Field | Type | Meaning |
| --- | --- | --- |
| `state` | Named state from the list below | Backend workflow state; also an input to presentation invalidation. |
| `stepLabel` | `string` | Backend step label, possibly empty. It is not a substitute for the displayed prompt. |
| `prompt` | `string` | Verbatim progress or pause explanation, possibly empty. It is display text, not a state to parse. |
| `active` | `bool` | False for Idle, Completed, Cancelled, and Failed; true for all other states. |
| `paused` | `bool` | True only for Paused or PausedWithPartial. |
| `canSavePartial` | `bool` | True when Dresser capture is complete and Armoire capture is incomplete. Show this action only inside a paused panel. |
| `dresserCompleted` / `armoireCompleted` | `bool` / `bool` | Backend-confirmed completion for each source. Counts alone cannot set these flags. |

Permitted states are `Idle`, `NeedDresser`, `RequestDresserInteraction`, `WaitForDresserReady`, `CaptureDresser`, `RequestDresserClose`, `WaitForDresserClosed`, `NeedArmoire`, `RequestArmoireInteraction`, `WaitForArmoireReady`, `CaptureArmoire`, `RequestArmoireClose`, `WaitForArmoireClosed`, `SaveFullSnapshot`, `SavePartialSnapshot`, `Completed`, `Paused`, `PausedWithPartial`, `Cancelled`, and `Failed`. The initial view is Idle, with step label `Ready`, an empty prompt, and all booleans false. State and flags must agree; the backend owns their transitions.

Render the supplied prompt verbatim. The workflow may wait for range, readiness, compatibility, a manual window action, or backend persistence. It can resume automatically when conditions permit. The frontend must not start its own timeout, advance a step because its label changed, or claim completion because both counts look populated. Native transitions and their timeouts are specified in [Backend Dresser and Armoire](<Exportaramus - Backend Specification.md#111-dresser-and-armoire>), [Retainers](<Exportaramus - Backend Specification.md#112-retainers>), and [Retainer Cleanup](<Exportaramus - Backend Specification.md#113-close-retainer-context-correctly>).

### 3.7 Display State

Prepare and replace one complete main-window display record using the following values. All display strings and lists are non-null; empty text hides an optional message, marker, or delta. This record is also an internal design contract, not a public response type.

| Display Value | Type and Meaning |
| --- | --- |
| Publication revision | `long`; accepted revision, or 0 without a publication. |
| Overall status / tone | One state in [Section 9](#9-overall-states), and its `Muted`, `Working`, `Success`, `Warning`, or `Error` tone. Tone selects the matching color in Section 6.3. |
| Last Refresh / history | Formatted relative-time strings, clamped `int` retained count, and formatted history lines from Sections 7.2 and 9. |
| Reduce Motion | `bool`; current in-memory setting. |
| Action availability | A `bool` for each control in [Section 9.1](#91-action-availability); cancellation and new-start availability must not share an unconditional busy check. |
| Health headline / tooltip | Strings prepared by Sections 7.3 and 9; headline is bounded, full tooltip is not. |
| Collection-row count / rows | `int` excluding Retainers, and an immutable ordered row list including Retainers as described below. |
| General message / tone | String and tone using Section 9's precedence; empty text hides the area. |
| Guided view | The complete record from Section 3.6. |
| Retainers active / message | `bool` and prepared string from the input frame; no frontend parsing of progress text. |

Each row contains `string` key, label, icon key, formatted count, marker, formatted delta, and tooltip; plus count and delta tones. Use the definitions in [Section 8.1](#81-rows-and-order) and icon keys in [Section 16.1](#161-icon-coordinates). Count tone selects Count or Muted; a displayed delta uses Success, Warning, or Error. Marker is either `!` or empty. Formatted count can be a number, lower bound, retainer ratio, or the explicit status text in [Section 8](#8-snapshot-summary); never parse it back into an ownership value.

Keep collection-row count separate from total displayed rows: there are 23 collection rows when a publication exists, zero before one exists, and one additional Retainers row in either case. The heading therefore reads `23 Collections` or `0 Collections`. Append Retainers after calculating that heading. Before the first publication, the summary contains only Retainers, normally `Unknown`; do not populate the other 23 rows with invented zeros.

A fresh display record prevents different panels from reading different publication revisions during one draw. The renderer reads that record once at frame start. Window visibility, position, scroll position, expanded diagnostic sections, hover, and keyboard focus remain local UI state.

## 4. Windows and Sizing

### 4.1 Main Window

Use one resizable window named `Exportaramus`, with a custom header and no host title bar. Retain the host's window position and size rather than resetting them when data changes.

| Property | Value |
| --- | --- |
| First-use size | 760 × 560 in the integrated application, including its initial Retainers-only summary |
| Minimum size | 680 × 360 |
| Maximum size | Unbounded |
| Background alpha | 0.995 |
| Inner window padding | 10 on each side |
| Normal inter-panel gap | 8 content units |
| Header-to-content gap | 8 content units after the subtitle |

Content units are derived from the active body font: `u = body font size / 17`. Multiply custom content positions and sizes by `u` once. The host manages window/DPI scaling; do not also multiply these coordinates by the global UI scale. Native style values and outer-frame strokes below are passed directly to ImGui. This distinction prevents doubled scaling.

Allow vertical scrolling when content exceeds the window. Do not resize or animate the window to accommodate a new message, a paused workflow, or a collection count.

### 4.2 Frame and Panels

Draw a rounded, nearly opaque surface with a fine gold frame and dark inset edges. Use a 7-pixel outer rounding. The custom frame has a 3-pixel shadow stroke, a 1-pixel gold line inset 2 pixels, and a 1-pixel inner shadow inset 4 pixels with 5-pixel rounding. The displaced outer shadow is offset by `(2, 3)` pixels.

Panels use a flat filled surface, 4-pixel corner rounding, a 1-pixel border, and a faint top highlight. Avoid large solid accent-color areas; headings, counts, icons, and small status marks provide emphasis.

### 4.3 Responsive Composition

Measure the available content width after window padding. Divide by `u` before applying these breakpoints:

| Content Width | Upper Panels | Initial Summary Columns |
| --- | --- | --- |
| 720 or more | Guided Capture left; Storage & History above Snapshot Health right | 3 |
| 700–719 | Three panels stacked | 3 |
| 460–699 | Three panels stacked | 2 |
| Below 460 | Three panels stacked | 1 |

In the side-by-side arrangement, subtract the 8-unit gap, then allocate 54% to Guided Capture and 46% to the right column. The left panel and combined right panels have equal total height. In the stacked arrangement, retain the order Guided Capture, Storage & History, Snapshot Health.

Summary columns may reduce further to fit measured labels and values. A breakpoint alone must not force clipped text. Narrow effective widths can occur through larger body fonts even when the window meets its minimum size.

Below the upper panels, display the current general message, Dresser & Armoire progress or pause panel, retainer progress, then Snapshot Summary. Omit empty message areas. Wrap messages to the available width.

## 5. Header, Typography, and Icons

### 5.1 Header

Place `Exportaramus` at the upper left. Place a borderless close button at the upper right, with a 28 × 28-unit hit area and an 18 × 18-unit X centered in both directions. Its normal background is transparent; retain host hover and keyboard-focus feedback. Hover text is `Close`.

Closing the main window hides it. It does not cancel capture, clear data, or change automatic refresh settings. Cancellation has its own visible action while a guided workflow is active.

The subtitle is:

> Snapshot and synchronize your FFXIV collection data with other plugins.

Place it 2 units from the left edge, at the greater of 36 units below the header origin or 6 units below the measured title height. Wrap within the available width minus 4 units. Do not add an icon, state capsule, or duplicate health label beside the title.

### 5.2 Fonts

| Element | Font and Treatment |
| --- | --- |
| Product title | Dalamud-managed FFXIV Jupiter font, size 24; heading color |
| Panel headings | Jupiter, size 15; uppercase visible labels; heading color |
| Body, buttons, times, counts, details | Current host body font; do not force a separate body font |
| Counts | Right-aligned; count color when known, muted when unavailable |
| Status headline | Title Case; color and glyph reflect the state |

Use managed font handles and release them with the plugin. Measure the font actually in use before positioning text. Document section headings use Title Case even though on-screen panel headings are uppercase.

### 5.3 Icons

Use small vector line icons, consistently colored with the icon token. Standard row and action icons are 18 × 18 units. A button with an icon reserves 25 units before its label; center the combined icon-and-label group. Standard lines and circles use 1.4-pixel strokes; simple outlined boxes use 1 pixel. Icons remain supplementary to readable labels.

The storage icon is used for Armoire, Glamour Dresser, Retainers, and snapshot storage. Other collection icons follow Section 8. Do not substitute unrelated emoji or require external icon fonts for these controls.

Section 16 gives the vector coordinates. Use those definitions with the requested icon size; names such as `Storage` or `Trophy` are keys, not instructions to choose a visually similar library icon.

## 6. Theme and Style

### 6.1 Theme Selection

Read FFXIV's `ColorThemeType` no more than once per second during the framework update. A failed read retains the last palette. An absent or unknown value selects Dark. Apply one prepared palette to both custom drawing and ordinary ImGui controls so backgrounds, text, and hover states agree.

| ID | Theme | Base RGB | Foreground Family |
| --- | --- | --- | --- |
| 0 | Dark | 0.270, 0.275, 0.270 | Dark |
| 1 | Light | 0.940, 0.810, 0.650 | Light |
| 2 | Classic | 0.220, 0.100, 0.760 | Dark |
| 3 | Blue | 0.220, 0.320, 0.550 | Dark |
| 4 | Clear White | 0.780, 0.780, 0.800 | Light |
| 5 | Green | 0.270, 0.460, 0.160 | Dark |
| 6 | Grey | 0.300, 0.310, 0.320 | Dark |
| 7 | Clear Pink | 0.930, 0.630, 0.850 | Light |

All colors below use normalized RGB or RGBA values in the range 0–1. These surfaces approximate the game's theme; they do not reproduce its textured window assets.

### 6.2 Surface Transformation

Transform each source color in the next table as follows, preserving alpha:

1. For Dark, keep the source color.
2. Otherwise, if `r > 0.6`, `g > 0.6`, and `b > 0.5`, use `(0.20, 0.16, 0.20)` for a light theme; retain the source RGB for a dark theme.
3. Otherwise, if `r > b × 1.3` and `r > 0.4`, use `(0.41, 0.28, 0.045)` for a light theme or `(0.91, 0.76, 0.43)` for a dark theme.
4. Otherwise, calculate `offset = ((r + g + b) / 3 - 0.27)`. Negate it for a light theme, add it to each channel of the theme's base RGB, and clamp each result to 0–1.

| Token | Source RGBA |
| --- | --- |
| Window | 0.180, 0.182, 0.185, 0.99 |
| Panel | 0.190, 0.200, 0.208, 1 |
| Primary Text | 0.900, 0.890, 0.850, 1 |
| Raised Surface | 0.280, 0.282, 0.280, 1 |
| Alternate Row | 0.225, 0.230, 0.235, 0.92 |
| Hovered Frame | 0.370, 0.370, 0.365, 1 |
| Active Frame | 0.420, 0.415, 0.400, 1 |
| Hovered Button | 0.380, 0.365, 0.320, 1 |
| Active Button | 0.470, 0.420, 0.320, 1 |
| Light Table Border | 0.360, 0.360, 0.350, 0.75 |
| Scrollbar Background | 0.130, 0.135, 0.140, 0.90 |
| Scrollbar Grab | 0.380, 0.375, 0.350, 1 |
| Collapsible Header | 0.350, 0.310, 0.235, 0.90 |
| Hovered Header | 0.460, 0.390, 0.270, 1 |
| Active Header | 0.540, 0.440, 0.280, 1 |
| Text Selection | 0.550, 0.440, 0.240, 0.55 |

### 6.3 Foregrounds and Accents

These values are used directly, after the surface transformation. Alpha is 1 unless shown.

| Token | Dark Themes RGB | Light Themes RGB |
| --- | --- | --- |
| Heading | 0.960, 0.860, 0.630 | 0.340, 0.230, 0.035 |
| Secondary / Muted Text | 0.860, 0.840, 0.800 / 0.820, 0.810, 0.780 | 0.200, 0.160, 0.200 for both |
| Icon | 0.910, 0.760, 0.430 | 0.410, 0.280, 0.045 |
| Border | 0.720, 0.620, 0.420 | 0.480, 0.400, 0.270 |
| Gold Frame / Focus | 0.780, 0.639, 0.353 | 0.460, 0.330, 0.100 |
| Count | 1.000, 0.890, 0.310 | 0.360, 0.250, 0.015 |
| Success | 0.514, 0.894, 0.478 | 0.055, 0.320, 0.100 |
| Warning | 0.960, 0.830, 0.550 | 0.430, 0.230, 0.025 |
| Error | 0.990, 0.820, 0.780 | 0.570, 0.075, 0.055 |
| Panel Highlight | 0.400, 0.380, 0.320; alpha 0.35 | 1, 1, 1; alpha 0.40 |
| Button Highlight | 0.650, 0.600, 0.480; alpha 0.40 | 1, 1, 1; alpha 0.60 |

Shadow is `(0.027, 0.027, 0.027, 0.80)` in every theme. Working-state text uses Heading. Unknown-state text uses Muted. The summary's fine row separator uses `(0.29, 0.278, 0.235, 0.70)`.

### 6.4 ImGui Style

Use window rounding 7, child rounding 4, frame and scrollbar rounding 3, window border size 0, window padding `(10, 10)`, frame padding `(7, 4)`, and cell padding `(8, 5)`.

| ImGui Surface | Token |
| --- | --- |
| Text / disabled text | Primary / Muted |
| Window / child / popup backgrounds | Window / Panel / Raised |
| Border / shadow / separator | Border / Shadow / Border |
| Title normal and collapsed / active | Window / Raised |
| Frame normal / hovered / active | Raised / Hovered Frame / Active Frame |
| Button normal / hovered / active | Raised / Hovered Button / Active Button |
| Checkbox mark | Heading |
| Table header / strong border / light border | Raised / Border / Light Table Border |
| Table row / alternate row | Panel / Alternate Row |
| Scrollbar background / grab / hovered / active | Scrollbar Background / Scrollbar Grab / Border / Gold |
| Collapsible header normal / hovered / active | Corresponding Header token |
| Selected text background | Text Selection |

Leave other host styles unchanged. Restore every style pushed for a window, including after an early return or exception; otherwise another plugin's UI can inherit Exportaramus colors or spacing.

## 7. Upper Panels and Actions

### 7.1 Guided Capture

Heading: `GUIDED CAPTURE`.

Show a 24-unit clock at `(16, 46)`, `Last Refresh` at `(52, 42)`, and its relative time at `(52, 64)`, measured from the panel origin. Show Reduce Motion with the helper `Disable nonessential animation` 26 units below its checkbox.

Normally place the checkbox at 48% of the panel width and y=40. If the panel width is less than the measured helper width plus the measured `Last Refresh` width plus 100 units, place it at `(16, 90)` instead. Panel height is 188 units normally or 232 with the information stacked.

Place Retainers on the left and Dresser & Armoire on the right. Each button is 36 units high. Retainers uses the storage icon; idle Dresser & Armoire uses the capture icon. While active, their labels become `Cancel Retainers` and `Cancel Capture` respectively.

Stack the buttons if two widest required button widths plus 32 units do not fit. A labeled button requires its measured text width plus 24 units, or plus 49 with an icon. Stacking adds 44 units to the panel height. Side-by-side widths are `(panel width - 32) / 2`, with an 8-unit gap; stacked widths are `panel width - 24`. In either layout, use 12-unit side insets, a rule 10 units above the action row, and 12 units below the final button.

The display must keep cancellation available while its workflow is active. Retainers and Dresser & Armoire cannot start together, because each navigates game windows. Starting either requires a character and the availability calculation in [Section 9.1](#91-action-availability). That calculation uses overall Synchronizing status after error precedence, not the raw refresh-busy input alone.

### 7.2 Storage & History

Heading: `STORAGE & HISTORY`.

Show a 27-unit storage icon at `(16, 44)`, `<n> Snapshots Retained` at `(52, 40)`, and `Latest: <relative time>` at `(52, 62)`. Clamp the retained count to 0–25. The latest time refers to a retained history entry; it can be older than Last Refresh when a new read found no meaningful change.

Sync Now is 30 units high and inset 12 units from the right. Normally its y-position is 42 and panel height is 88. If the longest history line plus the button width plus 76 units does not fit, put the button at y=86 and use height 126.

Sync Now requests a full refresh. Disable it without a character, while overall status is Synchronizing, during Dresser & Armoire capture, or during retainer capture, following [Section 9.1](#91-action-availability). It does not copy JSON. There is no View History button.

### 7.3 Snapshot Health

Heading: `SNAPSHOT HEALTH`.

Place a 17-unit-radius outlined status circle at `(30, 50)` and the headline at `(60, 40)`. Use a checkmark for success and an exclamation mark for other states. Match the glyph and headline to the prepared state color; the text explains the distinction that color alone cannot convey.

Copy JSON has a copy icon, is 30 units high, and is inset 12 units from the right. Normally its y-position is 34 and panel height is 80. If the headline width plus the button width plus 80 units does not fit, put the button at y=66 and use height 108. In a side-by-side upper layout, this panel may expand vertically to match the left column.

Enable Copy JSON only when a character is active and the backend has a prepared, valid FFXIV Collect export. Copying does not start another capture or open the website. Display the backend's export-unavailable explanation in the general message area when supplied. Do not synthesize an export from visible counts.

Hovering the headline or status glyph opens the health tooltip, wrapping at 420 units. Include:

1. Observation state: `Not observed`, `Restored snapshot`, `Observed just now`, or `Snapshot retained`.
2. `Collector errors` or `No collector errors`.
3. One `Reader Unavailable - <source name>` line for each reader awaiting compatibility validation, ordered by source key.
4. `IPC Available` or `IPC Unavailable`.

Select the observation line from publication state: no publication → `Not observed`; restored → `Restored snapshot`; otherwise capture age below one minute → `Observed just now`; otherwise → `Snapshot retained`. Future capture times also fall into the below-one-minute branch. Use the capture timestamp, not the newest individual source time. [Section 9](#9-overall-states) defines the separate collector-error test.

Use the collection labels in [Section 8.1](#81-rows-and-order), plus these additional source-name mappings. The distinct keys `armoires` / `armoire` and `glamour_dresser` / `glamourDresser` identify collection metadata and storage-presence metadata respectively; do not rename or merge their input entries.

| Source Key | Display Name |
| --- | --- |
| `facewear` | Facewear |
| `beasts` | Beasts |
| `inventory` | Inventory |
| `armoryChest` | Armoury Chest |
| `chocoboSaddlebag` | Chocobo Saddlebag |
| `premiumSaddlebag` | Premium Saddlebag |
| `retainers` | Retainers |
| `armoire` | Armoire |
| `glamourDresser` | Glamour Dresser |

Fall back to the exact source key only for an unrecognized source. Order unavailable-reader lines by ordinal source key, not translated display name. Do not repeat these details as permanent rows below the headline.

## 8. Snapshot Summary

### 8.1 Rows and Order

Heading: `SNAPSHOT SUMMARY`, followed by the number of displayed collection rows. The current main window has 23 collection rows, plus Retainers. Facewear and Beasts remain available in backend data without separate main-window rows.

Read the following order across each row, then downward. At three columns, Achievements and Retainers are the final middle and right cells. At two columns, the same order ends with Achievements on the left and Retainers on the right.

| Order | Label | Source Key | Icon |
| --- | --- | --- | --- |
| 1 | Aetherytes | `aetherytes` | Diamond |
| 2 | Armoire | `armoires` | Storage |
| 3 | Bardings | `bardings` | Shield |
| 4 | Triple Triad Cards | `cards` | Cards |
| 5 | Chocobo Routes | `chocobo_routes` | Route |
| 6 | Emotes | `emotes` | Smile |
| 7 | Fashion Accessories | `fashions` | Glasses |
| 8 | Field Records | `field_records` | List |
| 9 | Portrait Frames | `frames` | Frame |
| 10 | Glamour Dresser | `glamour_dresser` | Storage |
| 11 | Hairstyles | `hairstyles` | Head |
| 12 | Levequests | `leves` | Scroll |
| 13 | Minions | `minions` | Pet |
| 14 | Mounts | `mounts` | Mount |
| 15 | Triple Triad NPCs | `npcs` | Cards |
| 16 | Occult Records | `occult_records` | Book |
| 17 | Orchestrion Rolls | `orchestrions` | Music |
| 18 | Outfits | `outfits` | Shirt |
| 19 | Quests | `quests` | Quest |
| 20 | Relics | `relics` | Relic |
| 21 | Blue Magic Spells | `spells` | Book |
| 22 | Variant Dungeon Records | `survey_records` | Dungeon |
| 23 | Achievements | `achievements` | Trophy |
| Additional | Retainers | `retainers` | Storage |

Retainer coverage is `x/y`: x is the number of employed retainers with an observation; y is the known employed roster size, from zero through ten. A known empty roster is `0/0`; an unknown roster is `Unknown`. Saved observations count toward x even when not fresh. Its tooltip must say that these are last-observed inventories and that Retainers refreshes them. Do not imply that `4/4` proves four inventories were read during the current run.

There is no separate Retainers line or Storage Locations disclosure below the grid.

### 8.2 Grid Geometry

The summary height is `36 + row count × 28` units. Start the bordered column panels 8 units from the left and 30 units below the summary origin; leave 8 units between columns. Column width is `(available width - 16 - 8 × (columns - 1)) / columns`.

Calculate the minimum cell width from every label, count, stale marker, and delta, plus 72 units. Reduce the initial column count until every cell fits. Keep the original ordering; do not sort by count or status.

Each row is 28 units high. Place its icon at `(10, 5)`, label at `(38, 5)`, and values against the right edge with 10 units of padding. Alternate row fill on every other row; use a fine separator at y=27. Align counts and deltas separately so changing numbers do not move labels.

### 8.3 Values and Meaning

| Display | Meaning and Detail |
| --- | --- |
| `0` | A complete usable observation found no owned entries. |
| A number | The backend's owned count, using comma thousands separators. |
| `327+` | At least 327 outfits are confirmed; unread or retained storage prevents an exact current total. It is not a complete total for export. |
| `!` before a number | Last-known data is retained after a stale read; the row tooltip explains why. |
| `Waiting` | Required game data or a window has not finished loading. Explain which window to open when known. |
| `Incomplete` | Required storage has not been completely observed. |
| `Unavailable` | The reader is not enabled for this game build. |
| `Error` | Capture, mapping, quantity, or output-size validation failed. Do not substitute zero. |
| `—` | No count or more specific display status was supplied. |

A dresser or armoire count retained after its window closes is an expected last observation. Do not add a stale warning solely for that closure; its tooltip states when it was captured and that the window is now closed.

Display a delta only when the backend supplies one. Positive and zero deltas use success color and an explicit `+`; negative current-item-presence deltas use warning color because items can legitimately leave storage. Negative permanent-unlock or derived deltas use error color to flag a discrepancy. Missing, incomplete, and lower-bound outfit results have no invented delta.

Hover anywhere in a row to show its prepared detail. The whole row is a hover target, not an action. Retain readable names and text markers so status remains understandable without color.

### 8.4 Construct the Rows

When there is an accepted publication, visit all 23 definitions in Section 8.1. Keep their order even if some entries are absent from the publication.

1. If the collection is present, use its prepared owned count and delta. A null count produces `—`, a muted count tone, and no marker or delta. A known count uses invariant thousands separators. Use `!` only for stale data that does not meet the closed-window exception below.
2. If the collection is absent, obtain its source-health reason and use the table below. Do not assign a numeric count or delta. If a confirmed partial outfit count exists, show that number followed by `+` instead, with no delta and no stale marker at this stage.
3. Apply the inventory-derived outfit lower bound, when supplied. It replaces the Outfits count with `<n>+`, clears its delta, and supplies the inventory explanation below. Preserve the row's existing count tone and marker. Thus an inherited stale marker can remain next to a lower bound.
4. Append Retainers with no marker or delta. Its known ratio uses the count color; `Unknown` uses muted text. Do this even when there is no accepted publication.

| Missing-Source Reason | Display | Tooltip |
| --- | --- | --- |
| `NotLoadedThisSession` | Waiting | `Open Achievements and wait for it to finish loading.` for achievements; otherwise `Open the collection window and wait for it to finish loading.` |
| `WindowNotReady` | Waiting | `Open the Glamour Dresser and keep it open until capture completes.`, the same wording with `the Armoire` for armoires, or `the required window` for other keys. |
| `IncompleteContainerSet`, `IncompleteRetainerSet` | Incomplete | `Required storage has not been completely observed. Missing data is not an empty collection.` |
| `PatchValidationRequired`, `GameDataVersionUnsupported` | Unavailable | `This reader is not enabled for the current game build.` |
| `MappingValidationFailed`, `CaptureFailed`, `QuantityOverflow`, `OutputSizeLimitExceeded` | Error | `Capture failed: <reason code>. The collection has not been treated as empty.` |
| Missing health or any other reason | Waiting | `Not captured yet. Use Sync Now or open the required collection window.` |

The closed-window exception requires all four conditions: Current Item Presence data, Stale status, Observed provenance, and `WindowNotReady`. It preserves the count without a stale marker. The tooltip is `Captured <relative age>; window now closed. Last observed count retained.` Use the lowercase relative-age text from Section 9.

For another stale observation, use `Last-known count retained. <backend reason>`. For a non-stale observation, use `Captured <relative age>. Complete read; zero means no owned entries.` A backend-derived partial outfit count uses `At least <n> outfits confirmed from observed storage. Other storage is missing; this is not a complete total and is omitted from JSON.` The inventory override uses `At least <n> outfits confirmed from last-observed storage, including individually captured retainers. Unread or retained storage prevents a current exact total.`

For Retainers, start with `Retainers: <x>/<y> captured` when the roster is known, `Retainer list not yet observed` when inventory exists but the roster is unknown, or `Waiting for character` when there is no inventory snapshot. Append ` · Inventory cache could not be saved or restored; retry pending.` when the adapter reports that failure. End the tooltip with `. Counts include last-observed inventories; run Retainers to refresh.` These are tooltip details; do not reintroduce a storage-location panel.

## 9. Overall States

The presenter determines overall status in this order: no character, refresh error, synchronization active, no accepted snapshot, then complete or partial snapshot.

| State | Health Headline | Tone |
| --- | --- | --- |
| No character | Waiting for Character | Muted |
| No accepted snapshot | No Snapshot Available | Muted |
| Refresh active | Synchronizing | Working |
| Accepted capture complete | Capture Complete | Success |
| Accepted capture partial | Some Data Unavailable | Warning |
| Refresh failed | Refresh Failed | Error |

A restored snapshot is partial until refreshed. A usable source that is degraded, unexpectedly missing, or stale for a reason other than closing a storage window prevents a complete result. Capture, mapping, quantity, and output-size failures also prevent completion.

Use this exact completeness procedure after the higher-priority no-character, error, busy, and no-publication branches:

```text
if publication is restored or its collection map is empty:
    incomplete
for each of the 23 displayed collection definitions:
    if there is no source-health record:
        continue
    if reason is CaptureFailed, MappingValidationFailed,
       QuantityOverflow, or OutputSizeLimitExceeded:
        incomplete
    if capability is UnavailablePendingValidation or source is not usable:
        continue
    if collection is absent or capability is Degraded:
        incomplete
    if source is Stale and does not meet the closed-window exception:
        incomplete
otherwise:
    complete
```

The health tooltip's `Collector errors` test is separate: any source with CaptureFailed, MappingValidationFailed, or QuantityOverflow sets it, including sources outside the 23 rows. OutputSizeLimitExceeded can make the headline partial without setting that tooltip line. Preserve this distinction rather than deriving all health text from one boolean.

Capture completion and reader coverage are separate. A reader disabled pending compatibility validation does not by itself make an otherwise accepted capture fail; the health tooltip still identifies that reader. Do not infer whole-catalog coverage from the success headline.

When an operation fails, retain accepted counts and show the error separately. Never clear them to zero or replace the dashboard with an error modal. General messages and health headlines are prepared outside drawing; ordinary general display text is bounded to 160 characters.

Relative times use `Never` when absent, `Just now` below one minute, then whole minutes below one hour, whole hours below one day, and whole days thereafter. Refresh labels once per second, and refresh immediately when the accepted revision, a user intent, or the Dresser & Armoire workflow state changes.

Truncate elapsed minutes, hours, or days toward zero. The literal labels use `minutes ago`, `hours ago`, and `days ago`, including a value of one. Future timestamps fall into `Just now`. Format numbers in collection counts with invariant separators; an outfit lower-bound override uses the supplied integer followed by `+`.

General message precedence is refresh error, then export error, then empty text. Truncate general messages and health headlines to 160 C# string characters. Do not apply that truncation to the full health tooltip, source explanations, or guided messages. General-message tone follows overall status, except an active Dresser & Armoire workflow uses Working. Retainer activity alone does not change the overall status or message tone.

### 9.1 Action Availability

Calculate `busy = (overall status == Synchronizing) OR Dresser & Armoire active`. A refresh error takes precedence over Synchronizing in the status calculation. Then use these rules:

| Control | Enabled When | Result |
| --- | --- | --- |
| Close / Reduce Motion | Always | Hide the main window / toggle the setting. |
| Sync Now | Character available, not busy, and Retainers inactive | Request a full refresh. |
| Retainers | Retainers active, or character available with neither busy nor Dresser & Armoire active | Cancel the active retainer workflow or start one. |
| Dresser & Armoire | Its workflow active, or character available with neither busy nor Retainers active | Cancel its active workflow or start one. |
| Copy JSON | Character available and prepared export copy-available | Copy the accepted export. Busy or partial status alone does not disable it. |
| Continue / Cancel in pause panel | Panel visible | Send Continue / Cancel to the existing workflow. |
| Save Partial | Panel visible and Dresser completed while Armoire incomplete | Send Save Partial. |

Cancellation remains available without a character while the corresponding workflow still reports active. Revalidate actions when processing them; a button can have been drawn just before the backend changed state. In particular, ignore Sync Now and new Dresser & Armoire requests while Retainers is active. Do not automatically execute a disabled action later when its conditions improve.

## 10. Guided Workflows

### 10.1 Dresser & Armoire

The button starts the backend's [Dresser and Armoire sequence](<Exportaramus - Backend Specification.md#111-dresser-and-armoire>). Show its actual prompt below the upper panels while active. Change the initiating button to Cancel Capture; a click requests cancellation rather than a second run.

When the sequence pauses, place an inline Guided Capture panel below the upper panels. It is not a modal overlay and does not trap keyboard focus. Place the wrapped explanation at `(12, 36)` beneath the heading at `(12, 10)`. Height is the explanation's wrapped height plus 90 units.

The bottom action row contains Continue (120 × 32), optionally Save Partial (120 × 32), and Cancel (100 × 32). Save Partial is offered only after Dresser capture completes while Armoire remains incomplete. It retains completed observations and ends the unfinished workflow; it does not fabricate the missing source.

Move keyboard focus to Continue when the pause panel first appears. When the pause ends, return focus to the Dresser & Armoire action. Cancellation retains completed observations. Backend state, not text matching, determines whether Continue or Save Partial is meaningful.

### 10.2 Retainers

Retainers starts the backend's [retainer sequence](<Exportaramus - Backend Specification.md#112-retainers>) at a nearby Summoning Bell and visits the known employed roster, up to ten. Render the supplied retainer message verbatim below the upper panels, including after activity ends when it is nonempty. During a retainer visit, that message includes the action and one-based roster position, such as `Capturing inventory (2/4)`. Do not parse it into additional state or invent progress text. This position is distinct from the summary's count of successfully observed inventories. The backend supplies the actual roster total; never assume ten or use the last character's roster.

Keep Cancel Retainers available throughout capture. Other guided capture and Sync Now remain unavailable while it owns the game-window sequence. Cancellation stops further navigation and preserves inventories already observed.

Completion includes closing inventory interactions and the retainer overview through the backend's [cleanup sequence](<Exportaramus - Backend Specification.md#113-close-retainer-context-correctly>). Simply hiding windows can leave the game's Entrust interaction (the mode for transferring items to a retainer) active and produce invalid inventory actions later. The UI must report a cleanup failure or timeout rather than claiming completion based only on `x/y` coverage. The backend owns dialogue advancement, deadlines, and teardown; the renderer only displays the result.

## 11. Interaction and Accessibility

### 11.1 Buttons and Focus

Use ordinary ImGui controls for input, with custom drawing for labels and borders. Give every control a stable, unique identifier distinct from its visible label; changing Cancel Capture back to Dresser & Armoire must not collide with another control.

| State | Treatment |
| --- | --- |
| Idle | Raised fill, fine border, faint top highlight |
| Hovered | Hovered-button fill and gold rim |
| Held | Active-button fill; label and icon offset downward 1.5 pixels unless Reduce Motion is enabled |
| Keyboard focused | Gold rim, 2-pixel stroke |
| Disabled | Host-disabled input; muted label, retained icon and label |
| Cancel Capture and pause-panel Cancel | Warning-colored label; no solid red fill |

Cancel Retainers retains the standard label and storage-icon treatment.

Keep action targets at least 28 × 28 content units. Preserve the visible label for checkboxes and buttons; the close action has its X and Close tooltip. Support host keyboard navigation. Do not hide focus or use color as the only status signal.

Normal text should retain at least 4.5:1 contrast against its surface, and meaningful non-text indicators at least 3:1. Verify these targets for the prepared theme palette and actual rendering, rather than treating the presence of color tokens as proof. Larger fonts must trigger wrapping or fewer columns before they overlap adjacent controls.

### 11.2 Reduce Motion

Persist the checkbox through the backend's configuration writer. It affects visual feedback only. It must not change capture timing, retries, automatic refresh intervals, or completion conditions.

Current working-state emphasis is static. The only explicit motion is the held-button displacement; remove that displacement when Reduce Motion is enabled. Do not add pulsing counters, animated rows, moving panels, or automatic window-size animation.

### 11.3 Tooltips

Approved visible tooltips are the close label, source-specific Snapshot Health details, and collection-row details. The centralized generic action-tooltip provider currently returns no text for Reduce Motion, Sync Now, Copy JSON, Dresser & Armoire, Continue, Save Partial, and Cancel. Retainers has no action tooltip. Do not present unapproved placeholder copy as implemented behavior.

New explanatory tooltips can be reviewed separately. Their absence does not change action semantics, and they must not perform reads or work when hovered.

## 12. Diagnostics Window

Open Diagnostics with `/exportaramus diagnostics`. The main window has no Diagnostics button or secondary navigation. Opening the window requests a fresh report.

Use a separate resizable host-titled window, `Exportaramus Diagnostics`, initially 820 × 680 with a minimum of 620 × 440. Apply the same prepared palette, frame, and heading font. Prioritize wrapped technical fields over the main dashboard's composition.

### 12.1 Controls

| Control | Size | Behavior |
| --- | --- | --- |
| Generate Report | 160 × 32 | Start one report worker; disabled while that worker is active. |
| Copy Report | 145 × 32 | Copy the most recent prepared report; disabled before any report exists. |
| Reset Measurements | 200 × 32 | Reset recorded measurements without clearing collection data or starting a capture. |

These diagnostics action dimensions and its 590-pixel action-row breakpoint are currently passed directly to ImGui. Generate Report and Copy Report share a row. Reset Measurements joins them when the content width is at least 590; otherwise it starts the next row.

Show status (`Ready`, `Running`, `Partial`, or `Failed`), a wrapped error when present, then report generation time, encoded size, and fingerprint. A fingerprint is a digest used to compare report content; it is not an ownership count. If generation fails, show `Report generation failed. Generate a new report.` and retain any previously prepared report.

Closing Diagnostics cancels pending report generation. An old worker finishing after closure must not replace the current display. Collection synchronization continues independently.

### 12.2 Report Sections

Render the prepared report in collapsible sections:

| Section | Initially Open |
| --- | --- |
| Build and Runtime | No |
| Lifecycle and Operations | Yes |
| Collection Pipeline | No |
| Source Health | No |
| Storage and IPC | No |
| Performance | Yes |
| UI | Yes |
| Recent Failures | Yes |
| Redaction | No |

Each section uses a two-column table, allocating 46% to Field and 54% to Value. Wrap both columns and use alternating row backgrounds. Field paths retain their technical spelling so a developer can match them to the report JSON.

New reports use diagnostic format 1.1. Under Collection Pipeline, display `collectionPipeline.serializers.expanded` and `collectionPipeline.outputBytes.expanded` exactly as supplied. The first reports Expanded Snapshot serialization status; the second reports its encoded size. Diagnostic format 1.0 is retired. There is no report-import control; Generate Report produces a current report. Preserve the spelling and JSON supplied by the prepared report; do not rename fields while rendering or copying it.

The backend prepares and validates a report no larger than 256 KiB. It contains technical status and measurements, not character names, item contents, credentials, or clipboard contents. Render and copy the same accepted report; drawing must not reconstruct it from live services.

### 12.3 Report Data and Interaction

The diagnostics display record contains `running`, `status`, `error`, optional prepared report, and Reduce Motion. The prepared report contains its accepted JSON, UTF-8 byte count, UTC generation time, fingerprint, and ordered sections. Each section contains a heading, initial-open flag, stable table ID, and ordered `(label, value)` rows. Generate is enabled when `running` is false; Copy is enabled whenever a prepared report exists; Reset Measurements is always enabled.

Show `Generated: <UTC time>` using `yyyy-MM-dd HH:mm:ssZ`, `Report size: <formatted byte count> bytes`, and `Report fingerprint: <digest>`. The report-size count uses the host culture's thousands separator. A report whose status is Partial still renders and can be copied. Keep the previous report visible and copyable while generating its replacement and after generation failure.

Build the sections from these root JSON properties, in the listed order: Build and Runtime → `build`, `runtime`; Lifecycle and Operations → `lifecycle`, `operations`; Collection Pipeline → `collectionPipeline`; Source Health → `sourceHealth`; Storage and IPC → `storage`, `ipc`; Performance → `performance`; UI → `ui`; Recent Failures → `recentFailures`; Redaction → `redaction`. [Backend Diagnostics](<Exportaramus - Backend Specification.md#172-diagnostics>) defines generation and redaction; the [full diagnostic report schema](<Exportaramus - Backend Specification.md#2151-diagnostic-report>) defines every report field.

Flatten each property into display rows outside Draw. Visit object properties in ordinal key order; retain array order. Form labels with dotted property paths and zero-based bracketed indices, for example `sourceHealth[0].reasonCode`. Strings display their contents without JSON quotation marks; numbers and booleans use their scalar text, and null displays as empty text. Empty arrays and objects produce no child rows. Table IDs are `DiagnosticSection` followed by the section heading. Leave expansion state with ImGui after setting each section's initial-open flag.

### 12.4 Report Worker Lifecycle

Opening Diagnostics requests Generate Report. Keep at most one report worker. Starting it clears the error, sets Running, and records the current generation number. The worker receives immutable metadata and a cancellation token, prepares a validated report, and returns its result without touching either window.

On each update, inspect a completed worker and observe its result or exception. If its generation is still current, success replaces the report and sets its Ready or Partial status. Failure sets Failed and the error text in Section 12.1 while retaining the old report. Cancellation does not publish a result. Dispose the worker's cancellation resource after observing completion.

Closing Diagnostics increments the generation and cancels the worker. Immediately set Running false and restore the last report's status, or Ready if no report exists. Do not clear an existing error merely because the window closed. Observe the worker when it finishes, but reject its result because it belongs to an earlier generation. If the window reopens before that worker has been collected, another generate request is ignored; the player can request generation again after collection. Do not start two workers to bypass this rule.

Copy Report copies the prepared JSON immediately from the UI callback. Reset Measurements queues a measurement reset; it does not rewrite the displayed report, clear failure history, or trigger generation. A subsequent Generate Report captures the reset measurements. Backend report validation, size limits, and redaction remain mandatory even when the frontend only displays rows.

## 13. Commands and Plugin Listing

Register `/exportaramus` to open the main window and recognize `diagnostics` as its Diagnostics subcommand. Dalamud's Open and Settings entry points also open the main window; there is no separate settings window.

The listing should read:

```text
/exportaramus → Open Exportaramus.
/exportaramus diagnostics — Open diagnostics.
```

Dalamud adds the primary registered command before its help text. Supply `Open Exportaramus.` as the first help line and the full diagnostics command on the second line. Repeating `/exportaramus` in the first help line would display it twice.

The listing's collection total describes the backend's 25 supported types, not the main window's 23 collection rows. Command availability and the listing icon must not depend on whether the main window is open.

## 14. Rendering and State Ownership

### 14.1 Components and Ownership

| Component | Owns | Must Not Own |
| --- | --- | --- |
| Host integration | Registration, window system, fonts, update/draw callbacks, clipboard, orderly disposal. | Collection ownership decisions. |
| Backend adapter | Immutable frame and requests in Sections 3.5–3.6; conversion of backend metadata into that frame. | Drawing or hover/focus state. |
| Presenter | Complete display records, formatted values, health rules, row order, and action availability. | Native game calls, file writes, or asynchronous capture. |
| Action controller | Bounded input queues, routing and revalidation, configuration changes. | Advancing game dialogue by guessing from UI text. |
| Main renderer | One display record per draw; measured positions, controls, tooltips, and local focus bookkeeping. | Scanning item arrays, serializing JSON, or computing collection ownership. |
| Diagnostics controller | One report worker, its generation/cancellation, prepared report state. | Holding a renderer or querying mutable game objects on its worker. |

Keep Header, Guided Capture, Storage & History, Snapshot Health, and Snapshot Summary as separate drawing functions. Shared helpers implement text measurement, wrapping, panels, icons, and buttons. The names and file layout are implementation choices; the responsibilities are required.

### 14.2 Startup and First Display

1. Restore configuration. Only configuration version 1 preserves its stored Reduce Motion value; missing or unsupported configuration starts with false.
2. Construct the backend adapter, action queues, diagnostics controller, and font handles.
3. Prepare the first main display before constructing its window. It has no accepted publication, zero collection rows, and the appended Retainers row. History is zero, both relative timestamps are Never, and the health state reflects whether a character is available.
4. Construct both windows, add them to the window system, and register commands and callbacks. Neither opens automatically. Diagnostics has Ready status, no error, and no prepared report.
5. If registration fails partway through, undo completed registrations and release owned resources. Do not leave a half-registered command or a draw callback accessing disposed fonts.

### 14.3 Accept and Route Actions

Use two FIFO queues: 16 pending main actions and 8 diagnostic actions. Reject a new queued action when its queue is full; preserve the existing order. Process at most one action from each queue per framework update. Do not drain a burst of clicks into simultaneous native operations.

Copy JSON and Copy Report are exceptions: copy their already prepared strings immediately through the host clipboard on the UI thread. They do not enter a queue. Do not copy row labels, pretty-print the JSON again, or start a capture as part of copying.

| Main Action | Processing Rule |
| --- | --- |
| Close | Set the main window closed. Leave capture and backend scheduling running. |
| Toggle Reduce Motion | Toggle the current boolean and request persistence of that newest value. |
| Sync Now | Request a full refresh unless Retainers is active. |
| Retainers | Cancel if active; otherwise start only if Dresser & Armoire does not own the native sequence. |
| Dresser & Armoire | Cancel if its workflow is active; otherwise request Start. Ignore it while Retainers is active. |
| Continue / Save Partial / Cancel Capture | Forward the matching command to the active Dresser & Armoire controller. The controller validates whether it can act. |

There are no user actions for state disclosure or View History. Do not add controls because an old enum or mockup contains those names.

### 14.4 Framework Update

Use the following order. `Pump` means accept completed asynchronous work without waiting for a pending task. It does not mean blocking until the operation finishes.

```text
if disposed: return
read UTC time and monotonic seconds
sample the game theme if its one-second interval has elapsed
advance the backend's character/capture state
pump inventory work and advance retainer navigation
take at most one main action
route it using the current workflow state
advance Dresser & Armoire if active or given a command
accept backend readiness/refresh hints
take at most one diagnostic action
pump the configuration writer and completed diagnostic worker
copy the current Reduce Motion value into diagnostics state
if presentation needs refresh:
    read one immutable backend frame
    prepare and replace the entire main display
    refresh the immutable metadata available to diagnostic generation
```

Refresh presentation when the whole monotonic second changes, the accepted publication revision changes, a main action was processed, or the Dresser & Armoire state changes. Other changes, including retainer messages, become visible at the next scheduled refresh if none of those triggers fires. This is a once-per-second fallback, not a promise that every backend mutation appears in the same draw frame. An action takes effect on an update, not during the draw that enqueues it.

On an update exception, keep the last complete display and retry through later updates. Do not publish a partly prepared record or replace known counts with zero. The backend owns its error state; a caught host-update exception does not synthesize a successful capture or a new health result.

### 14.5 Draw One Frame

1. Apply this window's style and background alpha. Record all pushed colors, variables, and fonts so they can be restored.
2. Read the display once. Measure the body font to calculate `u`, then measure the content width and select the layout.
3. Draw the frame and header. Advance the vertical cursor by the measured wrapped subtitle and its gap.
4. Draw the upper panels. In wide layout use the larger of Guided Capture height and combined right-panel heights; in stacked layout measure and advance each panel in order.
5. Draw the nonempty general message. Then draw the active unpaused guided prompt, or the paused guided panel. Then draw a nonempty retainer message. Do not substitute the hidden workflow step label for the supplied prompt.
6. Draw the summary using its measured column count. Finish with a layout item covering the rendered extent so ImGui knows the scrollable content height; draw-list rectangles alone do not reserve layout space.
7. Record bounded UI measurements for Diagnostics, then restore the pushed style and fonts, including on early returns.

Track whether the paused panel was visible in the previous draw. On its first visible draw, focus Continue. On its first absent draw after a pause, focus Dresser & Armoire. Retain the normal host focus behavior at other times. Closing the main window clears its diagnostic geometry observation; it does not reset capture or manufacture new UI measurements while hidden.

No game reads, disk operations, serialization, consumer IPC calls, item-array scans, or new workers belong in Draw. Positioning, text measurement, drawing, local focus state, and enqueueing a bounded intent do. Clipboard writes of prepared strings are the documented exception.

### 14.6 Configuration and Disposal

Persist `{ "Version": 1, "ReduceMotion": <boolean> }` in plugin configuration. Change the in-memory setting when its queued action runs. Serialize writes: if one is pending, keep only the newest requested boolean for the next write. A save failure must not revert the visible checkbox to its old value. During orderly shutdown, observe the active writer and attempt to flush its newest pending value.

On plugin disposal, stop accepting actions; remove framework, draw, Open, Settings, and command registrations; remove windows; cancel and observe owned report work; finish configuration cleanup; dispose font handles; and clear pending action queues. Backend capture and IPC disposal follow [Backend Lifetime](<Exportaramus - Backend Specification.md#33-lifetime>). Unregistration and cleanup must tolerate partial startup and repeated disposal. Main-window closure and plugin disposal are different operations.

## 15. Verification and Implementation Evidence

### 15.1 Review Requirements

Verify the main window with a character absent, no accepted collection snapshot, complete and partial results, synchronization active, a retained snapshot after error, unavailable export, active guided capture, a partial-completion pause, and retainer cleanup failure. Include unknown, empty, and fully observed retainer rosters.

Check one-, two-, and three-column effective widths; long labels and large counts; all eight themes; normal and increased host font sizes; keyboard focus; and Reduce Motion. Verify that closing the main window does not cancel work and that closing Diagnostics cancels only its report worker.

For screenshot comparisons, record the host build, DPI, UI scale, body font, window size, selected theme, and background brightness. Compare against the current reference for structure and spacing, allowing recorded counts and times to differ. Preserve plugin borders, text, and controls in the comparison; mask only the game scene outside the window.

Use focused tests for presenter states, row meaning, layout decisions, theme transforms, intent handling, and report lifecycle when those behaviors change. Test game-window navigation and cleanup in game; presenter tests cannot establish whether native dialogue and inventory interactions succeed.

### 15.2 Acceptance Scenarios

Use a fake adapter to supply each input and record emitted operations. Advance UTC and monotonic time explicitly. These cases are requirements for the new implementation, not claims that a new runtime test was performed while writing this document.

| Case | Input or Action | Required Result |
| --- | --- | --- |
| No character | No character, no publication or inventory, both workflows idle. | Waiting for Character; Never refresh/history times; zero retained snapshots; `0 Collections` with only Retainers `Unknown`; capture, Sync, and Copy disabled. Close and Reduce Motion work. |
| Awaiting first snapshot | Character becomes available; publication still null. | No Snapshot Available; refresh and guided starts enabled; Copy disabled. No invented collection counts. |
| Complete observation | Non-restored publication with usable complete sources, known counts, and a prepared export. | Capture Complete; 23 collection rows plus Retainers; supplied counts/deltas; Copy enabled. |
| Restored observation | Same data restored from disk. | Some Data Unavailable; tooltip starts Restored snapshot. Keep the recorded values. |
| Error precedence | Character present, refresh error non-null, busy true, prior publication available. | Refresh Failed takes precedence over Synchronizing; preserve prior rows. Display the error; availability follows Section 9.1. |
| Refresh in progress | Busy true, no refresh error, accepted export already exists. | Synchronizing; Sync and new guided starts disabled; Copy remains enabled. |
| Missing source | Missing collection with NotLoadedThisSession, then a successful complete empty read. | Waiting first, then numeric `0`. The first state is not a zero observation. |
| Reader disabled | Field Records unavailable pending validation, no hard failure, remaining available data complete. | Field Records Unavailable; full health tooltip names it. This alone need not change Capture Complete to partial. |
| Closed dresser | Current-presence observation becomes Stale/Observed/WindowNotReady. | Keep its count without `!`; tooltip explains the closed window. |
| Failed retained read | Prior count exists, stale with CaptureFailed. | Keep count with `!`; partial headline and collector-error detail. |
| Deltas | Current presence falls by 3; permanent unlock count falls by 1. | `-3` warning and `-1` error respectively. Zero delta is `+0` in success color. |
| Outfits | Adapter supplies confirmed lower bound 327. | `327+`, no delta, last-observed explanation; retain the row's existing marker/tone. |
| Retainer roster | Unknown, then known empty, then 4 employed with 3 observations. | Unknown, `0/0`, then `3/4`; no extra storage disclosure. Retained observations count toward 3. |
| Refresh versus history time | New capture timestamp but unchanged latest retained history entry. | Last Refresh advances; Latest retains its older age. |
| Clock boundaries | Absent, 59s, 60s, 3,600s, 86,400s, future timestamp. | Never, Just now, `1 minutes ago`, `1 hours ago`, `1 days ago`, Just now. |
| Copy | Click Copy with accepted JSON available. | Clipboard gets the exact prepared string; no refresh, navigation, or serialization request. |
| Main close | Start capture, close main window, allow updates, reopen. | Capture continues; reopened window shows current prepared state and retained host geometry. |
| Dresser pause | Active workflow changes to PausedWithPartial with Dresser complete. | Inline panel appears; Continue gains focus; Save Partial is present; initiating button remains Cancel Capture. |
| Continue | Click Continue; backend subsequently resumes. | One Continue request; panel remains until backend reports unpaused; then focus returns to Dresser & Armoire. |
| Save Partial | Select Save Partial in the preceding pause. | One request; retain completed observations; do not claim Armoire completion. Display backend progress until it finishes. |
| Retainer cleanup | All retainer inventories observed but workflow still active during teardown. | Ratio can be `4/4` while Cancel Retainers and progress remain. Only backend completion ends the active state. |
| Competing action | Retainers becomes active after Sync was drawn but before its queued action is processed. | Ignore Sync; do not execute it later. |
| Queue limits | Enqueue 17 main actions or 9 queued diagnostic actions before updating. | Keep the first 16 or 8, reject the last, and consume at most one of each per update. |
| Presentation refresh | No action, revision, guided-state, or whole-second change. | Reuse the existing display. A changed revision or accepted main intent refreshes on the next update. |
| Motion setting | Toggle Reduce Motion while an action is held. | Checkbox changes after action processing; held-label offset becomes zero; capture remains unaffected. Latest setting persists across reload. |
| Diagnostic replacement | Prepared report exists; start replacement generation. | Running status, old report still visible/copyable, Generate disabled. Success atomically replaces it. |
| Diagnostic failure | Replacement generation fails. | Failed and the specified error; old report remains copyable. |
| Diagnostic close race | Close before worker completes; later return success from that worker. | Worker is cancelled/observed; its old-generation result cannot replace the display. |
| Measurement reset | Reset after a report is displayed. | Samples/counters reset, failures and displayed report remain; next generation reflects reset. |
| Layout and theme | Use all eight themes, 1/2/3-column effective widths, increased body fonts, long text, hover/focus/disabled states. | Apply the exact measurements and wrapping rules; preserve readable labels and usable actions. Record actual contrast and visual results. |
| Disposal | Dispose after partial registration or during report work; invoke disposal again. | No callbacks use released state; owned work is observed; no duplicate registration or uncaught second-disposal failure. |

Begin with the first three cases to establish a working display. Add failure, workflow, and lifecycle cases before connecting real capture. Use the screenshot for measured visual comparison, not as evidence that every case passes. For in-game acceptance, identify the loaded build and record native capture/cleanup outcomes separately from frontend unit results.

### 15.3 Current Implementation Map

| Requirement | Implementation Evidence |
| --- | --- |
| Window composition and inline prompts | [MainWindow](../src/Features/Main/MainWindow.Dalamud.cs) |
| Header and close target | [MainHeaderRenderer](../src/Features/Main/MainHeaderRenderer.Dalamud.cs) |
| Upper panels | [OperationsRenderer](../src/Features/Main/OperationsRenderer.Dalamud.cs), [StorageHistoryRenderer](../src/Features/Main/StorageHistoryRenderer.Dalamud.cs), [SnapshotHealthRenderer](../src/Features/Main/SnapshotHealthRenderer.Dalamud.cs) |
| Collection grid | [SnapshotSummaryRenderer](../src/Features/Main/SnapshotSummaryRenderer.Dalamud.cs) |
| State, counts, health, and relative time | [MainPresenter](../src/Features/Main/MainPresenter.cs), [MainSnapshotPresenter](../src/Features/Main/MainSnapshotPresenter.cs) |
| Retainer coverage and outfit lower bounds | [InventoryPresentation](../src/Features/Inventory/InventoryPresentation.cs), [Plugin integration](../src/Bootstrap/Plugin.Dalamud.cs) |
| Layout and refresh decisions | [LayoutPolicy](../src/Shared/Ui/LayoutPolicy.cs), [PresentationInvalidationPolicy](../src/Features/Main/PresentationInvalidationPolicy.cs) |
| Palette and host styles | [ThemePolicy](../src/Shared/Ui/ThemePolicy.cs), [ThemePalette](../src/Shared/Ui/ThemePalette.cs), [WindowStyle](../src/Shared/Ui/WindowStyle.Dalamud.cs) |
| Shared controls and icons | [Drawing](../src/Shared/Ui/Drawing.Dalamud.cs), [UiIcons](../src/Shared/Ui/UiIcons.Dalamud.cs), [Tooltips](../src/Shared/Ui/Tooltips.cs) |
| Diagnostics display and lifetime | [DiagnosticsWindow](../src/Features/Diagnostics/DiagnosticsWindow.Dalamud.cs), [DiagnosticsController](../src/Features/Diagnostics/DiagnosticsController.cs), [DiagnosticReportSerializer](../src/Features/Diagnostics/DiagnosticReportSerializer.cs) |

These links identify the current implementation of the requirements above.

## 16. Vector Drawing Reference

### 16.1 Icon Coordinates

Each icon uses a square local coordinate system from 0 to 1. Convert a point `(x, y)` to `origin + iconSize × (x, y)`. Use the Icon color, including for disabled-button icons. Size is normally `18 × u`; Section 7 specifies larger panel icons. Coordinates describe product-owned shapes, so no separate icon library is required.

- `line(x1, y1, x2, y2)`: one segment, 1.4-pixel stroke.
- `box(x, y, width, height)`: outline rectangle, 1-pixel stroke and 1-pixel corner rounding.
- `circle(x, y, radius)`: outline circle with 20 segments and a 1.4-pixel stroke.

The stroke widths and box rounding are raw drawing pixels; scale the coordinates and radius by iconSize, not the strokes. Execute the listed operations in order.

| Icon Key | Operations |
| --- | --- |
| Close | `line(0.2, 0.2, 0.8, 0.8)`; `line(0.8, 0.2, 0.2, 0.8)` |
| Clock | `circle(0.5, 0.5, 0.43)`; `line(0.5, 0.15, 0.5, 0.5)`; `line(0.5, 0.5, 0.7, 0.65)` |
| Capture | `box(0.1, 0.1, 0.8, 0.8)`; `circle(0.5, 0.5, 0.2)` |
| Copy | `box(0.1, 0.1, 0.6, 0.7)`; `box(0.3, 0.25, 0.6, 0.7)` |
| Scroll | `box(0.2, 0.12, 0.6, 0.76)`; `line(0.3, 0.32, 0.7, 0.32)`; `line(0.3, 0.48, 0.7, 0.48)`; `circle(0.2, 0.13, 0.12)`; `circle(0.8, 0.86, 0.12)` |
| Diamond | `line(0.5, 0.05, 0.8, 0.4)`; `line(0.8, 0.4, 0.5, 0.95)`; `line(0.5, 0.95, 0.2, 0.4)`; `line(0.2, 0.4, 0.5, 0.05)`; `line(0.2, 0.4, 0.8, 0.4)` |
| Storage | `box(0.1, 0.15, 0.8, 0.65)`; `circle(0.5, 0.48, 0.2)`; `line(0.15, 0.8, 0.15, 1)`; `line(0.85, 0.8, 0.85, 1)` |
| Shield | `line(0.1, 0.15, 0.9, 0.15)`; `line(0.1, 0.15, 0.2, 0.65)`; `line(0.2, 0.65, 0.5, 0.95)`; `line(0.5, 0.95, 0.8, 0.65)`; `line(0.8, 0.65, 0.9, 0.15)` |
| Cards | `box(0.12, 0.15, 0.55, 0.65)`; `box(0.35, 0.3, 0.55, 0.65)` |
| Smile | `circle(0.5, 0.5, 0.43)`; `circle(0.35, 0.35, 0.03)`; `circle(0.65, 0.35, 0.03)`; `line(0.3, 0.65, 0.5, 0.75)`; `line(0.5, 0.75, 0.7, 0.65)` |
| Glasses | `box(0.05, 0.35, 0.35, 0.3)`; `box(0.6, 0.35, 0.35, 0.3)`; `line(0.4, 0.45, 0.6, 0.45)` |
| Frame | `box(0.1, 0.1, 0.8, 0.8)`; `circle(0.1, 0.1, 0.05)`; `circle(0.9, 0.9, 0.05)` |
| Music | `circle(0.3, 0.8, 0.14)`; `line(0.44, 0.8, 0.44, 0.1)`; `line(0.44, 0.1, 0.9, 0.3)`; `line(0.44, 0.3, 0.9, 0.5)` |
| Quest | `circle(0.5, 0.5, 0.43)`; `line(0.5, 0.2, 0.5, 0.6)`; `circle(0.5, 0.77, 0.025)` |
| Relic | `circle(0.5, 0.5, 0.35)`; `circle(0.5, 0.5, 0.18)`; `line(0.5, 0, 0.5, 0.15)`; `line(0, 0.5, 0.15, 0.5)`; `line(0.85, 0.5, 1, 0.5)`; `line(0.5, 0.85, 0.5, 1)` |
| Shirt | `line(0.3, 0.1, 0.05, 0.3)`; `line(0.05, 0.3, 0.2, 0.5)`; `line(0.2, 0.5, 0.3, 0.4)`; `line(0.3, 0.4, 0.3, 0.9)`; `line(0.3, 0.9, 0.7, 0.9)`; `line(0.7, 0.9, 0.7, 0.4)`; `line(0.7, 0.4, 0.8, 0.5)`; `line(0.8, 0.5, 0.95, 0.3)`; `line(0.95, 0.3, 0.7, 0.1)`; `line(0.3, 0.1, 0.5, 0.25)`; `line(0.5, 0.25, 0.7, 0.1)` |
| Trophy | `box(0.3, 0.1, 0.4, 0.45)`; `line(0.5, 0.55, 0.5, 0.85)`; `line(0.2, 0.9, 0.8, 0.9)`; `box(0.05, 0.15, 0.25, 0.3)`; `box(0.7, 0.15, 0.25, 0.3)` |
| Dungeon | `box(0.1, 0.4, 0.8, 0.55)`; `circle(0.5, 0.4, 0.4)`; `line(0.35, 0.4, 0.35, 0.95)`; `line(0.65, 0.4, 0.65, 0.95)` |
| Head | `circle(0.5, 0.35, 0.3)`; `line(0.3, 0.5, 0.4, 0.9)`; `line(0.7, 0.5, 0.6, 0.9)` |
| Pet | `circle(0.5, 0.65, 0.3)`; `line(0.3, 0.4, 0.2, 0.05)`; `line(0.7, 0.4, 0.8, 0.05)`; `circle(0.4, 0.65, 0.025)`; `circle(0.6, 0.65, 0.025)` |
| Mount / Route | `line(0.2, 0.9, 0.3, 0.45)`; `line(0.3, 0.45, 0.2, 0.2)`; `line(0.2, 0.2, 0.45, 0.05)`; `line(0.45, 0.05, 0.8, 0.4)`; `line(0.8, 0.4, 0.55, 0.6)`; `line(0.55, 0.6, 0.8, 0.9)`; `line(0.2, 0.9, 0.8, 0.9)` |
| Book | `box(0.05, 0.15, 0.45, 0.7)`; `box(0.5, 0.15, 0.45, 0.7)` |
| List / Fallback | `box(0.1, 0.1, 0.8, 0.8)`; `line(0.3, 0.3, 0.75, 0.3)`; `line(0.3, 0.5, 0.75, 0.5)`; `line(0.3, 0.7, 0.75, 0.7)` |

### 16.2 Health Glyph and Button Details

Health uses its tone color rather than the normal icon color. Draw its circle with 24 segments, radius `17 × u`, and stroke `2 × u`. For success, draw two segments relative to the circle center: `(-8, 0) → (-2, 7) → (10, -9)`, all multiplied by `u`, with stroke `2 × u`. Otherwise draw `(0, -9) → (0, 3)` and a filled dot at `(0, 9)` with radius `1.5 × u`. The glyph tooltip hit box extends `19 × u` from its center in each direction and requires the window to be hovered.

Ordinary buttons use a real ImGui button for hit testing, held/hover/focus state, disabled input, and themed fill. Give that button only its hidden ID; draw the visible label yourself. Add a border with 3-pixel rounding, 2-pixel stroke for keyboard focus and 1 otherwise. Use Gold for hovered or focused borders and Border otherwise. Draw the top highlight from `(2, 1)` to `(width - 2, 1)` in raw pixels.

Measure the label, add `25 × u` if an icon is present, and center that combined width. Vertically center using label height. Add the 1.5-pixel held displacement only when Reduce Motion is false. Draw an icon at the resulting origin, then advance by `25 × u` for the label. Disabled labels use Muted; enabled cancel labels use Warning; other labels use Primary. The close button instead follows Section 5.1 and has no added border.

Panels have a top highlight from `(3, 1)` to `(width - 3, 1)` in raw pixels. The main frame has both the displaced outer shadow described in Section 4.2 and a 3-pixel shadow outline inset one pixel. Restore host drawing/style state after either window; these helpers must not alter another plugin's appearance.
