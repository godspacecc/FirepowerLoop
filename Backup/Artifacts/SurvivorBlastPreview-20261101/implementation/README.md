# 幸存者短爆燃正式接入

2026-11-01用户采用右侧候选，正式接入及验证于2026-11-02完成。

保留SurvivorWeaponBlast原GUID、根节点与View fileID及GrayboxBullet引用。正式独立Prefab改为闪光0.14秒（尺寸1.12倍）、爆燃0.28秒（数量75%）、短烟0.4秒（数量45%、透明度42%），不添加放射火星。闯关原版、伤害、范围、触发、互斥、0.4秒领域寿命和表现预算均保持；未增加音频。

SurvivorBlastView只由Apply归一化进度手动推进，重复进度不前进、倒退重采样，粒子不自动播放，回池清空。既有投射物准备事务检查模板与粒子材质，缺失依赖沿原事务回滚。

## 验证

13个不同限定EditMode案例通过，includePassingTests=true：SurvivorBlastVisualTests最多8例；SurvivorRuntimeTests五个完整方法各1例（名单、红绿结果见test-summary.json）。新资源先8/8失败于粒子数量0，再正式接入后8/8通过；验证覆盖正式入口缺失依赖、暂停、反向采样、复用清空、尺度；五个既有方法覆盖伤害、预算、发射快照、障碍和击杀结算。初次测试传输超时未算结果，确认编辑器空闲后重跑同一有效类。

formal-blast.gif为正式资源隔离复拍，非实际战斗录像：左为采用候选，中间为正式资源，右为暂停/恢复；同镜头、1.4米半径、正式2米主角。78张源帧编码合并静态重复帧后为38帧GIF，时长3.9秒，暂停检查通过，见formal-capture.txt与media-check.json。

真实BeginCollect(false,false)确认实际预载地址Prefabs/Combat/GrayboxBullet唯一、arcade标签、短爆燃及三份材质/贴图/Shader依赖；见source-collection.txt。沿既有投射物准备事务加载，无新增逻辑地址或下载标签。

正常Boot Play、Game.YooAssetResourceLoader、EditorSimulateMode真实异步加载通过，原模板引用与三粒子/材质/纹理/Shader有效，句柄released=True，见runtime-load.txt；检查后退出运行。此证据不代表旧发布包内容；未打包或发布。

review_short_blast独立审查未发现问题，见review.md；unchanged-boundaries.json证明闯关效果、幸存者伤害源/配置及GrayboxBullet与开工快照相同。只提交本任务，隔离index避开其他聊天暂存内容。
