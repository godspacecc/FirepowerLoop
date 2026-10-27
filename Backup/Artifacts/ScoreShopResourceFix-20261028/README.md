# 积分商店资源地址修复

根因：正式 ArcadeScoreShop.prefab 已存在，控制器路径正确，但 DefaultPackage 的逐文件采集清单漏登记该窗口，导致 YooAsset 报运行时地址无效。

修复：补充正式窗口的 MainAsset 采集项，沿用 Game 相对路径无扩展名地址规则及 arcade 标签。商品卡片由窗口 CustomListView 的序列化引用作为依赖带入，不改窗口、卡片或其 meta。

验证范围：EditMode / ScoreShopUiTests，最多 4 项，includePassingTests=true。新增测试使用真实 BeginCollect 结果核对运行时地址及下载标签，不执行资源打包。

- 修复前：4 项中 3 通过、1 失败；新增测试发现目标地址数量为 0。
- 修复后：4/4 通过；包含地址采集、窗口生成引用、外部商品卡片引用及本地化检查，详见 results.json。
- Unity 项目路径：E:/GitHub/FirepowerLoop/Client，Boot 场景。
- 未运行完整测试套件、资源打包或 Player Build；未进行完整过关试玩，旧已发布资源包需后续按发布流程更新才能包含此修复。
