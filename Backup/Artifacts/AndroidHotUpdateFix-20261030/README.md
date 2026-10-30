# Android热更新整理

- 入口改为构建热更新（代码+资源）。成功APK新增内容指纹，AOT记录指向冻结名单；基线从成功APK对应的不可变记录加载，不使用最新准备索引。旧记录缺指纹时拒绝更新。
- 编译前及发布前验证APK、冻结资料、Unity版本、设置、插件/宿主/桥接；编译后核对Game外部引用及原生需求。冻结名单用于复制AOT；长流程输入变化拒绝发布。
- 只发布明确目录/版本，构建后及发布后核对完整远程包。保留CI只归档、热更新不改APK基线及启动资源服行为。
- 四份修改C#文件独立C#9语法解析通过（见syntax-check.json），源码顺序/绑定/不构建APK断言通过，任务git diff --check通过。未Unity编译、测试或实际发布/设备运行，不声明运行验收通过。
- 既有Game.Tests.AndroidHostBuildTests.HostUpdateReusesAndroidAotAndDoesNotBuildPlayer已同步但未运行。恢复验证须仅指定该完整testMethod，上限1、includePassingTests=true，禁止全套及无效过滤回退；现有合同检查不能替代缺基线、APK篡改、宿主不兼容、合法热更和设备运行验收。
- 使用说明第九项补齐输入输出位置、记录用途与失败补做。
