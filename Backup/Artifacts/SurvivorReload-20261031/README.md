# 幸存者步枪弹夹与自动换弹
- 已确认：30发基础弹夹、空弹自动换弹、无限备弹；弹夹容量每层增加基础10%（3发），按层数累加，新容量下次装填生效，不补当前弹量。
- 首轮参数：换弹2秒；换弹加速每层减少0.2秒，5层后1秒并退出候选。无人机独立射击。每项均由Config权威表配置。
- Model拥有弹量/容量/换弹计时；System在有效帧推进。无目标或弹丸预算已满不扣弹；暂停冻结计时；强化不重置当前换弹；Stop清空、新局满弹。
- 用户质疑附加界面显示后，撤回额外HUD提议。只在既有三选一卡片显示实际发数或秒数，未改Prefab布局或动作。preview.png/reload-preview.gif仅为未采用HUD的概念提议，非正式实拍。
- RED：真实Fire连续尝试31次，原逻辑发31发，30发断言失败。
- GREEN：SurvivorRuntimeTests最大40项实际37/37；SurvivorConfigTests最大13项实际13/13；Game.Tests.SurvivorUiTests.RifleUpgradeTextFitsExistingCard最大1项实际1/1。均includePassingTests=true，共51项。
- Config/gen.bat双端生成成功。独立审查无阻断；未Player Build、未整局人工试玩。用户当前已开始的旧对局应重新开局读取新配置。
- 本批不包含其他会话的场内地面入场、敌池分时权重或字体工具改动。
