# 喷火器持枪偏左修复 · 2026-10-20

## 原因与修复

真实 GrayboxPlayer 在正式 Humanoid Animator 下，喷火器待机枪管水平朝向为左偏约 44.7°。现有目录挂点通过直接 `Clip.SampleAnimation` 校准，而正式 Animator 的主手姿态与该采样结果相差约 45°；原有测试只覆盖手动采样，所以未发现正式播放偏差。本工程本机姿态文件没有喷火器覆盖项。

在现有 `CharacterWeaponPresenter.ApplyCurrentAnimationPose` 内先恢复局部挂点，然后仅对喷火器、非手动采样且当前动画允许握持时，围绕角色上轴旋转主手，使枪管水平投影对齐角色前方。保持枪体跟随手腕与握点，保留动画俯仰、翻滚，不修改角色根、伤害、射程、火焰资源或其他武器。枪口读取和 LateUpdate 继续共用同一入口，副手在主手校正后求解；重复调用不叠加固定角度。换弹、受击、手动采样沿用原动画。

## 验证

所有测试均为明确的 EditMode 范围，`includePassingTests=true`；未运行完整套件或 Player Build。

- `FlamethrowerAimTests`：声明上限 14 项。先运行 12 项，其中 11 项因正式播放左偏而失败；修复后加上受击与换弹排除检查，最终 **14/14 通过**。包含待机、四向移动、连续喷射、中途转向、重复枪口读取、切回步枪。
- `Game.Tests.ArcadeWeaponVisualTests.FlamethrowerBarrelFacesFiringDirectionAcrossCombatPoses`：上限 1 项，1/1 通过。
- `Game.Tests.ArcadeWeaponVisualTests.FlamethrowerSharedPresentationUsesItsNozzleAndRestoresBasicWeapon`：上限 1 项，1/1 通过。
- `Game.Tests.ArcadeWeaponVisualTests.FlamethrowerMuzzleEmissionStartsAtNozzleAndEndsAfterFireOrWeaponChange`：上限 1 项，1/1 通过。
- 最终合计 **17/17 通过**。结果见 `tests.json`。只读代码复核未发现阻塞问题。

## 实际资源对照

`comparison.png` 使用现有角色、喷火器、共享 Animator，在临时预览场景渲染。左侧在同一正式 Animator 姿态下跳过新增校正，复现原挂点方向；右侧走修复后的正式入口。白线为角色前方，红/绿线为喷嘴方向。测量结果见 `probe.txt`：左侧 -44.69683°，右侧 0°。

复现入口为 `Tools/Unity/FlamethrowerAimPreview.cs`。未修改 Prefab、模型、目录资产或保存场景；结果是编辑器内正式资源验证，未进行双机实战或设备性能测试。单人和联机角色沿用共享持枪入口。

提交前按上述相同范围再次复跑 17/17 通过。提交仅包含本次修复、测试和预览记录，其他未提交改动保留在工作区。
