# 阶段2商品业务与开局外观

本阶段已实施，可交独立审查；未操作index、未提交、未推送。未修改正式视觉资源或UI。开工Unity确认为E:/GitHub/FirepowerLoop/Client，Boot非Play且不脏；Console旧资源错误与参数探测错误不作为新编译错误。

## 对接API
- SurvivorSystem.TryReadCharacterShop(out SurvivorCharacterShopSnapshot snapshot,out string error)：snapshot.Balance、Products；每商品Id/AppearanceId/Price/NameKey/Owned。Products为本次独立快照，不影响权威目录。默认0不在收费列表，由UI单独呈现。
- TryPurchaseCharacter(string productId,out string error)：仅接商品ID，Config权威价。账本同文件事务成功（含已拥有幂等）后更新Model余额并发布SurvivorChangedEvent。
- TrySelectCharacter(int appearanceId,out string error)：0免费，其余须当前目录有效且已拥有；活动局拒绝修改。SelectedCharacter与RunCharacter在SurvivorModel只读公开。
- 商品稳定ID survivor.character.1/7/3/4/9；名称key ui.survivor.character.0/1/7/3/4/9，中英已由Config发布。
- 业务错误character.config_failed、character.unknown、character.not_owned、character.run_active、character.appearance_failed；钱包沿用wallet.read_failed/insufficient/transaction_failed/invalid_product。阶段3将这些code映射本地化提示（本阶段未新增错误译文）。

## 开局与重开
StartAsync在进图前验证选择；TryStart再次验证，随后记录RunCharacter。同步SurvivorCharacterApplyEvent由既有PlayerCtrl订阅，使用已创建真实人物的ApplyWholeBody；失败抛异常进入现有开局回滚。衣物刷新以活动幸存者RunCharacter优先，避免诊断造型覆盖。Stop保留SelectedCharacter与RunCharacter，RestartAsync的先Stop不会抹掉选择；UI每次菜单进入仍须展示选角页。默认0虽可选择，但开局TryPrepare仍读取金币，坏钱包继续阻止开局。

## 配置、收集和验证
Config定义新增SurvivorCharacterConfig及五商品CSV，gen.bat预期表数客户端40→41、服务端36→37；正式入口双端生成成功，日志generation.log。新增bytes沿已有Configs目录收集和startup标签；生成Tables构造器加载新表，DataLoader以tbsurvivorcharacter.bytes同步加载，Boot启动下载startup和arcade。没有修改收集设置。

枚举并提前声明SurvivorCharacterTests最多3个无参数EditMode测试，includePassingTests=true。第一次MCP传输失败，第二次3/3通过，原始tests-result.json：真实配置价格/未知商品/余额不足/重复购买/未拥有拒绝/默认免费/Stop保留、正式GrayboxPlayer六种ApplyWholeBody与尺寸保持、BeginCollect精确新表地址唯一且startup。钱包测试使用临时目录，不读取真实玩家余额。没有执行完整套件或Player Build。

验证边界：真实Prefab应用测试不是正常YooAsset对局实拍；完整StartAsync→RestartAsync、正式PlayerCtrl事件链及动作穿模、运行包加载与UI旧异步门禁留阶段3整体实测，不将它们报告通过。未获得测试先失败的红灯，不声明TDD红绿完成。

changes.json列仅本阶段文件；review.patch为本阶段文本增量，bytes单列。手写源有开工前副本；生成Tables与发布语言JSON未及时复制，已按本次唯一新增成员/key反推基线，并在changes标注，独立审查需核对；不以HEAD全量diff夹带既有其他任务。
