# 幸存者经验晶体与掉率

- 用户选择100%必掉，并确认采用蓝青色低多边形晶体及预览尺寸（高0.5米）。模型已按确认结果接入。
- `experience-candidate.png`：独立临时素材与正式主角模型同框尺寸预览；不是游戏实拍。
- `experience-implemented.png`：正式晶体Prefab与主角同框的隔离渲染核对；不是整局实拍。
- 经验配置新增ExperienceDropChance（0到1）与ExperienceVisualPrefabName，当前1；每份仍为原ExperiencePerKill=1。死亡必掉，地面数量达到上限时合并经验、不丢失奖励。
- 经验显示名称及模型独立于金币，拾取只增加本局升级经验；零掉率仍正常计入击杀和分数。
- 正式模型在Game/Art/Survivor/Experience；加载入口Prefab在Game/Prefabs/SceneObjects，使用ResourceAddress标准逻辑地址。新资源纳入LootCtrl地图候选准备、提交及失败释放流程。
- 通过Config/gen.bat双端生成，退出码0。两端tbsurvivorrule.bytes SHA256相同：FD9DB3AAC3CD7B9B3F6E9F0E4D116A19FF49931B36789854755239E58FF49E6F。
- 先运行新增掉率边界测试，因缺少规则入口失败；实现后SurvivorRulesTests 14/14、SurvivorConfigTests 9/9、SurvivorRuntimeTests 21/21通过。地址核对后补齐标准地址，并修复命名空间编译错误；重新运行受影响配置9项及运行21项全部通过。所有测试includePassingTests=true，未运行完整套件。
- 审查未发现阻断问题。未触发Player Build；未进行整局人工试玩。
