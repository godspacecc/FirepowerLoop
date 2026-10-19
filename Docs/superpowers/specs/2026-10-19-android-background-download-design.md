# Android 后台下载正式设计

日期：2026-10-19。状态：用户已同意 APK 与资源更新共用原生下载模块的方向；本文待审阅，尚未实施。

## 目标与验收边界

玩家开始更新后，切换到其他应用或锁屏，APK 和本次启动必需的资源仍能通过 Android 原生任务下载。返回游戏时恢复真实进度，文件完整校验后才允许安装或进入游戏。系统中断后可恢复已完成文件和有效的部分文件。

本次范围为 Android Host 模式的 Boot 更新：APK 更新，以及 DefaultPackage 中现有 startup、arcade 标签选择出的资源。保留 APK 可选更新、缓存与内置资源回退、安装确认及服务器切换边界。Windows、编辑器模拟和离线模式沿用现有流程。

“后台完成”指网络文件已传输完成；YooAsset 导入、资源就绪校验、Game.dll 加载及进入 Main 在 Unity 恢复运行后执行。通知不会把“文件下载完成”表述成“游戏更新已生效”。后台不打开安装器、不自动进入游戏。

用户强制停止、系统任务管理器停止、厂商限制及设备资源压力可能终止任务，不承诺永久保活。普通切后台不取消；下一次可见启动能够核对记录并恢复。系统明确停止的任务不自行循环重启。

不涉及 AnotherDay 数据、玩法、局外成长、跨项目同步或通用框架。当前工作区已有其他改动，实施时保留并逐项限定本任务差异；本阶段不提交、不运行 Unity 打包或 Player Build。

## 现有实现证据

2026-10-19 已用 Unity MCP 查询场景、Console 和项目路径：连接 E:/GitHub/FirepowerLoop/Client，Unity 2022.3.55f1c1，Android 目标，Boot 场景已保存。Console 中读取到既有测试日志，不据此声称本功能通过任何测试。

| 当前源文件 | 现有职责及限制 |
| --- | --- |
| `Client/Assets/Boot/Res/ResourceBoot.cs` | Host 启动串接 APK 检查和资源初始化；CreateStartupResourceDownloader 使用 startup、arcade 标签、并发 10、失败重试 3；RequestServerChange 取消旧请求。 |
| `Client/Assets/Boot/Runtime/ApkUpdateFlow.cs` | APK 下载使用 UnityWebRequest 与 Update 轮询；临时文件在 finally 删除；校验、玩家选择和安装交接已存在。 |
| `Client/Assets/Boot/Runtime/AndroidApkInstaller.cs` | 稳定 Boot 中的原生安装桥接；安装权限与确认交给系统。 |
| `Client/Assets/Boot/Runtime/UpdatePackageCache.cs` | 已验证 APK 的回执及已安装版本清理；不是在途任务数据库。 |
| `Client/Assets/Boot/UI/HotUpdate/HotUpdateView.cs`、`Client/Assets/Game/Scenes/Boot.unity` | 现有启动提示、进度和按钮；本设计复用 Boot 覆盖层，不引入游戏 UI 框架。 |
| `Client/Assets/Plugins/Android/FirepowerLoopUpdate.androidlib/` | 原生安装器及文件 Provider；没有后台下载任务。 |
| `Client/Packages/manifest.json` | YooAsset 固定为 2.3.17；当前为注册表依赖。 |
| `Server/ResourceServer/resource_server.ps1` | 文件 GET/HEAD 输出完整 200 响应；当前未处理 Range/If-Range。 |

已检查本机 YooAsset 2.3.17 包源码：DownloaderOperation 持有私有 BundleInfo 列表，只公开总量和执行回调；没有导出整份待下载文件清单的公开接口。ResourcePackage.CreateResourceImporter 支持 ImportFileInfo 指定本地路径与 BundleGUID，仍由文件系统执行导入及校验。

因此仅把 UnityWebRequest 放进线程或增加 Android 服务，不能解决完整队列在 Unity 暂停后继续调度的问题。

## 方案比较与选择

