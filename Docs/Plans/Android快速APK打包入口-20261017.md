# Android 复用本次 AOT 和资源的快速 APK 入口

日期：2026-10-17。

## 背景

2026-10-17 的 Android 首包生成了 `Client/Build/Android/20261017_170250/FirepowerLoop-0.0.3.apk`，在 `PublishAndroidApk` 阶段因 `0.0.3 / versionCode=4` 已发布且字节不同而失败，总耗时 1403.66 秒。资源已经同步为 `20261017170250`，新 APK 保留，但原流程尚未保存这轮宿主状态。

## 新入口

`Tools/打包/Android/快速打包/复用全部打包`

复用已准备好的同轮 Game DLL、AOT 补充元数据、桥接生成物、完整远端资源以及首包内置清单。跳过预备 Player、HybridCLR 生成和 YooAsset 资源重建，只执行一次最终 APK 构建；Unity 本身仍可能编译、裁剪或运行 IL2CPP，不能承诺固定耗时。

成功后同步指定版本的现成资源（只复制和校验）、发布新 APK，并保存 Android 宿主状态。资源版本固定为复用快照中的版本，不扫描其他较新资源包。目标资源服不存在时沿用现有发布事务创建资源服包，不自动启动资源服；结束时提供原有启动选择。

## 复用门禁

- 新的完整首包在编译前登记手写源码、编译配置和 Player 设置指纹，资源完成后确认未变化，再保存复用快照。生成物在生成完成后单独记录。
- 快照涵盖运行时源码、程序集与响应文件、插件及元数据、包清单、HybridCLR 生成物、Game DLL、AOT、内置清单、StreamingAssets 与现成资源包。Player 设置只有应用版本名和 Android 版本码允许变化。
- AOT 基线从资源实际携带的 `.dll.bytes` 保存为独立 DLL，位于 `Client/Library/FirepowerLoopBuild/AndroidQuickAot/<资源版本>`，不依赖会被 Player 构建清空的现场裁剪输出目录。
- 构建前及发布前重新校验，输入变化时拒绝快速打包，提示完整首包。
- 保留 PC／Android 构建入口互斥及平台重载恢复逻辑。

没有快照的旧流程保留 APK，使用最近成功的本项目 Android 最终 Player 报告恢复：核对 APK 内置清单的实际字节；源码不得晚于对应 Android DLL 的生成时间，编译配置及插件不得晚于 Game DLL；生成物不得晚于最终 Player 开始，Player 设置不得晚于该构建结束。恢复依赖现有 DLL、报告和文件时间，不适用于人为保留或回写时间戳的历史资料。任何检查失败都拒绝自动恢复。

## 版本防重

全部 Android 最终 APK 构建共同使用版本分配器，分别取以下来源的最大版本名和最大版本码，再递增：

1. 当前 PlayerSettings。
2. Android 发布包中已有 APK 文件名和更新描述。
3. `Client/Library/FirepowerLoopBuild/AndroidVersionReservation.json` 的本地版本预留记录。

版本身份在构建前原子持久化，失败后仍占用，避免设置回退或重试引发同版本不同内容冲突。预留文件使用独占锁；历史身份无效、记录损坏或版本达到上限时直接失败。原有已发布 APK 不允许覆盖的保护继续保留。

在当前已知设置与发布记录为 `0.0.3 / 4`、且没有更高记录时，下次会分配 `0.0.4 / 5`，实际身份以执行时检查结果为准。

## 失败与恢复

最终 APK 未通过验证时仅清理本次已建立的输出目录；已经验证的 APK 在后续校验或发布失败时保留。不可变 AOT 与复用快照保留，便于再次快速重试。快速构建成功后保存的 AOT 状态指向独立副本。

资源发布与 APK 发布仍是先后执行的两个步骤，失败时可能只完成资源同步；错误信息保留 APK 路径，不将其报告为整轮发布成功。

## 验证边界

仅进行编辑器编译和明确限定的 EditMode 测试，不执行 Player Build、资源构建或真实发布，不提交推送。测试证据保存于本机忽略目录 `Client/Logs/android-quick-*.json`。

定向范围：`AndroidQuickBuildTests`（最多 18 项）、`AndroidHostBuildTests`（最多 17 项）、`AndroidApkPublishTests`（最多 8 项），以及资源发布事务的指定单方法回归。全部使用 `includePassingTests=true`；不得使用完整套件或无效过滤回退。

真实 APK 构建耗时、包内版本、手机覆盖安装及完整发布链路仍待用户允许打包后验收。

2026-10-17 本次验证结果：快速策略 18/18、Android 构建规则 17/17、APK 发布 8/8、资源发布与替换事务各 1/1，合计 45 项定向测试通过。最后的快速策略回归在输入过滤修正后重新运行通过。

实际保留材料的校验返回 `REUSE_READY 20261017170250`，已建立同版本复用快照及 13 份独立 AOT 元数据副本。证据：`Client/Logs/android-quick-preflight-final.json`、`Client/Logs/android-quick-material-validation.txt`。仅校验与保存本地复用材料，没有创建新 APK、预留应用版本或执行真实资源发布；项目应用版本保持 `0.0.3 / 4`。

## 用户授权后的真实快速打包

2026-10-17 17:56，用户明确要求执行快速打包后，单次运行本入口成功，总耗时 88.15 秒，其中最终 APK 构建 75.94 秒，复用原资源版本 `20261017170250`，没有重跑预备 Player、HybridCLR 生成或 YooAsset 资源构建。

扫描发布目录发现历史最高 APK 为 `0.1.23 / 13`，因此实际分配 `0.1.24 / 14`，而不是只依据当前更新描述推测的 `0.0.4 / 5`。产物为 `Client/Build/Android/20261017_175606/FirepowerLoop-0.1.24.apk`，大小 66,753,044 字节；本地资源服发布目录的 APK 与更新描述已同步，Android 宿主状态已保存到新的独立 AOT 基线。

使用 Android SDK aapt 核对包名 `com.firepowerloop.game`、包内版本 `0.1.24` 和版本码 `14`；源 APK、发布 APK 与更新描述的大小及 SHA256 一致，哈希为 `c3955b98433043db522582b334ae9edcce7531d5ef923d2e1245379ff5f68db8`。真实手机安装、启动及覆盖更新仍待设备验收；是否启动资源服由成功后的选择窗口决定。

## 打包后整理与提交前复验

2026-10-17 19:48，用户已完成打包并明确要求检查、提交。再次限定EditMode验证：AndroidQuickBuildTests最多18项，实际18/18；AndroidHostBuildTests最多17项，实际17/17；AndroidApkPublishTests最多8项，实际8/8，均includePassingTests=true，共43项通过。没有重跑Player Build、资源构建或发布。[本次持久证据](../../Backup/Artifacts/AndroidQuickBuild-20261017)。静态独立审查未发现阻止提交的问题。

本次保留0.1.24/14设置和3份构建DLL：Game与该轮Android热更输出一致，Newtonsoft.Json、mscorlib与该轮独立AOT基线一致，SHA256见证据。再次核对已存在APK大小66753044、SHA256与上述真实快速打包记录及发布描述相符；未将APK或忽略目录纳入Git，也未声称设备安装通过。

TimeScale恢复1；字体字符表未变、仅图集字节发生自动变化，恢复已提交版本；两份DLL元数据GUID未变、仅空Importer被省略，恢复基线。上述原件备份于忽略目录Client/Temp/PostBuildCleanupBackup-20261017。保留项目真实版本递增，排除无关行尾空格，未改APK、发布文件或本地复用快照。以后复用前仍须通过内容指纹门禁，不保证整理后可绕过校验。
