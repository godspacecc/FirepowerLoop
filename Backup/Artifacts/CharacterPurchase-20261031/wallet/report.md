# 角色购买账本阶段

本阶段仅修改 SurvivorGoldFileStore.cs 与 SurvivorGoldStoreTests.cs；不涉及UI、配置、角色加载和真实玩家钱包。未提交，未操作index。

API：TryPurchase(string productId,long price,out long balance,out string error)，上层必须按Config验证商品及价格；成功和已拥有均true，已拥有不写盘。false错误码wallet.invalid_product、wallet.insufficient、wallet.transaction_failed。TryReadSnapshot(out long balance,out string[] productIds,out string error)一次读取一致快照，失败wallet.read_failed。TryReadOwned提供所有权副本。

V1文件继续严格验证Sequence<=Balance且禁止夹带消费记录；第一次成功购买原子迁移为V2。V2累计Spent与OwnedProducts和Balance同文件写入，水位以Balance+Spent验证并避免加法溢出。累计Spent溢出拒绝事务。重复购买不写盘，失败不替换账本。默认0角色由上层提供，不购买。

验证：枚举SurvivorGoldStoreTests共4例（无参数展开），最大4，EditMode指定短类名，includePassingTests=true；4/4通过，原始回执tests-result.json。新增测试覆盖V1迁移、重复购买、消费后幂等领取/继续领取、余额不足、非法参数、锁文件写入失败与坏V1拒绝。原测试覆盖余额溢出、目录不可写、领取重开与活动局隔离。全部钱包在临时目录。未执行Player Build。

测试先写，但未取得缺实现时的红灯回执，不声明完成红绿测试循环。完整角色购买与选角功能尚待root后续阶段；本报告仅证明存储层范围。review.patch相对baseline副本，仅两文件增量，changes.json列文件。

## P2审查修复

V2改用JObject检查Spent为显式整数、OwnedProducts为显式字符串数组，再执行领域一致性校验；V1保持兼容。新增MissingV2PurchaseFieldsNeverOverwriteLedger覆盖两个缺字段、分别缺字段及错误类型，读取/购买/领取均拒绝且文件不变。重新枚举5项非参数化测试，上限5，指定SurvivorGoldStoreTests，includePassingTests=true，5/5通过。原始回执review-fix-tests.json；review.patch已更新为全部本阶段增量。无新增生产文件，未提交。