| 方案 | 适用性 | 结论 |
| --- | --- | --- |
| APK 使用 DownloadManager，资源另做原生队列 | 单个 APK 接入较小，但任务、取消、恢复及进度要维护两套后端。 | 保留为单做 APK 时的备选。 |
| APK 与资源共用持久化原生队列，原生执行 HTTP，Unity 只负责启动与结果消费 | 统一恢复、通知、网络策略；可在 Unity 暂停时执行完整批次。需要明确 YooAsset 文件清单及导入边界。 | 采用。 |
| 保持 Unity 前台下载，补暂停及重试 | 改动最小，但切后台期间不持续下载。 | 不满足目标。 |

采用 Java 原生下载核心，Android 14/API 34 及以上用用户发起的数据传输任务（UIDT），较低版本用下载期间的前台服务。两者共用任务仓库和传输引擎，同一批次只允许一个执行宿主。UIDT 调度失败显示原因并允许前台重试，不在新系统中静默退到另一套保活机制。

## 玩家交互

1. APK 保留“下载更新／稍后更新”。只有点击下载才创建任务；取消明确取消本次 APK 下载并删除其未提交临时文件，已验证安装包继续复用。
2. 必需资源缺失时，Boot 先显示文件数和大小，并提供“开始下载”。这次明确点击用于发起后台任务；新增交互是本设计的一部分。无缺失文件时不增加点击。
3. 开始后游戏内显示总大小、进度、速度和剩余时间。通知显示“正在下载游戏更新”、批次进度，以及“取消下载”。通知点击返回应用正常启动入口，不直接安装或跳过 Boot。
4. 资源取消后停留在启动更新状态，允许重新开始，不能进入缺资源的游戏。APK 取消后继续保留稍后更新选项。网络恢复属于同一批任务，不额外重复弹确认。
5. 原生文件全部完成时通知显示“下载完成，返回游戏继续校验”。回到 Boot 后显示“正在校验资源”或“正在校验安装包”；资源就绪后继续启动，APK 校验后仍需点击“安装更新”。
6. 网络断开时保留进度并显示“等待网络”；首版沿用玩家点击下载时可用网络，不新增 Wi-Fi 专用设置。切换到计费网络可继续，开始提示须说明这一点。
7. Android 13 及以上在首次用户开始后台下载时按系统机制请求通知权限；拒绝不等于下载失败。界面说明进度通知可能不可见，并仍提供游戏内取消。权限处理遵守 UIDT/前台服务的实际系统规则，不以反复弹权限阻断启动。

Boot View 只展示快照和提交意图，不保存任务权威状态。复用已存在按钮和覆盖层；需要新增静态控件时在 Boot 场景中序列化，不运行时拼装第二套界面。正文沿用稳定 Boot 当前文案机制，不依赖尚未下载的游戏本地化资源。

## 职责与模块

拟新增路径均为设计，不表示当前已经存在。

| 所有者 | 拟承载位置 | 职责 |
| --- | --- | --- |
| Boot 下载适配器 | `Client/Assets/Boot/Runtime/Downloads/` | 统一类型、JNI 桥接、提交批次与读取快照；不保存独立的权威任务副本。 |
| 原生任务核心 | `Client/Assets/Plugins/Android/FirepowerLoopDownload.androidlib/` | SQLite 任务记录、HTTP 文件传输、恢复、通知、取消、受控临时目录；不依赖 UnityPlayer 主循环。 |
| UIDT JobService／低版本 Service | 同一 androidlib | 按系统版本获得执行窗口并驱动同一个核心；结束时释放执行窗口、通知及唤醒资源。 |
| APK 事务 | 既有 ApkUpdateFlow | 候选检查、用户选择、原生任务结果消费、完整校验、回执与安装交接。 |
| 资源事务 | 既有 ResourceBoot + Downloads 内窄适配器 | 使用当前 YooAsset 下载器确定缺失列表；下载后导入、复查启动就绪、发布启动来源。 |
| 资源缓存 | YooAsset | 唯一拥有正式缓存的校验、写入和索引；原生模块不写其内部缓存结构。 |

下载发生在 Game.dll 加载之前，不把启动任务注册成 QFramework 的游戏 Model/System，也不让游戏 View 接管。Game 内版本提示继续提交现有重启意图；进入 Boot 后才真正下载。

