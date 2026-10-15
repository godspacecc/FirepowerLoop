---
name: unity-ui
description: Use when creating, modifying, reviewing, or diagnosing AnotherDay Unity UI Prefabs, BaseUI or BaseBoxUI views, BaseUICtrl or BasePageCtrl controllers, UIRoot layers, lists, localization, input blocking, screen HUD, world-space HUD, serialized UI references, or UI lifecycle behavior.
---

# Unity UI

## Core Rule

Treat Prefab structure, View references, Controller lifecycle, and domain behavior as separate responsibilities. Keep stable layout and serialized references in Prefabs; let Controllers render authoritative snapshots and submit user intent; let domain owners validate and mutate gameplay state.

## Context Gate

1. Read `AGENTS.md`, the authorized requirement or confirmed design/formal plan, and the actual nearby UI implementation. Read `Docs/UI框架/README.md` when it is present; if it is not synchronized, use this Skill plus the actual base types and nearest mature Prefabs/Controllers, and mark any unresolved project-history or UI-policy claim `待确认`.
2. Confirm the current device may edit every affected `Client/` path. If not, stop at design, plan, or review.
3. Inspect the real Prefab or scene YAML, its `.meta`, View type, Controller, base lifecycle, resource path, show/hide callers, domain data source, events, localization keys, and nearby tests before proposing changes.
4. Treat fields, layers, paths, components, GUIDs, anchors, sizes, and hierarchy as facts only when present in synchronized files. Never reconstruct a missing Prefab contract from a plan or screenshot alone.
5. For handwritten runtime C#, **REQUIRED SUB-SKILL:** Use `code-role`. For Luban-backed UI data, **REQUIRED SUB-SKILL:** Use `luban-config`.

## Choose the UI Owner

| Responsibility | Current project owner |
| --- | --- |
| Independent loadable screen, dialog, tips, or screen HUD | `BaseUICtrl` subclass under `Controller/UICtrl/` |
| View fields serialized by a loadable Prefab | `BaseUI` subclass under `Controller/UI/` |
| Page embedded inside an existing host Prefab | `BasePageCtrl`; host Controller owns binding and selection |
| Reused row, slot, or small element View | `BaseBoxUI`; owning Controller supplies data and interaction |
| Stable hierarchy, anchors, layout groups, masks, fonts, default visibility, and references | UI Prefab or scene |
| Dynamic display state and user intent | UI Controller |
| Authoritative rules and mutation | Model, System, or Command outside the UI |
| Screen UI roots and order | Existing `UIRoot` with `Dialog`, `Tips`, and `Top` layers |
| Unit-following world HUD | Owning unit View and its World Space Canvas, not `UIRoot` |

Do not create another UI framework, layer, root, or generic base class for a single screen. Reuse the closest working screen with the same lifecycle and interaction shape.

## Lifecycle Contract

### `BaseUICtrl`

- `Path()` returns the actual resource path without inventing an extension or alternate loader.
- `Layer()` selects one existing `UILayer`; do not infer that every HUD belongs in `Top`.
- `BlocksGame` defaults to `true`. Override it to `false` only for displays that must not block gameplay input.
- `OnInit()` runs once for each loaded `BaseUI` instance, not once forever for the Controller. Bind View-owned buttons and list callbacks here. For architecture events or other subscriptions that can outlive the View, establish an explicit unregister owner before relying on reload; do not accumulate a second subscription when a destroyed Prefab is loaded again.
- `OnShow()` runs only on a hidden-to-visible transition. Refresh all current presentation state here.
- Repeated `Show()` while visible only moves the instance to the top; it does not rerun `OnShow()`.
- `OnHide()` runs only for an active instance. Clear focus, drag state, timers, pending short-lived work, and other visibility-owned state here.
- Destruction resets the loaded instance and lifecycle state. Do not retain destroyed Unity objects in callbacks or caches, and do not assume resetting `_initialized` unregisters external events.

### `BasePageCtrl`

- Bind an existing host-owned `BaseUI` through `SetUI()`; never load a second Prefab or register the Page in `UIHelper`.
- Treat repeated show/hide as idempotent. Rebinding or host destruction must not let the old View affect the new binding.
- Keep Page selection, actual child binding, and ownership in the host Controller.

### Views and boxes

- A `BaseUI` subclass exposes serialized presentation references and Unity-specific callbacks; it does not own domain state.
- A `BaseBoxUI` subclass is a reusable View marker, not a generic business-data cache.
- Validate required components and references before use. Do not hide a broken Prefab contract behind repeated null checks, runtime-created replacement controls, or guessed hierarchy searches.

## Prefab, Layout, and Layer Rules

