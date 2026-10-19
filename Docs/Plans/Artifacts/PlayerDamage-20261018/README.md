# 玩家受击与倒地表现验收

已接入单人及双人的正式 PlayerView。受击保留原有短暂闪红，增加红白接触闪点和最多 4 度胸部轻震；不切换街机全身 Hit 状态。倒地沿用正式 Death 动画，在 0.32 秒后显示短暂落尘。双人青绿十字和细环只镜像服务器已有的 Alive=false 且 DownedSeconds>0。

本机和远端初次收到已倒地快照时直接建立倒地终态；真实死亡时保留最后显示地点，重复死亡快照不重播。复活、禁用、退出街机及销毁均清理瞬时表现。没有修改伤害、无敌、冲刺、救援资格、救援半径、死亡或复活规则。

`three-stages.png` 为 Unity 隔离 PreviewScene 的正式资源渲染：从左到右为受击、倒地落尘、双人可救援状态。它不是实际对局截图，也没有包含完整 HUD、场景灯光和双端网络延迟。画面中的角色外观和死亡片段沿用当前资源。

EditMode 定向执行 26 项全部通过：ArcadePlayerDamageTests 9 项、CombatFeedbackTests 10 项、ArcadeDashVisualTests 7 项，均返回通过项。范围和结果见 verification.json。审查发现的本机重连补播问题已修复并复核。未做真实双客户端实测，未触发 Player Build。最终死亡仍沿用既有场景切换时机，动画或落尘可能被切场截断。

制作与预览入口：Tools/Unity/ArcadePlayerDamageAuthoring.cs、Tools/Unity/ArcadePlayerDamagePreview.cs。GrayboxPlayer.prefab 显式引用 PlayerDamage.prefab，材质、Shader、纹理与网格均通过资源依赖保留。