JNI 提供按批次提交、查询、取消、消费确认四个窄操作。快照含 schemaVersion、batchId、generation、状态、文件数、字节数及稳定错误码。C# 只在前台轮询，暂停后无需轮询；原生服务不靠 UnitySendMessage 收到回调才能推进。重复提交同一任务身份返回既有任务。

## YooAsset 清单与缓存交接

采用项目内嵌的固定 2.3.17 包：后续实施时将依赖固定在 `Client/Packages/com.tuyoogame.yooasset/`，保留原包许可证、源码和 .meta，并记录与上游的有限差异。禁止修改 Library/PackageCache 中的缓存包作为实现，也不升级 YooAsset 版本。

唯一必要的上游扩展是 ResourceDownloaderOperation 的只读待下载快照接口：在 BeginDownload 前，复制其原有列表中的 BundleGUID、远端文件名、FileHash、FileCRC 和 FileSize；不返回内部对象、缓存路径或可变集合。快照选择完全继承原有 startup/arcade 标签、依赖、内置文件与缓存判断，不重写标签依赖算法。增加包维护成本是本方案的明确代价。

资源下载顺序：

1. ResourceBoot 按原有流程读取版本并激活清单；小型版本及清单查询仍在前台完成。
2. 创建原有标签下载器，仅用于取得缺失快照，不调用其 BeginDownload，不与原生队列同时下载。
3. 将本次完整快照一次性持久化到原生仓库后启动执行；并发上限不超过现有 10，文件失败重试保持 3。队列不能只包含当前首批并发文件。
4. 原生模块将文件写入自己的暂存目录，完成长度与 CRC 校验后标记 Transferred。CRC 用于传输错误检测，不将其描述为数字签名或来源认证。
5. Unity 恢复运行后，核对当前服务器、包版本、清单身份和 generation；用 ImportFileInfo（本地路径与 BundleGUID）创建 YooAsset 导入器并执行。导入仍在 Unity 主线程操作生命周期内推进。
6. 重新创建相同标签下载器，确认缺失数为零，并执行现有 ValidateStartupResources。导入器成功本身不是启动就绪依据，未知或被跳过的文件不能算完成。
7. 仅在上述步骤通过后消费原生结果并删除暂存文件。原有成功版本及启动来源记录仍由 ResourceBoot 提交。进程在导入或确认中断，下次重新核对缓存并幂等重试。

使用外部暂存再导入会增加磁盘峰值。开始前按剩余暂存量 + 最大导入复制量 + 元数据空间检查容量；最坏按两份缺失资源估算。前台可逐文件导入并消费以回收空间，后台仅下载。空间不足明确失败，不通过删除有效正式缓存或存档腾空间。

APK 使用同一下载核心，但沿用 APK SHA-256、包名及版本检查和 AndroidApkInstaller 的包身份/签名验证。原生传输完成后仍由 ApkUpdateFlow 完整校验，通过后原子提交到现有 UpdateDirectory 并写回执；下载完成不自动发起安装。

## 任务身份、持久化与取消

每批任务绑定 app package、规范化服务器、用途、版本、清单身份和 generation。文件身份绑定期望内容摘要与长度；APK 使用 SHA-256，资源使用其清单 FileHash/CRC。数据库只持有受控目录相对文件名，不接受任意目标绝对路径。

状态为 Queued → Running → Transferred → Consumed；WaitingNetwork、Suspended 为可恢复状态，Failed、Cancelled 为终态。Transferred 只表示下载核心已检查传输文件，APK 安装就绪和资源就绪由上层校验决定。读取快照本身不启动任务。

SQLite 事务提交状态及取消代次，字节进度可节流写入；恢复时以实际临时文件长度和响应校验器为准，记录进度不作为完整性证据。每批执行具有数据库租约/执行代次和文件排他写入锁；两个宿主不得写同一临时文件。进程重建时处理过期执行记录，活跃写入者持锁期间不得夺取写权。