1. Keep stable hierarchy, anchors, pivots, size, spacing, layout groups, masks, raycast settings, fonts, materials, default active state, and View references serialized in the Prefab.
2. Use runtime layout only when content is genuinely data-dependent. Do not hardcode a second copy of a static Prefab layout in C#.
3. A loadable UI Prefab root must provide the expected `BaseUI` subtype and match the Controller `Path()`.
4. Preserve the existing `UIRoot`, `Canvas`, `CanvasScaler`, `GraphicRaycaster`, `EventSystem`, and `InputSystemUIInputModule` contract. Adding a layer or alternate root requires confirmed design.
5. Use sibling order only within the selected layer. Do not solve a wrong-layer problem by continuously forcing transforms to the end.
6. Modify Unity YAML only with verified file IDs, GUIDs, component types, and neighboring structure. New Unity files and referenced assets require matching `.meta` files.
7. Tests may read formal Prefabs and scenes but must not create, overwrite, or repair product assets as a side effect.

## Required UI Generation and Wiring

- Every UI Prefab under `Assets/Game/Prefabs/UI/` must have a root `ReferenceCollector` and use the matching project generator. Independent UI uses `Assets/智能更新UI`, Box UI uses `Assets/智能更新BoxUI`, and Page UI uses `Assets/智能更新Page`. There is no handwritten View or manual serialized-field wiring exception.
- Treat the generated View under `Assets/Game/Scripts/Controller/UI/` as generator-owned. Change its fields through the Prefab `ReferenceCollector` and rerun the matching generator; do not manually add, remove, rename, or bind generated View fields. Controller behavior may remain handwritten in the existing Controller file.
- After changing a UI hierarchy, component, reference key, or referenced object, rerun the matching generator and inspect the resulting Prefab, generated View, attached View component, and serialized field values as one change.
- A nonzero YAML `fileID` is not proof of a valid reference. Every required serialized reference must resolve to exactly one existing YAML document in the same Prefab, and that document must provide the field's required object or component type. A missing document header, duplicate ID, wrong component type, or unresolved target is broken wiring and behaves as a null reference.
- When editing Prefab YAML, preserve complete Unity document boundaries. Deleting or moving an object or component must not remove a neighboring `--- !u!... &<fileID>` header or leave references to a removed document.
- Review and focused Prefab contract tests must inspect the Prefab after Unity deserialization, including the root `ReferenceCollector`, generated View component, and every required generated field. Text search, a nonzero serialized ID, or runtime null protection alone cannot prove correct wiring.
- Required generated fields are used directly after the Prefab contract is established. Do not add null guards to make missing required wiring appear optional; only fields explicitly defined by the UI behavior as optional may use optional handling.

## State, Input, and Interaction

- Render Models, Systems, configuration, or immutable snapshots; do not store a second authoritative inventory, setting, health, selection result, or transaction state in the UI.
- Submit intent through the established Command/System/Controller entry and refresh from authoritative state after success or failure.
- `UIHelper.HasUIOpen` blocks gameplay input when any visible Controller has `BlocksGame == true`; opening a UI does not by itself change `AppState` or `timeScale`.
- Clear `EventSystem` selection and input-focus state when a blocking input UI hides. Prevent submit and click paths from executing the same action twice.
- Keep button display text separate from roles, IDs, and behavior. Never branch business logic on localized display strings.
- For async UI work, capture the owning instance/request and revalidate visibility, instance identity, and current state after awaiting. Late completion must not mutate a replaced or hidden screen unless the confirmed behavior requires it.

## Lists and Reused Elements

- Use `$ui-prefab-authoring` whenever a list, code-managed repeated element, Box Prefab, or custom collection component is created, changed, reviewed, or diagnosed.
- Use `CustomListView` for ordinary small bounded collections that refresh as a whole.
- Use the existing LoopScrollRect family for infinite or virtualized collections that need pooling and visible-area optimization. Long or continually growing data is a common reason to need this behavior.
- Update the backing snapshot before setting the list count.
- Every row refresh must overwrite all text, images, visibility, interactable/selected state, IDs, and callbacks for every branch.
- Remove or replace old button listeners before binding the current row. Do not let a `BaseBoxUI` retain stale business objects across reuse.
- Keep viewport, mask, content layout, cell size, and default item source in the Prefab; the Controller supplies count, data, and actions.

## Localization and HUD Boundaries

