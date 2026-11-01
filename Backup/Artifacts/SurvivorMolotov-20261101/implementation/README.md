# 燃烧弹接入与尺寸修正

用户采用淡焦痕并要求缩小飞行瓶子，随后确认继续接入。瓶子缩至旧临时模型45%，火区不缩小。正式效果见../formal/preview.gif。

## 实现

- Molotov独占一个武器槽，与步枪/无人机等级独立；固定5秒冷却、0.8秒飞行、3秒火区，仅DPS和半径成长。
- 五级DPS：12/18/26/36/48；半径：1.2/1.425/1.65/1.875/2.1米。不扣步枪弹药，不继承步枪弹体强化。
- 2在途瓶/4火区预算；每0.25秒局部空间查询，余量累计，沿现有死亡事务。不伤墙后与尚未露出地面的敌人。
- 2/4有界表现池；180火焰粒/区，落地短闪每粒子系统上限48；纯权威年龄采样，暂停冻结、退出清理。
- 沿已有公共Content资源异步事务（代次/地图身份/句柄归属），幸存者入场额外等待模板；缺失或失败走原错误出口。

## 验证

9项唯一限定EditMode用例全部有通过证据。presentation.result.json首轮3项包含2通过和粒子失败；对应MolotovPresentationIsBoundedReusableAndManuallySampled.result.json修复后通过，资源收集/图标未修改无需重跑。
其余6个Molotov/MaxedLimited方法result.json为各自通过结果。遮挡回归先红后绿，obstruction-red.result.json记录墙后误伤64HP，绿色结果验证100HP。零年龄首次采样追加验证产粒16，见zero-age-sampling.txt。

real-collection.txt由真实BeginCollect(DefaultPackage,false,false)只读查询产生，精确地址：Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet、Prefabs/UI/Survivor/SurvivorHud。两者arcade标签、正式Prefab/材质/纹理/图标依赖以及Boot启动arcade标签均通过。

runtime-load.txt：正常Boot Play，真实Game.YooAssetResourceLoader，EditorSimulateMode，两地址成功，嵌套模板、45%瓶体、淡焦痕材质、粒子材质、图标引用有效，句柄均释放。没有资源Build、Player Build、手动模拟清单构建或发布动作。未验证发布包或整局实战。

formal/preview.gif用正式模板与ArcadeCombatViewController调用采样，主角正式模型同框。测试与预览结束Boot未脏。

只读复审发现墙后/地下误伤P2，修复并红绿验证后复审无P1/P2阻塞；粒子停止后直接增量模拟为0粒，首次restart模拟为16粒，修复后池复用测试通过。

Config/gen.bat双端生成成功，见config-generation.log。生成输出没有手工修改。
