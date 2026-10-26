# Task B scoped review

## Strengths
- Actor clips exclude actor-root curves; runtime roots remain driven by gameplay position and facing. Gait is sampled from observed travel, not elapsed preview time.
- Spider uses real melee windows; Ghoul uses locked rush except melee/telegraph target facing; RedMangler uses target facing; ZRex samples its existing three-cycle rules. Server changes only project existing fields and add no protocol or damage rules.
- Actor instances carry no independent resource handles. Existing map-owned removal/release paths destroy instances and retain shared meshes/materials. Readiness checks require all six named clips before exposing the layout as ready.
- Existing bounded test/readback evidence is documented: 20 client, 4 server, 251 meshes and 865 clip samples. I did not rerun tests or call Unity.

## Issues

### Critical
None found.

### Important
1. **Move plus upper-body overlays break the shared shoulder height.** `Tools/Unity/TenStageCreatureBake.cs:89-90,110`; composed at `Client/Assets/Game/Scripts/Controller/Arcade/ArcadeCombatViewController.AdoptedCreatures.cs:43-49,65-68`.
   - Upper-body baking moves Core and every arm segment back to fixed `baseHip`, then omits Core position curves. Runtime first samples Move, whose RedMangler Core has up to 0.025m bob and ZRex Core up to 0.07m bob, then overlays fixed-height arms without replacing Core position. Core-mounted torso/shoulders therefore move relative to the upper-arm attachment by that bob amount (including ZRex's normal moving charge). This violates the required local-pose/limb-interface integrity even though every individual clip matches its transformed source.
   - Preserve a common translation for torso and arms when composing gait and upper actions: e.g. apply the current Core displacement from baseHip to the baked arm positions after the upper overlay, or restructure the shared transform ownership without changing adopted geometry. Verify the composed Move + Rush/action poses at several nonzero travel phases, checking upper-arm start against the Core-transformed shoulder anchor.
   - Current readback (`Tools/Unity/TenStageCreatureReadback.cs:38`) samples Idle before each upper clip, so Core is at baseHip and this production composition is absent from all 865 comparisons. Add a bounded composed-pose assertion or extend the existing readback within the allowed test budget.

### Minor
None raised.

## Declined to judge
- Task A environment/config changes: explicitly assigned to a separate reviewer.
- Natural play, actual devices, and two human clients: user paused these checks.
- Final playable isolated videos: capture still in progress and primary owns visual verification.
- Existing small-dragon/burrow/hound regression and cross-task final integration: explicitly reserved for primary.

## Assessment
- **Spec verdict: With fixes.** Geometry persistence and authority/lifecycle boundaries satisfy the inspected requirements, but moving upper-body composition does not preserve the required limb interfaces.
- **Quality verdict: With fixes.** No new gameplay Update, protocol, damage rule, or standalone handle owner was introduced. The composition defect and its blind spot in the readback should be corrected before commit.
- **Ready to merge: With fixes**, also subject to primary's pending video and integration checks.

Scope: supplied Task B diff (base 525e828e to uncommitted Task B), targeted source reads only for named animation composition, actual movement/attack rules, revision filtering, and map release/readiness risks. No production/index changes, no Unity calls, no test executions, no subagents.