取消先使 generation 失效，再停止网络并等待写入者退出，最后清理该批次未消费暂存文件。切换服务器必须先收到旧批次停止确认，才开始新批次；旧结果及迟到回调均不能提交。APK 玩家主动取消沿用删除部分文件语义；非主动的网络中断和系统暂停保留有效部分文件。

Boot 场景隐藏、应用暂停或 C# 等待者释放只解除观察，不等于取消原生任务；明确取消、服务器切换才停止。正常退出不依赖 OnDestroy 做关键持久化，也不承诺主动退出后一直下载。重启后先读取任务，再检查当前服务器候选；身份一致才恢复或消费。

升级后回执清理不得删除在途任务持有的文件。首次迁移不把旧版未验证 .part 当成可续传文件；已有验证通过 APK 和 YooAsset 缓存继续复用。无人持有的终态临时文件启动时回收，失败记录保留至重试或玩家取消，不以无限后台重试掩盖错误。

## HTTP 续传与资源服

后台执行与断点续传分别实现。服务端不支持续传时，保留已完成文件，仅重新下载中断的单个文件。

- 客户端传输使用明确的读取/连接超时和有限失败重试；断网转 WaitingNetwork，系统停止转 Suspended，不占用工作线程空转。恢复不使用 Unity Time.realtimeSinceStartup 计算超时。
- 有可信强 ETag 或适当 Last-Modified 时使用 Range 与 If-Range。206 必须校验 Content-Range 起点、总长及内容身份；200 表示服务器忽略范围或内容改变，先截断旧文件再重下，禁止追加。
- 416 仅在已有长度达到期望长度且全文件校验通过后视为已完成，否则清理部分文件并有限重试。错误范围、长度超限、损坏校验器不得生成 Transferred。
- 无稳定响应校验器的中断文件不盲目拼接；可以从零重下。所有完整文件必须通过期望长度及摘要/CRC 验证。
- `resource_server.ps1` 的 APK 和资源文件 GET 增加单段字节 Range、206/416、Accept-Ranges、Content-Range、强 ETag 和 If-Range；HEAD 保持全文件描述。多段或语法无效 Range 忽略范围并按完整 200 返回，合法但不可满足范围返回 416。
- 强 ETag 由实际内容摘要生成并按稳定文件身份缓存；传输期间固定文件句柄与长度，发布替换后重新计算，避免同尺寸文件被当成同内容。保留现有根目录约束与 GET/HEAD 限制。
- 不接受超出任务服务器边界的重定向；APK 保持现有不重定向策略。请求同一内容失败不得自动切换到未经选择的资源服。

## Android 生命周期与发布要求

UIDT 仅在玩家可见并点击下载后调度，提交完整队列及估计字节数，并在 JobService 挂载通知。网络条件恢复由系统重新调度时从持久化记录继续；系统拒绝或暂停时返回可恢复状态，不能承诺立即执行。

低版本服务在可见 Activity 中启动，API 26 起按前台服务启动时限及时发布通知；API 22–25 使用相应旧接口。网络 IO 在工作线程进行。断网后通知显示等待网络，使用系统网络回调等待最多 5 分钟，不占用工作线程或唤醒锁空转；窗口内恢复自动继续，超时转 Suspended 并停止服务，下次玩家打开应用时恢复。任务结束或取消后停止服务，不从后台重新拉起前台服务，不建立常驻重启循环。

Manifest 声明所需网络、通知、FOREGROUND_SERVICE、RUN_USER_INITIATED_JOBS 等权限，UIDT 服务使用 BIND_JOB_SERVICE 且不导出；所有取消 PendingIntent 固定为本应用、不可变并绑定批次。执行版本判断避免旧系统加载新 API。若使用唤醒锁，只在活动传输阶段持有有时限锁并在全部出口释放，不用锁跨越等待网络。

编译 SDK 至少能编译 API 34，最低 SDK 保持当前 22。target SDK 不为绕过限制而下调；实施计划需要核对 Unity 2022.3 当前 Android 工具链的实际兼容性。新系统使用 UIDT，未来若新增 Android 15+ dataSync 前台服务分支，必须实现对应超时退出，不能绕过时长限制。

