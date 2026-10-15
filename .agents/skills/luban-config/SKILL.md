---
name: luban-config
description: Use when modifying, generating, reviewing, or diagnosing AnotherDay Luban schemas, CSV configuration, luban.conf, the generation entry, generated C# or bytes, runtime configuration definitions, IRuntimeConfigProvider, or LubanRuntimeConfigProvider.
---

# Luban Config

## Core Rule

Treat configuration sources, generated outputs, and handwritten runtime boundaries as three different layers. Change the source of truth, regenerate outputs through the unique project entry, and update runtime adapters only when the runtime contract changes. Never hand-edit generated output to bypass a source or generation problem.

## Context Gate

1. Read `AGENTS.md` and the relevant authorized requirement, formal plan, or confirmed design. Read `Config/README.md` when it is present; if it is not synchronized, use the actual configuration sources, generator wiring, outputs, and this Skill, and mark facts that cannot be established from them `待确认`.
2. Confirm the current device may edit every affected path. If it may not edit `Client/`, stop at design, plan, or review.
3. Inspect the actual `Config/luban.conf`, relevant files under `Config/Defines/` and `Config/DataCsv/`, the generation entry, generated outputs, runtime provider, consumers, and nearby tests before naming files or fields.
4. Treat paths, tables, fields, output names, and counts as facts only when present in synchronized content.
5. When handwritten runtime C# changes, **REQUIRED SUB-SKILL:** Use `code-role` for framework and responsibility boundaries.

## Layer Boundaries

| Layer | Current project location | Rule |
| --- | --- | --- |
| Schema and enums | `Config/Defines/*.xml` | Source of table, bean, enum, field, type, group, and key contracts |
| Row data | `Config/DataCsv/*.csv` | Source of configuration values and cross-table IDs |
| Luban wiring | `Config/luban.conf` | Source of schema list, data root, targets, and groups |
| Generation entry | `Config/gen.bat` | Unique formal entry after project naming migration |
| Generated C# | `Client/Assets/Game/Scripts/Model/Config/` | Generated output; never edit as source |
| Generated binary data | `Client/Assets/Game/Config/*.bytes` | Generated output; do not treat unrelated assets in the same directory as generated |
| Server generated C# | `Server/GameServer/Unity/Assets/Scripts/Model/Generate/Server/Config/` | Generated output for the server; never edit as source |
| Server generated binary data | `Server/GameServer/Config/Luban/` | Generated output loaded by the server |
| Temporary output | `Client/Library/LubanBinData/` | Disposable generation intermediate, never a source or delivery target |
| Runtime contract | `Client/Assets/Game/Scripts/Utility/Config/IRuntimeConfigProvider.cs` and `RuntimeConfigDefinitions.cs` | Handwritten stable interface exposed to gameplay code |
| Runtime adapter | `Client/Assets/Game/Scripts/Utility/Config/LubanRuntimeConfigProvider.cs` | Handwritten loading, conversion, reference validation, and error boundary |

During first adoption, if the synchronized project still has only `Config/gen_anotherday.bat`, rename it to `Config/gen.bat`, update still-active references, and keep no compatibility stub. Do not rewrite historical results merely to change an old path mention. If multiple possible generation entries exist, stop and establish one authoritative entry before generation.

The formal `Config/gen.bat` entry generates the configured client and server targets in one run. `Config/DataCsv/map_display.csv` is client-only display metadata; map images and their item icons are Unity Editor outputs from `Tools/地图/生成地图资料图片`, not Luban outputs. After server-side generated configuration changes, restart the server process before treating runtime verification as current.

## Classify the Change

| Change | Required source work | Possible handwritten work |
| --- | --- | --- |
| Existing row value | Relevant CSV only | None unless runtime validation intentionally changes |
| New row or reference | Relevant CSV files | Constants or consumers only when the confirmed runtime contract requires them |
| Field, table, bean, or enum | Defines plus relevant CSV | Runtime definitions, provider, consumers, and tests as required |
| Target, group, or data-root change | `luban.conf` and affected schema | Generator/runtime loading only when outputs or loading contract change |
| Generator behavior or path | `Config/gen.bat` | Active documentation and loading paths affected by the confirmed change |
| Runtime query or validation | Config source when data contract changes | Provider interface, definitions, adapter, consumers, and tests |

Do not add a schema field because one consumer needs a temporary value if an existing stable field already represents it. Do not hide configuration defaults in gameplay code, Views, or tests to avoid changing the real source.

## Change Workflow

1. State current source, generated output, runtime consumer, owning requirement, and unsupported facts marked `待确认`.
2. Define IDs, types, defaults, optionality, enum values, groups, keys, references, ordering, and failure semantics before editing.
3. Modify the smallest coherent source set. Keep headers and schema names exact; preserve stable IDs unless migration is explicitly designed.
4. Check all direct cross-table references and every runtime consumer affected by a renamed or added contract.
5. Run only the unique formal generation entry. On a device where generation is not authorized or available, do not run it and do not claim generated outputs are current.
6. If generation fails, stop, report the first reliable error and affected source, and repair the source/tool problem. Never patch generated C# or `.bytes` to simulate success.
7. Confirm the generator produced every client and server C# and `.bytes` target configured by the actual `luban.conf` without overwriting unrelated assets. New Unity outputs must include their `.meta` files when synchronized.
8. Update runtime definitions/provider only when the handwritten runtime contract requires it. Preserve deterministic validation and error reporting; do not expose generated mutable rows directly when a stable runtime definition exists.
9. Run only verification authorized by the current task and device rules. Prefer focused EditMode coverage for schema/provider/consumer behavior; do not introduce PlayMode automation unless the user explicitly changes the project constraint.
10. Report source changes, generated outputs, handwritten changes, executed validation, skipped validation, and unresolved risks separately.

## Failure and Review Rules

- Missing schema, CSV, generator, output, provider, or consumer evidence is `待确认`, not permission to invent a path or contract.
- A successful generator exit does not prove runtime reference validity, provider conversion, or consumer behavior.
- A passing runtime test does not prove generated output came from current sources.
- Reject duplicate IDs, missing required values, invalid enums, broken references, and incompatible provider mappings at the earliest established boundary.
- Do not broaden a value-only change into a schema or runtime API change without confirmed design.
- Preserve user changes and unrelated files in shared output directories.

## Example

For a new enemy field, define it once in the relevant XML contract, add the CSV column and values, regenerate through `Config/gen.bat`, then map and validate it through the existing runtime definition/provider only if gameplay consumes it. Do not add the property directly to generated `EnemyConfig.cs`, and do not duplicate a fallback constant in `EnemySystem`.

## Completion Check

- Actual sources, generation entry, outputs, provider, consumers, and tests were inspected.
- Each changed value or contract has one authoritative source.
- Generated files were not hand-edited.
- Cross-table IDs, enums, keys, defaults, groups, and provider mappings are consistent.
- Generation status and runtime validation status are reported separately.
- No unrelated asset under `Client/Assets/Game/Config/` was treated as generated output.
- Validation claims match actual evidence and `AGENTS.md` restrictions.

## Common Mistakes

- Editing generated C# because it is easier to find than the schema.
- Treating all of `Client/Assets/Game/Config/` as generated and overwriting unrelated assets.
- Adding a second generator or compatibility batch file instead of maintaining one entry.
- Renaming an ID without checking references, saves, constants, and consumers.
- Updating CSV without regenerating, or regenerating without reviewing runtime mappings.
- Copying config defaults into runtime code or tests to make a failure disappear.
- Claiming configuration completion from generator exit code alone.
