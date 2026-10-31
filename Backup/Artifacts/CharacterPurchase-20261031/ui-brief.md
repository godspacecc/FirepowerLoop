# 阶段3：已确认商店与每次幸存者开局选择

总体需求Docs/Plans/角色购买与开局选择-20261031.md；先读business/report.md、wallet/report.md及实际业务API。全部功能、候选、价格及shop-preview.png/select-preview.png两页视觉已经确认，直接接入。动态视频不另拍，仅必要技术兼容检查。保持本任务全部完成后才交root审查，不能只做页面外观。

## 呈现

正式UI沿两张批准预览：深色面板、顶部标题与小金币、卡片使用现有模型真实头像。商店五卡顺序1/7/3/4/9，价格50/100/150/200/300，已拥有禁重复购买，底部说明+返回。选角六卡0/1/7/3/4/9，默认免费、已拥有可选，未拥有变暗禁用，选中金边，返回/开始。图片外围的“布局预览”和示例流程为说明，不放进产品。状态和金额使用真实数据，不用预览的1200/已拥有示例。

头像原始文件在Backup/Artifacts/CharacterShopPreview-20261031/character-{0,1,3,4,7,9}.png；可导入正式Game/Art新目录并正确Sprite设置/meta。默认全身与候选相同尺寸。不要AI重画，不新增角色属性或效果。

## 接入

- 先读unity-ui、ui-prefab-authoring、code-role、resource-load-check和适用luban-config/TDD技能。现有MainMenuShopUICtrl/UI/Prefab为基础；StartUICtrl改为每次打开选择页，最新用户指令替代直接开局。
- 独立正式Box Prefab经ReferenceCollector+Box生成器；宿主CustomListView直接引用外部Box，不能Instantiate/Destroy内嵌模板。布局/引用序列化，生成View禁止手写。使用正式UICodeSpawner AutoUpdateUI等；编译后确认实际绑定，必要调用同生成器BindGeneratedUiComponent。已有Tools/Unity/SurvivorGoldUiAuthoring.cs及Backup/Artifacts/MainMenuShop-20261031/MakeShop.cs可参考但不可盲复制老设计。
- 购买/选角只提交ID，余额和所有权来自业务一致快照；错误本地化显示，不假装0金币或重置坏档。成功刷新余额、已拥有；失败保持记录。0默认无需花费。
- OnInit按钮/list绑定，OnShow刷新，关闭/销毁/重载及旧异步失效不启动对局、不更新旧View，开局中不可重复点击。当前开始菜单仍可见时才允许回调触发开局，业务重验所有权。重复Show不靠OnShow刷新。语言切换刷新动态文案。
- Controller不保存第二份权威钱包。Box复用完整重置文字/头像/可交互/选中/回调，固定字走LocalizedText且TMP为可读默认中文，动态文本无LocalizedText。Config本地化只源文件改后gen.bat。

## 验证与交付

- 开始保存本阶段基线，勿覆盖他人差异/index，不提交推送，不派生agent。Unity调用先核对正确工程、场景、Console；Boot脏可按测试重载，不丢其它场景。
- 测试先枚举真实非空短testClass/fullmethod，预声明最大数量含参数展开，includePassingTests=true，禁止全套/无效筛选回退。只临时钱包，不能实际扣玩家金币或测试对局写真实存档。覆盖生成引用与Box来源、购买/重复/余额不足/坏档、未拥有选择拒绝、默认免费、关闭旧回调失效、重开已选造型。已有业务测试不无故重复。
- 新UI、头像、Box、配置表的实际加载地址按resource-load-check逐个查真实BeginCollect、标签、下载/预加载或按需链路；检查序列化依赖，不为每张依赖图盲建collector。正常Play可用真实YooAsset loader加载并释放验证；绝不构建、模拟构建或重建清单。源收集/运行/发布边界分开报告。
- 渲染并检查最终正式Prefab两页（可用示例临时钱包/克隆，不写真实金币），包括中英文容纳、宽高比和锁定/已拥有状态；root要查看实际图片。确认候选模型绑定/Animator/武器/碰撞不改动且ApplyWholeBody实际生效。用户豁免独立动态视频审批不是省略必要验证。
- 全部脚本/截图/原始回执/报告位于Backup/Artifacts/CharacterPurchase-20261031/ui/，最终report.md、changes.json、review.patch包括本次所有新.meta及相关生成源证据。告知root未验证项，勿把AssetDatabase读成功当真实运行加载成功。
