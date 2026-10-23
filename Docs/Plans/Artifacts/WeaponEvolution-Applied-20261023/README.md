# 已采用武器进化正式模板回放

用户采用顺序：v1第一组与双无人机；v2残火和宽束；v3榴弹（“采用 v3，继续接入”）。

- flame.gif：正式残火四层粒子、余烬和烟尾，8秒。
- piercing.gif：正式宽束白芯、蓝晕、双流线及能量粒子，5.2秒。
- grenade.gif：正式v3实体弹体、抛射弧线、主爆与三枚子爆，6.4秒。

均为960×754，50ms帧延时，循环播放；正式主角同框。采样克隆正式Prefab，使用正式ArcadeWeaponEvolutionVisual手动时钟；为隔离演示时序和半速回放，不代表自然对局或真实双端录像。GIF全部解码、帧数和时长检查通过，关键帧已目视检查。候选v1/v2/v3原件保留在相邻目录。

## 玩法和接入

基础AK击杀贯穿；速射第三次成功齐射追加两侧弹；霰弹每齐射震退近敌一次；火带持续2秒，每0.25秒造成当发25%伤害，同来源重叠不叠加；穿透枪第四次成功齐射发出宽束；榴弹首爆派生三枚有限寿命子榴弹且不递归。既有三联追踪火箭及两目标连锁射线保留。紫级无人机从一架增至两架，继续原本攻击，不复制手持武器。

单人与联机均消费发射时的强化身份；联机残火、宽度、榴弹飞行相位和第二无人机来自权威快照。爆炸回执去重，迟到加载沿当前视觉年龄恢复；退场清理及Boss清场后不重建子弹。

## 验证

限定EditMode，includePassingTests=true，未运行完整套件或Player Build。原始通过用例见Validation目录。

- client-evolution-22-final.json：22/22。
- visual-final-current.json：6/6。
- guardian-final.json：1/1。
- feedback-final.json：11/11。
- drone-regression.json：7/7。
- pair-view-green.json：6/6。
- ArcadePurpleRocketTests-regression.json：8/8。
- ArcadePurpleRayTests-regression.json：7/7。
- upgrade-final.json：7/7。

- 服务端WeaponEvolutionOnline限定最多24例：工作目录与选择提交的独立源码快照均24/24。独立源码快照中的原无人机7/7、火箭8/8、射线7/7、升级6/6通过。
- 提交快照的客户端597份源码通过C#编译：0错误，278条Unity序列化字段未赋值、隐藏成员或过时API等警告；不等同Player Build。
- Config/gen.bat双端生成及Tools/gen-protocol.ps1协议生成已执行；武器升级bytes两端SHA256一致，见config-hash.txt。
- 独立只读审查发现的碰撞顺序、齐射去重、重叠火带、强化到期、引信尾帧及Boss清场问题均修复并验证；最终复查无新增P1/P2。
- Unity连接本项目；Boot未脏、编辑态且未编译。Console残留另一预览AllExplosionsPreview的RenderTexture释放错误，堆栈保存在console-errors-final.json。

## 已有失败的对照记录

额外限定SlayerGuardianOnline 11例中，“Flame path crosses borrowed stone obstacle”失败，10/11通过。为确认归因，分别对修改前197a947f源码、配置及正式测试文件，以及本次选择提交源码快照重跑同一11例，均得到相同失败；本任务没有改该用例，未将该类宣称为全绿。本次新增的实际守卫死亡清理用例及客户端对应完整单方法均通过。
