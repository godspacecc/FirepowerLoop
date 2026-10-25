# 码头海中攀爬v2正式接入

2026-10-25用户“这个也接入啊”，明确采用[DockSeaEntry-v2](../DockSeaEntry-v2-20261024/README.md)并允许正式接入。另两路、敌人数量/属性及既有背桶规则保持；地面接步采用已正式接入的GroundWalk v2。

[播放正式资源回放](applied-sea-entry.mp4) · [预览页](index.html) · [关键帧](contact-sheet.png)

正式地图与三种便服丧尸、正式主角按原尺寸同框。视频19秒、380帧、20fps，后10秒为登台近景四分之一速度。隔离场景通过正式EnemyView采样当前权威年龄，编辑态按正式骨矩阵刷新蒙皮；站稳后0.9米/秒为参考续步，视频不作为自然对局、双客户端连接或实际追击验收。真实出生、追击、伤害、协议另由下列入口检查验证。

## 正式实现

- 既有第三通道队列改为海中接近、抓边收膝、前脚登台、后腿蹬起、站稳接步。单人与服务端共用Started/Age/Lane及路径；6.75秒走入实体半径可站稳的10.9处，下一活动帧恢复追击。未完成仍能受伤，不攻击，不接受拥挤及武器位置推移；暂停/失联加载/过场不消耗进度。
- 三份40fps、6.75秒局部骨骼片段绑定各自Prefab；高度和倾斜只作用视觉根。断线续接直接采样当前年龄，重复/较旧状态不回退。水纹复用单张三环网格，隐藏/回池复位、最终释放；背桶含迟到绑定随视觉根，普通死亡从当前攀爬姿态继续原0.16秒失衡再交物理。
- 四张码头布局的第三入口内收为10.5|-4.1，新增右侧只限制行走的海面边界，弹道继续开放。保留布局Version1：轮前节点不保存活敌人与现场玩家坐标，恢复在原中央点，保留旧桶账本身份。配置指纹更新，双端经Config/gen.bat生成，协议经Tools/gen-protocol.ps1生成。
- 只从DocksMesh1移除1344个已采用条件的低位三角形，不扩大删除高位栏杆；重制工具同步同条件。[资源审计](resource-audit.json)检查48个原文件，变化仅一份网格和三份Prefab引用，原meta全部保持。新资源为三片段和泡沫材质。
- 配置发布遇真实已映射bytes无法COPY截断，publish-generated.ps1改用同目录完整临时文件原子替换；不改变meta。[真实映射夹具](publish-mapped-file-check.ps1)实测旧COPY失败，正式发布成功，旧视图、meta保持，相同hash跳过且无临时残留；见[日志](publish-mapped-file-check.log)。

## 限定验证

共69项不同Unity EditMode检查通过，全部includePassingTests=true；无完整套件或无效过滤回退：

|范围|实际通过/上限|记录|
|---|---|---|
|DockSeaEntryFlowTests|6/6|[出生与战斗](client-flow.json)|
|DockSeaEntryVisualTests|10/10|[表现与协议](client-visual.json)|
|DockBarrelFlowTests|7/7|[载桶](regression-dock-barrel.json)|
|GroundBarrelFlowTests|18/18|[地面桶](regression-ground-barrel.json)|
|ArcadeZombieGroundWalkTests|9/9|[地面步态](regression-ground-walk.json)|
|ArcadeZombieGrabVisualTests|8/8|[抓挠](regression-grab.json)|
|ArcadeRoundLayoutTests|5/5|[地图几何](regression-layout.json)|
|RoundNodeRoundTripsWithoutChangingMapIdentity(21)|1/1|[指定码头存档](regression-save.json)|
|WeaponDeathPhysicsTests|4/4|[武器死亡](regression-weapon-death.json)|
|NormalDeathStaggersBeforeHandingCurrentPoseToPhysics("ArcadeEnemyBasic")|1/1|[指定普通死亡](regression-normal-death.json)|

服务端独立工具DockSeaEntryOnline限定maxTests=9、includePassingTests=true，[9/9通过](server-green.log)，覆盖四轮三车道/预算、其它通道、活动冻结、完成后追击与近战、推移不改路径但保留伤害、快照深复制与MemoryPack终态、桶优先耐久和卸桶/爆桶。[无效过滤](server-invalid-filter.log)退出2，不回退。

初始RED：[客户端0/3](client-red-initial.json)、[服务端0/9](server-red-behavior.log)、[正式动画未接入3项失败](visual-red-initial.json)。扩展检查先复现[手套推移1.22786米](client-red-handglove.json)，修复后6/6；审查复现[普通死亡错误启用旧Animator](visual-red-death.json)，保留旧三参数死亡入口并新增仅采样死亡入口，修复后10/10及相邻5项通过。服务端最早server-red.log是布局连通性失败，不算行为RED；server-green-attempt.log是7/9，修正测试夹具的NextSpawnAt和EnemyIterationIds后真实入口9/9。首个未带(True)的参数方法匹配为空，停止该调用；明确完整参数方法后仅运行1项，没有扩大范围。

[采用姿态回读](pose-audit.json)：三体型各6个攀爬关键时刻，对照原采用版骨架和正式EnemyView，髋/上臂/膝/双脚世界坐标最大偏差0.000002米；接步段按已采用GroundWalk另作平滑衔接。[视频审计](video-audit.json)完整解码通过，代表帧已目视检查。

独立只读审查最终无剩余Critical/Important，见[审查记录](review.md)。配置/协议生成成功，双端布局bytes一致；Unity Boot保持未脏，正式片段执行和上述测试确认编译有效。Console保留本次开发途中已解决的旧编译/工具调用错误，不以清空历史掩盖失败。未Player Build、未真人双客户端整局实玩。

工具：Tools/Unity/DockSeaEntryBake.cs（与DockSeaEntryPreview.cs同编译单元）、DockSeaEntryPoseAudit.cs、DockSeaEntryAppliedPreview.cs、encode-dock-sea-entry-applied.py；原始预览源快照在source-v2/。源帧保留本批次frames/用于本机复查，提交只包含最终视频、代表帧、时间轴与证据。