- Fixed Prefab labels use verified `LocalizedText` keys when the project pattern applies.
- A player-facing TMP serialized in a Prefab must remain readable to designers. For fixed localized text, store the verified default-language text in the TMP preview and store the internal `ui.*` identifier only in the same-object `LocalizedText.Key`; key mode keeps `Texts` empty. Do not use a localization key as the TMP preview value.
- Classify every TMP as exactly one of: fixed player text, dynamic player text, or an explicitly approved developer-facing literal. A player-facing TMP serialized as `ui.*` is internal-key leakage whether or not a same-object `LocalizedText` uses that key; a missing or mismatched component is an additional wiring error, not a preview convention.
- Fixed player text does not belong in `ReferenceCollector` and must not be exposed as a generated View TMP field. If a Controller only needs visibility, collect the owning `GameObject` or stable container instead.
- Dynamic text uses the current localization entry points such as `Texts` or `Loc.T` and refreshes when language changes.
- Dynamic text must not carry `LocalizedText`; the Controller owns every refresh branch, including the current-state redraw after a language change.
- Developer-facing literal text is allowed only for interfaces explicitly excluded from player localization. Store readable text, not `ui.*` keys, and do not attach `LocalizedText`.
- Missing keys, fonts, glyphs, or generated accessors are real contract gaps; do not replace them with unrelated hardcoded strings.
- Prefab localization checks must validate both ownership and authoring readability: the Key exists in every formal language table, the serialized TMP preview equals the default-language table value for fixed text, and no player-facing TMP serializes an internal `ui.*` value. A finite legacy exception must identify exact Prefab object paths and must be removed by its owning migration; never turn it into a permanent whitelist.
- Screen HUD follows `BaseUICtrl` and `UIRoot` rules. Non-blocking HUD explicitly returns `BlocksGame => false`.
- World-space unit HUD remains part of its owning View, uses verified Canvas/material/depth behavior, and consumes read-only presentation state. Do not move it into screen UI or let it mutate the unit model.

## Change Workflow

1. State the visible behavior, current hierarchy, lifecycle, data authority, input behavior, and evidence; mark unsupported causes `待确认`.
2. Classify each change as Prefab structure, serialized View contract, Controller behavior, domain behavior, localization, resource, or test contract.
3. Define ownership, layer, blocking behavior, show/hide semantics, failure behavior, and exclusions before editing.
4. Modify the smallest coherent Prefab/`ReferenceCollector`/generated View/Controller slice and every directly affected caller or test. Rerun the matching UI generator after Prefab reference changes; do not expand nearby business systems for layout convenience.
5. Check show, repeated show, hide, repeated hide, destroyed/reloaded View, language change, empty data, reused rows, failed command, and input-focus paths where applicable.
6. Use focused EditMode contract tests or other verification explicitly authorized by the task. Do not introduce PlayMode automation unless the user explicitly changes the project constraint.
7. Report Prefab/scene changes, `.meta` files, View/Controller changes, domain changes, executed verification, skipped verification, and unresolved visual risks separately.

## Example

When hidden start-screen buttons must stop leaving gaps, configure one stable vertical layout in the Prefab and let the Controller only set each button's active state from the real save/update snapshot before rebuilding the affected layout if necessary. Do not maintain separate hardcoded Y positions for every visibility combination.

## Completion Check

- The actual Prefab/scene, `.meta`, View, Controller, base lifecycle, callers, data owner, localization, and tests were inspected.
- Every project UI Prefab in scope has a root `ReferenceCollector`, was processed by the matching UI generator, and has the generated View component attached.
- Every required generated View reference resolves after Unity deserialization to exactly one object or component of the required type; no nonzero dangling `fileID` is accepted as valid wiring.
- Static layout and serialized references have one Prefab source; dynamic behavior has one Controller owner.
- UI displays authoritative state and submits intent without becoming a second domain writer.
- Layer, sibling order, raycasts, blocking, focus, show/hide, destruction, and async expiry are correct where applicable.
- Event and timer ownership remains correct after hide, destruction, and reload; no subscription is duplicated.
- Reused rows fully reset all presentation and callbacks.
- Screen HUD and world-space HUD boundaries remain distinct.
- Validation claims match actual evidence and `AGENTS.md` restrictions.

## Common Mistakes

- Handwriting a View or its serialized fields instead of updating `ReferenceCollector` and running the matching UI generator.
- Accepting a nonzero Prefab `fileID` without confirming that its YAML document and required component still exist.
- Adding runtime `SetParent`, `SetSize`, or position code to compensate for an incorrect stable Prefab layout.
- Binding stable listeners in `OnShow()` and duplicating them after every reopen.
- Discarding an external event's unregister handle in `OnInit()` when the Prefab can be destroyed and reloaded.
- Expecting repeated `Show()` to refresh because it moves the UI to the top.
- Loading a Page as an independent UI or registering it in `UIHelper`.
- Using `Top` or a world-HUD overlay material as a blanket fix for sorting problems.
- Leaving a blocking `TMP_InputField` selected after hiding its UI.
- Refreshing only one branch of a reused row and leaking old text, visibility, selection, or callbacks.
- Letting tests write product Prefabs or claiming visual correctness from code inspection alone.
