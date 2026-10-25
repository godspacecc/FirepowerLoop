# 丧尸地面步态v2正式接入与验证

2026-10-25。用户否决持续矮身的v1后，对v2站高拖步动态回复“可以”，允许三种丧尸地面步态正式接入。码头攀爬v2、撤通道与出生规则不在本项。

[正式动画与采用版同框回放](formal-ground-walk.mp4) · [预览页](index.html) · [360帧核对记录](formal-capture-audit.json)

## 正式变化

- Shambler、Gaunt、Bloated分别新增独立GroundWalk片段，使用采用版0.82秒步周期、普通/瘦弱约6.5厘米和重型约5厘米抬脚，不持续下蹲。原Walk/Run_Arms片段保留，未改主角、骨长、比例或丧尸网格。
- 三个正式控制器仅Run改绑，按现有EnemyView真实位移速度以MoveSpeed/0.9调整步频；不整体加速Animator，Idle/Hit/Attack/Death状态和抓挠/受击/尸体逻辑保持原入口。单双人都经过同一View。运行时没有新IK、事件、资源异步加载或逐敌人Update。
- 制作工具直接采样采用版后烘焙腿部旋转和骨盆位移，上身原片段按周期重排；角色重制入口保留已采用Run绑定。新资源有独立meta，既有meta不变。

## 定向验证

- [步态最终9/9](ground-walk-final-green.json)：真实骨骼采样验证接地、非持续矮身、循环接缝；实际Animator验证移动速度翻倍则步频翻倍，Hit不加速；停步、死亡和回池后回到站立Idle。
- [外形3/3](civilian-green.json)：三种正式模型与正式主角人体/头部比例及脚底、燃烧挂点。
- [抓挠8/8](grab-green.json)：大幅抓挠、权威进度、取消、回池、死亡和联机方向映射。
- [死亡17/17](death-green.json)：单人/联机死亡来源、类型、可见移除和去重。初次[14通过/3失败](death-before-adapter-fix.json)因旧测试反射调用漏传现有players参数；只补五处测试调用，未改业务或断言，重跑17/17。

共37项不同限定EditMode检查通过，全部includePassingTests=true；逐类上限9/3/8/24。初次RED记录见[第一次](red-first.json)、[第二次](red-second.json)：滑步及步频6项真实复现；停步/死亡测试初期对过渡求值不足，改为连续小步推进后验证真实行为，不把这些测试设置问题说成旧业务缺陷。没有全套回退或Player Build。

两轮独立只读审查无P1/P2/P3，增量死亡测试适配和正式回放入口已复审。首次创建与后续覆盖文件哈希不同：独立审查通过只在内存中还原m_Name和m_ClipBindingConstant块，精确重建三份首次SHA，证明曲线/切线/循环数据一致，仅名称和可重建绑定缓存不同。后续两次烘焙、强制导入、最终9项检查与正式回放的三个动画文件逐字一致，无动作漂移。首次哈希见[bake-hashes.json](bake-hashes.json)，最终哈希见[final-bake-hashes.json](final-bake-hashes.json)。

## 画面与边界

完整18秒/360帧/20fps/1280×720视频逐帧对照已采用v2，三种各含常用俯视角与半速低机位，正式主角原尺寸同框。右侧通过正式Animator.Run取指定相位，左侧为采用版原帧；不是自然追击或真人双端录像。全帧最大平均颜色通道差为0.047/255，存在插值与轮廓栅格差异，未宣称逐像素一致。完整解码通过。

既有122份正式依赖只发生预期的三个controller变化，原源动画、Prefab、网格/材质与meta均无本项新增差异；其他会话原有未提交修改保留。未改Config、协议、移动/伤害/碰撞数值或其它关卡，未资源打包、Player Build或真人双端整局验收。结束Unity为本项目Boot编辑态、未脏、无编译/导入及Console错误。

制作与回放工具：`Tools/Unity/ZombieGroundWalkPreview.cs`、`ZombieGroundWalkBake.cs`、`encode-zombie-ground-walk-preview.py`、`encode-zombie-ground-walk-applied.py`。原始帧和中间烘焙副本本地保留，不作为正式资源。
