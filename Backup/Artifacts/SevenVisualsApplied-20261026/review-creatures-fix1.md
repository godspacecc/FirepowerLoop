# Task B fix1 scoped re-review

**Original Important: ADDRESSED.**

`ArcadeAdoptedCreatureView.Sample` now caches the formal Prefab's Core bind position before the first sample and, after an upper-body clip resets its arm positions, adds the current Core displacement to all eight arm segment/joint transforms. This retains the gait's torso/leg motion while giving arms the same translation. Idle/Move skip this adjustment, and Spider has no Core, so their full-body clips retain their previous behavior. Each upper clip rewrites the affected arm positions before adding the displacement, avoiding accumulation across repeated samples. The cache is per actor instance and owns no handles.

The new two parameterized controller cases check both shoulder anchors over four travel/action phases, rather than only testing independent clips. Supplied `review-red.json` records the two new failures (RedMangler 0.0130758537m, ZRex 0.0250034332m); supplied `review-green.json` records 22/22 passed, 0 failed, 0 skipped, including both composed-pose cases. Existing 865-pose readback remains reported unchanged. The capture tool now checks both shoulders every frame and fails above 0.0003m before declaring its audit complete.

## New Critical / Important issues in fix
None found.

## Declined to judge
- Broader unchanged Task B behavior and cross-task regressions: outside this re-review's scope.
- Rerendered RedMangler/ZRex video appearance: capture is ongoing and primary owns viewing/acceptance.

**Scoped quality/spec verdict: PASS for fix1.** Original Important can be closed. Final integration remains subject to primary's pending capture and regression checks.

Read only: supplied three-file fix diff and existing evidence. No Unity calls, duplicate tests, production/index changes, or subagents.
