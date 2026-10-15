---
name: code-role
description: Use when modifying, adding, reviewing, or explaining AnotherDay Unity C# runtime code, QFramework Model/System/Controller/View responsibilities, lifecycle updates, events, map-owned objects, asynchronous resources, persistence, or cross-module behavior under Client/Assets/Game/Scripts.
---

# AnotherDay Code Role

## Core Rule

Determine the current owner and transaction boundary from synchronized implementation before naming new types or files. Roadmaps, plans, and knowledge describe intent; actual code is implementation evidence.

## Context Gate

1. Read `AGENTS.md` and the actual implementation in scope. When `AnotherDay后续自主开发路线图-0724-1838.md` or `Docs/README.md` is present on this device, read it for stage and knowledge-index context; its absence on an implementation device does not block a user-authorized task and does not permit inventing its contents.
2. Confirm whether this device may edit `Client/`. If not explicitly authorized, stop at design, plan, or review.
3. If the request adds or changes product behavior, satisfy the design-confirmation and implementation-authorization workflow defined by this device before implementation. This Skill does not provide product authorization or replace that workflow.
4. Read the owning `Docs/` topic when it is present, then the actual implementation, registration point, callers, consumers, and nearby tests. If that topic is not synchronized, use the authorized requirement or formal plan plus actual implementation evidence, and mark any unresolved project-history claim `待确认`.
5. Read [references/responsibility-map.md](references/responsibility-map.md) when ownership is unclear, a new type is proposed, or the task crosses modules.
6. Treat missing files and counts as facts only for content synchronized on the current device.

## Choose the Owner

| Responsibility | Default owner |
| --- | --- |
| Persistent or domain state | `AbstractModel` / save section |
| Rules, state transitions, continuous updates | `AbstractSystem` through the shared lifecycle |
| Creation, assembly, resource preparation, runtime ports | `IController` / ViewCtrl |
| Serialized Unity references, Collider/Animator callbacks, presentation | View `MonoBehaviour` |
| Infrastructure adapter without product state | narrow Utility interface |
| User intent | input/UI emits a command or event; domain owner decides |

- Do not add a product `MonoBehaviour.Update()` merely to obtain per-frame execution.
- Keep View and UI from becoming authoritative business state.
- Publish events after authoritative state changes. Prefer stable IDs and snapshots; do not carry `GameObject`, `Transform`, resource handles, or mutable Views as domain facts.
- Register new Models, Systems, Controllers, and Utilities through the existing architecture composition in `Client/Assets/Game/Scripts/Game.cs`.

## Map-Owned and Async Work

For a map-owned object, establish `UnitId`, `MapInstanceId`, owner root, authority, active state, and release path before implementation.

1. Validate configuration and dependencies before visible mutation.
2. Capture cancellation/generation and expected `MapInstanceId` before awaiting.
3. After completion, revalidate cancellation, current map identity, owner root, and controller release state.
4. Create candidates hidden; validate Prefab/component/NavMesh contracts before commit.
5. Commit domains in the mature local order. On failure, undo only completed steps in reverse order.
6. Give every resource Handle exactly one owner and one release path, including cancellation before callback, late callback, rollback, replacement, and map unload.
7. Make repeated requests, events, settlement, and release idempotent or explicitly rejected.

Use `MapSceneSystem` plus the nearest `PlayerCtrl`, `EnemyCtrl`, `LootCtrl`, `ProjectileCtrl`, or `ExtractionCtrl` transaction as evidence. Do not reconstruct the pattern from memory.

## Data, Config, UI, and Save Boundaries

- For Luban schemas, CSV data, generation, generated outputs, runtime config definitions, or providers, **REQUIRED SUB-SKILL:** Use `luban-config`. Never edit generated C# or bytes as source, and never invent an absent source or generation path.
- Do not copy configuration defaults into Systems, Views, or tests to bypass `IRuntimeConfigProvider`.
- Save only stable domain values and IDs. Never persist scene objects, runtime Units, Transforms, contexts, or Handles.
- For Unity UI Prefabs, Views, Controllers, layers, lists, localization, input blocking, or HUD work, **REQUIRED SUB-SKILL:** Use `unity-ui`. UI renders snapshots and submits intent; domain owners perform validation and mutation.
- For a bug or unexpected behavior, follow the systematic-debugging workflow available on this device before proposing a fix. This Skill does not replace that workflow.

## Implementation Flow

1. State current behavior and evidence; mark unsupported causes `待确认`.
2. Select the owning stage, domain owner, lifecycle, data authority, failure semantics, and exclusions.
3. Reuse the closest mature implementation without copying unrelated complexity.
4. Change the smallest coherent vertical slice; do not broaden public interfaces or create future abstractions.
5. Preserve existing user changes and generated-file boundaries.
6. Run only verification explicitly authorized by the current task and device rules. Report executed and unexecuted checks separately.

## Example

For continuous environmental damage: keep elapsed/rule state in a Model or System, advance it through the shared lifecycle only while the authoritative app/expedition state allows, call the existing combat damage entry, and let a View render radius or effects. Do not search the player every frame or write HP from a zone View.

## Completion Check

- Actual owner and neighboring precedent were read before proposing types.
- Model/System/Controller/View/Event responsibilities remain one-way and explicit.
- Map identity, async expiry, candidate commit, reverse rollback, and Handle release are covered where applicable.
- Save/config/generated/UI boundaries are preserved.
- Every named source/config path was verified in synchronized content or explicitly marked `待确认`.
- New registrations and all unregister/dispose paths are paired.
- Validation claims match actual evidence and `AGENTS.md` restrictions.

## Common Mistakes

- Reusing a class name from an old roadmap instead of checking current code.
- Treating a source path mentioned by a plan as though it was synchronized implementation evidence.
- Using this code-role Skill to skip new-feature design confirmation.
- Adding a second authoritative write path for convenience.
- Treating map unload as cleanup for a callback that may arrive after unload.
- Exposing mutable implementation objects through events or public getters.
- Hand-editing generated outputs or hiding configuration defaults in tests.
- Claiming completion from a plan, compilation assumption, or missing manual record.
