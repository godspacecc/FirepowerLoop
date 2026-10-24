# 骷髅、无人机及HUD采用版正式接入

用户于2026-10-24对`DropHudScale-v1-20261024`实际Unity模型同框与完整HUD对照明确选择“采用这组预览，正式接入（推荐）”。本记录承接该确认；第4项零进度隐藏已由feea8160交付，本次完成其余三项。

## 正式结果

- [红紫骷髅与主角、金币同框实拍](skulls-applied.png)：两种骷髅按金币实际最长边0.349581米等比校准，仅修改两个Prefab根比例。
- [无人机与主角同框实拍](drones-applied.png)：图中主角右侧依次为空投展示0.85米、随身0.595米。随身根缩放使用原精确值0.60283685乘0.7；空投尺寸绑定只作用于无人机，保留原空投定位，其余空投武器仍1.7米。
- [完整PC HUD零进度](hud-pc-zero-applied.png)、[完整PC HUD有进度](hud-pc-progress-applied.png)、[完整手机HUD零进度](hud-phone-zero-applied.png)：红紫骷髅按类别各轴0.9，普通图标恢复1，每次绑定覆盖而非累乘。HUD固定布局、正式UI Prefab及生成View不变。

以上均直接加载正式Prefab、使用正式显示逻辑，未补施临时候选比例；是编辑态隔离实拍，不是完整对局截图。主角、基础AK、机位与采用版一致。三张HUD图与采用版PNG的SHA256逐一相同。两张模型图各432万颜色通道中仅34/20个通道有栅格边缘差异，其余一致，尺寸与机位一致；详见`preview-pixel-comparison.json`。

## 验证

只运行以下真实短测试类及精确非参数方法，全部EditMode、`includePassingTests=true`，没有完整套件、Player Build或资源打包：

| 测试类 | 每次上限 | 验证结果 |
| --- | ---: | --- |
| ArcadeDronePresentationTests | 6 | 6/6，精确尺寸、异步迟到、双机独立、回收及着色 |
| ArcadeWeaponDeliveryTests | 9 | 9/9，空投阶段、尺寸/姿态、重复同步、普通复用、其它武器及中心旋转 |
| ConfirmedWeaponPresentationTests | 8 | 8/8，红紫等金币、重复展示和回池、基础枪及普通资源回归 |
| ArcadeCompactHudTests | 28 | 28/28，红紫90%、普通图标还原、脉冲和两端完整HUD |
| ArcadeMultiplierHudTests | 6 | 6/6，先前零进度隐藏、恢复、最高倍率及单人/联机投影 |

共57项不同检查通过。最后为对齐采用版浮空位置，又复跑Delivery9/9与Confirmed8/8；补充正式Prefab初值断言后，完整非参数方法`Game.Tests.ArcadeWeaponDeliveryTests.DroneDeliveryKeepsApprovedSizeAndDoesNotResizeOtherWeapons`再1/1通过。测试结果保存在同目录JSON。

先红后绿记录保留：旧尺寸分别导致骷髅、随身、空投、HUD用例失败；初次持久化误用了工具四舍五入的根缩放0.60，尺寸检查捕获0.5922与0.595偏差，已按HEAD源值0.60283685修正。正式重拍又发现直接适配0.85会改变浮空位置，高度回归先失败后修为原1.7定位再缩小实体，保持采用版位置。所有问题已修复并重新验证。

两个参数化方法过滤曾返回未匹配，保留`red-skull-size.json`与`red-hud-skull.json`作为工具诊断，不能记为执行成功或回归失败；没有用无效过滤回退整套。新增红紫尺寸用例改为一个明确非参数方法，实际RED及后续GREEN有独立记录；HUD的非参数复用用例实际RED。后续57项检查使用事先声明范围及上限的真实短类。

独立只读复审及浮空位置增量复审未发现运行时代码问题；已采纳“先断言正式模板maxSize为0.85、再做临时覆盖”的测试建议并验证通过。结束时Boot编辑态未脏、仅1个场景、无编译失败、Console错误0，未进行自然整局、真人双端或实机验收。
