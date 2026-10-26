# 终局小龙正式替换（2026-10-26）

用户看过 FinalDragonModel-v1-20261026 模型与动作预览后明确“那就替换”。本批据此保存正式资源并替换终局 Boss；其它设备旧飞龙废弃决定仍只适用于该旧批次。

[正式资源画面与视频](index.html)读取 ArcadeFinalDragon.prefab，与正式主角 scale 1/1/1 同框。视频为正式片段隔离回放，包含用于检查姿态的演示路径，不是整局实战录像。游戏内根位置始终由原战斗规则决定。

## 交付

- 正式美术：Client/Assets/Game/Art/Enemies/Creatures/FinalDragon；118 个网格、3808 三角面、7 个材质、8 个动画片段（含2个上身收招片段）。Prefab 为 Client/Assets/Game/Prefabs/Characters/ArcadeFinalDragon.prefab。
- 既有 ArcadeObstacleVisualSet 的 Content/FinalDragon 提供预载模板。当前地图统一创建与销毁，缺少模板时等待现有加载事务，不生成旧巨猿替身。
- 单人和现有双人按权威年龄、Boss阶段与锁向采样。真实位移驱动步态，重复快照和暂停不自行推进；张口后继续收口及展翼，上身覆盖允许腿部保持追击步态。新技能中断收招。
- 未改变血量、伤害、攻击窗口、增援、射线或胜利出口；未改配置/协议，也未添加飞行或喷火玩法。历史资源及 meta 保留。

## 验证证据

所有测试均明确真实短 testClass 或完整 testMethod、固定最大数量，includePassingTests=true；无整套测试或过滤回退。

| 范围 | 上限 | 结果 |
|---|---:|---:|
| FinalDragonPresentationTests | 9 | 9/9 |
| CreaturePresentationTests | 14 | 14/14 |
| ArcadeRoundContentViewTests | 7 | 7/7 |
| ArcadeCasualFinalBossTests | 17 | 17/17 |
| FinalDragonOnline | 4 | 4/4 |
| TenStageRouteOnline | 8 | 8/8 |

[汇总](verification.json)。[首次RED](tests-red.json)确认7项缺失资源/入口失败；[复核RED](review-red.json)确认原7项通过、新2项确实失败；[最终GREEN](tests-green.json)9项全通过。服务端[小龙快照](server-check.txt)及[旧路线](server-route-check.txt)分别4/4、8/8。

[正式回读](readback.txt)：118份网格顶点/法线/索引数组与采用源完全一致，118个Renderer颜色一致；8片段626个采样点局部位置和旋转最大偏差均为0，演员根不被动画移动。[尺寸与场景检查](model-audit.txt)中最低点高于地面0.003259米、无Collider；12秒和24秒视频均240帧完整解码，见[视频记录](video-audit.json)。

独立代码复核发现重复移动快照改变姿势、张口收招截断两项Important，均通过新增RED定位并修复，复审关闭。上身曲线仅颈/头/颌/翼，不覆盖脚或根。

第一次动态烘焙曾遇到缺少接口声明、工具别名冲突及资源逐项导入导致MCP回复失败。已补接口、统一别名并批量导入，持久对象名称与文件名同步；最终[烘焙调用](bake-result.json)成功。[最终编辑器状态](probe-after.json)：正确工程、Boot未脏、未Play、无编译/导入中任务、Console错误0；28条已有代码编译警告仍保留。测试框架可能重置Console，不能将最终计数解释为全程从未出现错误。

原有33份本机差异SHA256均保持不变，不纳入本次提交。未进行真人双端整局、真机验收或Player Build，不以隔离回放替代这些验收。

## 重现

正式资源工具：Tools/Unity/Invoke-FinalDragonBake.ps1；回读加 `-Readback`。画面工具：Tools/Unity/Invoke-FinalDragonPreview.ps1 `-Formal -Video`。编码工具：Tools/Unity/encode-final-dragon-preview.py `--formal --ffmpeg <本机ffmpeg路径>`。工具只允许连接本工程，不保存Boot场景。

提交与推送以本仓库Git记录为准；本批已获得自动提交与推送授权。
