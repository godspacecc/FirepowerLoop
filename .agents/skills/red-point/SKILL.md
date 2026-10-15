---
name: red-point
description: Use when adding, modifying, reviewing, diagnosing, or explaining AnotherDay red points, RedPointSystem, RedPointMod, RedPointView, red-point IDs, dirty refresh, parent aggregation, or business events that drive notification indicators.
---

# Red Point

## Core Rule

Keep red points as derived presentation state. Business owners publish authoritative state and domain events; a concrete `RedPointMod` observes those events and derives one red-point state; `RedPointSystem` schedules refresh, aggregates the tree, and publishes changes; UI only renders the resulting state.

Never replace this boundary with business-side `SetLeaf()` calls or by moving every business rule into `RedPointSystem`.

## Evidence Gate

1. Read `AGENTS.md`, the current AnotherDay implementation, registration point, event producers, Models, UI consumers, and nearby tests.
2. When this device is responsible for design or architecture research, follow its project rules for consulting available reference projects. An implementation device executing an approved plan does not require another device's reference checkout.
3. If reference projects are unavailable, do not invent reference facts. Use the current AnotherDay implementation, the approved implementation scope, and the rules in this Skill; report any unresolved conflict or unknown.
4. Separate `AnotherDay 已确认事实`, reference facts, new design, and unknowns. Existing AnotherDay `SetLeaf()` usage proves current behavior, not the intended architecture.

## Ownership

| Concern | Owner |
| --- | --- |
| Authoritative inventory, task, update, mail, or other state | Owning Model/System |
| Domain mutation and post-commit event | Owning System |
| One business rule's active/count projection | Concrete `RedPointMod` |
| Module registry, dirty queue, refresh scheduling, parent aggregation, changed event | `RedPointSystem` |
| Stable node identity and hierarchy | Red-point ID/tree definition |
| Binding, initial query, mark/count rendering | `RedPointView` or owning UI Controller |

The required dependency flow is:

```text
Business Model/System -> domain event -> Concrete RedPointMod
Concrete RedPointMod -> dirty notification -> RedPointSystem
RedPointSystem -> RedPointChangedEvent -> UI
```

Business code must not reference `RedPointSystem`, `RedPointMod`, `RedPointIds`, `RedPointView`, or a red-point display object.

## Concrete Module Contract

For each leaf rule:

1. Identify the authoritative Model state and every post-commit domain event that can change the projection. Do not infer state from localized text, UI visibility, reason strings, or mutable Views.
2. Implement a concrete `RedPointMod`. It subscribes to those domain events, tracks every unregister handle, and calls `NotifyChange()`; it does not receive a pushed active/count value from the business owner.
3. Derive active/count from the current authoritative Model in `OnCheckActive()` and `OnGetCount()`. Repeated refresh must be idempotent.
4. Register the module through architecture composition or the established `RedPointSystem` module-registration boundary. Ensure an initial dirty refresh occurs.
5. On detach/deinit, unregister every event and lifecycle callback. A late event must not revive a detached module.

For composite nodes, aggregate child state in `RedPointSystem`; do not duplicate parent writes in child modules or business Systems. Refresh dirty modules in a bounded shared lifecycle phase and emit `RedPointChangedEvent` only when active/count actually changes.

## Example

Resource update should use a dedicated module shaped like this, adapted to the actual restored base API:

```csharp
public sealed class ResourceUpdateRedPointMod : RedPointMod
{
    private ResourceUpdateModel model;

    public void Initialize()
    {
        model = this.GetModel<ResourceUpdateModel>();
        Track(this.RegisterEvent<ResourceUpdateChangedEvent>(_ => NotifyChange()));
    }

    protected override bool OnCheckActive()
    {
        return model.Snapshot.Value.Status == ResourceUpdateStatus.UpdateAvailable;
    }

    protected override int OnGetCount()
    {
        return OnCheckActive() ? 1 : 0;
    }
}
```

`ResourceUpdateSystem` still owns checking and snapshot publication. It must not call a red-point API.

## UI and Lifecycle

- UI queries current red-point state when enabled, then listens for the matching changed event.
- Toggle a dedicated visual child, not the GameObject that owns the observer component; disabling the observer root can silently unsubscribe it.
- UI opening or closing is not the default source of truth for “viewed”. If viewing is a domain fact, submit that intent to the owning System, let it update authoritative state, and let the resulting domain event dirty the module.
- Prefab references, node IDs, count labels, default visibility, and observer lifetime must be verified together.

## Review and Test Gate

Before accepting a red-point change, verify:

- No business System directly pushes red-point active/count state.
- No concrete business projection is embedded in generic `RedPointSystem`.
- The concrete module covers initial state, all relevant domain events, repeated events, detach, and current Model reads.
- Parent active/count aggregation and event deduplication remain correct.
- UI enable/disable cycles neither miss current state nor accumulate subscriptions.
- EditMode behavior tests use real domain events and Model state; static rules may guard the forbidden business-to-red-point dependency.
- Do not add PlayMode automation unless the user explicitly changes the project rule.

## Stop Conditions

Stop and return to design instead of simplifying the architecture when:

- `RedPointMod` is absent or current code exposes only `SetLeaf()`;
- the authoritative “unread/unviewed/update available” state is undefined;
- required domain events are missing or fire before authoritative commit;
- the requested file limit forbids the module, registration, lifecycle, UI binding, or tests needed for a coherent slice.

“Only one leaf,” “faster to call `SetLeaf()`,” “the current code already does it,” or “the deadline forbids a module” are not valid reasons to bypass `RedPointMod`. Report the scope conflict explicitly.

## Common Mistakes

| Rationalization | Required response |
| --- | --- |
| “Current AnotherDay code already uses `SetLeaf()`.” | Record it as current behavior and check the mature module design; do not promote the shortcut into the contract. |
| “There is only one leaf, so a module is unnecessary.” | A leaf's business projection is exactly the responsibility of one concrete `RedPointMod`. |
| “Put the mapping in `RedPointSystem` to remove the business dependency.” | This turns the generic registry/aggregator into a business-rule owner; use a concrete module. |
| “The deadline or file limit forbids a module.” | Report an incoherent scope and stop; delivery pressure does not reverse ownership. |
| “Fixing the Prefab completes the red-point task.” | Treat UI wiring and business projection as separate requirements; verify both. |

## Red Flags

- A business System calls `SetLeaf()`, `MarkDirty()`, or another red-point mutation API.
- `RedPointSystem` switches on inventory, task, resource-update, mail, or other business state.
- Active/count is pushed into a module instead of derived from an authoritative Model.
- A red-point observer disables its own GameObject to hide the mark.
- Missing `RedPointMod` is described as simplification rather than an architecture gap.

Any red flag requires stopping the proposed implementation and correcting the responsibility model first.

## Required Companion Skills

- **REQUIRED SUB-SKILL:** Use `code-role` for handwritten runtime C# and QFramework ownership.
- **REQUIRED SUB-SKILL:** Use `unity-ui` when Prefabs, `RedPointView`, UI Controllers, serialized references, or UI lifecycle are involved.
- Before changing behavior or architecture, satisfy the design-confirmation and implementation-authorization workflow defined by this device. This Skill does not provide product authorization or replace that workflow.
