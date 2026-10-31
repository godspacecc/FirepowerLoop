# 阶段3正式商店与开局选择

已实现：五商品商店与六角色选角，复用现有MainMenuShop/SurvivorEntry，独立CharacterCardBox由Box生成器生成，宿主CustomListView直接引用外部Box。六张原始模型头像导入为Sprite；ReferenceCollector与正式UI生成器生成三份View并回绑。金币小字金色、可购买/选中金色、已拥有禁买、未拥有暗色禁选。每次菜单进入先选角，菜单实例/代次检查阻止旧回调启动；选择业务重新验证所有权，关闭/卸载清除回调。

购买仅提交ID，余额/拥有来自同次快照，坏档未知余额并显示本地化错误，语言切换动态刷新。Config/Localization/survivor.json新增文案，经Config/gen.bat双端生成，generation.log证明完成。未手写生成View或生成配置。

## 验证
- MainMenuShopInteractionTests指定短类最大1项，includePassingTests=true，1/1通过。真实五卡、重开、语言切换、坏档不覆盖。原始interaction-tests.json。
- CharacterUiTests指定短类最大1项，includePassingTests=true，1/1通过。真实按钮购买/重复/不足、外部Box绑定、六卡、默认免费、锁定拒绝、选中、关闭旧回调失效、重复开始门禁及重开保留。原始purchase-selection-tests.json。全部隔离临时钱包。未取得先失败红灯，不声明完整TDD红绿。
- 真实BeginCollect四地址：Prefabs/UI/Home/MainMenuShop startup；Prefabs/UI/Survivor/SurvivorEntry与Box/CharacterCardBox startup+arcade；tbsurvivorcharacter.bytes startup。source-collection.json。头像通过宿主Sprite字段序列化引用，无新增直接加载地址；沿启动startup下载，UI由BaseUICtrl加载，卡片由CustomListView实例化。
- 正常Boot Play进入Main，Game.YooAssetResourceLoader真实异步加载四地址均成功并释放句柄。临时金币与结果账本注入后StartAsync/RestartAsync/ReturnToMenuAsync均成功；两次真实玩家ClothingAppearanceView索引1、尺寸(1,1,1)，说明PlayerCtrl同步事件链实际生效。runtime.txt及Runtime.cs。未修改模型/Animator/武器/碰撞。未进行逐帧动画穿模审查，不声明所有动作均无穿模。
- 为保持临时结果账本，SurvivorSystem.TryPrepare结果存储改为??=。SurvivorResultFileStore唯一实例字段readonly path，无跨局内存状态，正常玩家行为一致。运行finally恢复原两账本；随后退出Play。未触玩家金币或结果存档，未打包/模拟构建/重建清单。
- shop-final.png/selection-final.png为正式Prefab克隆渲染，1200余额及部分拥有状态是示例数据；原型不写存档。shop-english.png/selection-english.png用正式英文表文案检查布局。均1920x1080；中英文容纳已查看。未验证额外宽高比，未验证旧发布包。

## 交付与审查
changes.json与review.patch仅本阶段增量，含新增头像png/meta、Box及生成View/meta、测试meta。baseline包含UI/Controller、Config本地化、发布语言及服务端Tables；客户端Tables未在生成前备份，但本阶段无schema修改且未纳入增量，未借HEAD夹带其他改动。SurvivorSystem基线另于最小??=修改前复制。未操作index、未提交/推送、未建立worktree。

编译/Play切换期间MCP两次10-retry传输失败、一次Boot临时脏拒绝测试；按既有授权重载Boot并以同过滤重试，未回退全套。最终交root独立审查，整体提交由root处理。

## 最终生成契约补证
已枚举并分别运行Game.Tests.MainMenuShopTests.ShopHasGeneratedReferencesAndMenuEntry与Game.Tests.SurvivorUiTests.GoldEntryHasGeneratedReferencesAndDynamicBalance，各最大1项、includePassingTests=true，各1/1通过，原始shop-contract-tests.json/entry-contract-tests.json。入口Bound辅助方法遍历所有生成字段；另generated-contract.json只读反序列化两个宿主及Box，逐个确认全部Unity对象字段非空且与根ReferenceCollector精确一致，Box继承BaseBoxUI。未运行其余SurvivorUiTests。

## 最终P2修复
StartSurvivorAsync失败提示出口通过FormatSurvivorEntryError映射character.*与wallet.*到既有本地化文案，其他普通场景错误保持原说明。TryPrepare默认免费角色的钱包读取失败也归一wallet.read_failed，详细原因仅保留内部日志；结果账本读取及其他领域错误不改。补齐商品/商店快照属性与构造函数、PlayerCtrl订阅字段中文注释，更新过时只读商店说明。
精准真实方法Game.Tests.CharacterEntryFailureTests.RevalidationFailureUsesLocalizedEntryMessage，最大1项无参数展开，includePassingTests=true，最终1/1通过（fix-final-test.json）。使用临时已购角色1与默认0，选中后损坏钱包，再调用真实StartAsync，均安全拒绝并呈现明确中文；覆盖英文钱包提示、未拥有、配置失败及普通场景文本保留，坏文件不覆盖。初版默认0测试发现原始JSON错误泄露后按root指令补齐，不把首次失败误称映射缺失红灯；原始各轮回执均保存。fix.patch为本轮增量，review.patch及changes.json已更新；没有index/提交操作。