原生代码、Manifest、Boot 程序集以及 YooAsset 包接入均属于宿主变化，首次启用必须使用新 APK。仅推送 Game.dll 或资源包不能让旧安装获得本能力。实现与代码检查可先完成，项目规定禁止本代理触发 Unity 打包或 Player Build；新 APK 制作及真机安装留给后续用户操作。

## 失败处理与回退

APK 更新仍为可选：元数据失败、下载失败或不兼容不阻止原资源流程。资源下载失败沿用现有 Boot 结构化失败及完整缓存/内置资源回退，不将原生批次状态直接当作资源成功。

原生桥接不可用、协议版本不兼容或调度失败时显示明确原因；玩家可选择本次保持前台使用原有下载链路，开始该链路前先停止原生写入并核对文件。不得悄悄降级后仍显示“可后台下载”。

原生错误至少区分网络等待、HTTP 错误、磁盘不足、文件校验失败、任务被取消、系统调度受限和桥接不兼容。界面显示简短原因，日志保留批次与阶段；日志不写任意鉴权信息。后台不存在自动安装、自动加载热更程序集或修改游戏存档的路径。

## 验证设计

本文仅设计，没有执行下述测试，也不表示已实现或已通过设备验收。

自动验证须覆盖实际行为：完整队列超过并发上限后 Unity 不再轮询仍能下载后续文件；取消与服务器切换不能迟到提交；进程重建重用已完成文件；Range 206/200/416 分支、同长度内容变更、无校验器重下；APK 损坏拒绝安装；YooAsset 导入失败或剩余资源非零不能启动；空间不足、通知拒绝、调度失败与前台回退。

后续新增队列/桥接及资源交接 EditMode 测试，必须先写真实测试，再按实际短 testClass 或完整 testMethod 声明范围、最大数量并设置 includePassingTests=true；新增类存在后才可调用，不得无效过滤后退回全套件。原生核心以独立 Java 测试或连接设备测试验证，Unity EditMode 的模拟桥接不能代替后台执行证据。PowerShell 资源服使用临时根与临时端口验证实际 HTTP 响应，不改当前发布目录、不接管正在使用的资源服务器。

可复用的现有定向回归方法（后续实施时运行，以下每项最大 1）：

- `Game.Tests.ApkUpdateFlowTests.ValidDownloadCommitsVerifiedFile`
- `Game.Tests.ApkUpdateFlowTests.InvalidDownloadDoesNotCommitFile`
- `Game.Tests.ApkUpdateFlowTests.PlayerCancellationRemovesPartialFile`
- `Game.Tests.Flow.UpdatePackageCacheTests.AndroidSameCodeButNewerVersionRemainsUntilNewVersionRuns`
- `Game.Tests.Flow.BootResourceFallbackTests.CompleteStartupAllowsOptionalBundleToRemainAbsent`
- `Game.Tests.Flow.BootResourceFallbackTests.MissingStartupDependencyRejectsBuiltinFallback`

真实设备至少验收：API 33 或更低前台服务分支、API 34+ UIDT 分支；下载超过首批并发数量、切后台及锁屏、网络中断与恢复、系统回收及重新打开、通知取消、强制停止后重新打开、计费网络切换、后台下载完成后回前台导入、APK 下载完成后的手动安装。没有这些证据只能报告源码与定向测试状态。

## 官方依据

资料核对日期为 2026-10-19；Android 平台说明作为约束，不代替当前项目验证。

- [UIDT 用户发起的数据传输任务](https://developer.android.com/develop/background-work/background-tasks/uidt)：API 34 起提供；用户发起、通知、可见状态调度及系统停止后的恢复要求。
- [启动前台服务](https://developer.android.com/develop/background-work/services/fgs/launch)：启动与进入前台的要求。
- [后台启动前台服务限制](https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start)：不能等进入后台再任意启动。
- [通知权限](https://developer.android.com/develop/ui/compose/notifications/notification-permission)：权限拒绝时的系统通知行为。
- [前台服务超时](https://developer.android.com/develop/background-work/services/fgs/timeout)：target Android 15+ 的 dataSync 等类型存在时长与退出约束。
- [DownloadManager](https://developer.android.com/reference/android/app/DownloadManager)：单个 APK 使用系统下载服务的备选依据。
