# FirepowerLoop 构建节点适配

本目录是未部署的手动CI工具。已做请求、文件事务和校验模式验收；未进行真实首包、Host构建、服务安装或发布。项目当前禁止Player Build，不能用本文或模板启动真实构建。

## 现有入口

- `Game.Editor.CiBuildEntry.Execute`：Unity批处理入口，参数`-firepowerRequest <绝对JSON> -firepowerReport <绝对JSON>`。
- `Invoke-FirepowerBuild.ps1`：默认只生成请求文件；加`-Execute`才启动独立Unity，加`-Build`才将`ValidateOnly`设为false。
- `Jenkinsfile`：单Windows节点、禁止并发、手动四路由，`VALIDATE_ONLY=true`、`PUBLISH=false`；保存文件不会安装Jenkins或创建Job。

参数检查模式只验证项目／固定Commit／请求／Host归档，不代表Unity授权、Android工具链、Inno Setup或真实构建已验证。实际构建前需要在专用节点单独配置这些条件。

## 节点与请求

独立checkout拥有自己的Client/Library、Temp和状态，不使用开发者打开的Client，不共享另一项目配置或发布目录。Unity版本固定为`2022.3.55f1c1`。节点环境变量`FIREPOWER_UNITY_EXE`指向该版本Unity.exe，`FIREPOWER_ISCC`指向ISCC.exe；CI不读取或写入个人EditorPrefs选择编译器。

请求示例（只校验，不构建；路径及SHA必须替换为节点真实值）：

```json
{
  "SchemaVersion": 1,
  "ProjectId": "FirepowerLoop",
  "RequestId": "manual-001",
  "ProjectPath": "E:/JenkinsWorkspaces/FirepowerLoop/Client",
  "Platform": "PC",
  "BuildType": "FirstPackage",
  "ExpectedCommit": "1111111111111111111111111111111111111111",
  "ValidateOnly": true,
  "Publish": false,
  "InstallerCompiler": "C:/Tools/Inno Setup 6/ISCC.exe"
}
```

Platform只允许PC、Android；BuildType只允许FirstPackage、HostUpdate。HostUpdate还必须有`BaselineDirectory`，不能填Library状态目录。Publish=true还必须有`PublishDirectory`，目标为完整的新资源服包目录，不能覆盖既有包。未使用的字符串字段应省略，不能填null；未知字段或拼写错误直接失败。

`ExpectedCommit`是完整40位SHA；真实执行脚本拒绝脏checkout、Unity占用和缺失工具，不reset、不自动清理工作区、不杀其他进程。`-buildTarget Win64/Android`由脚本传入，CI不安排跨域切换后续跑。Jenkins模板不配置触发器或凭据；需由管理员在独立节点创建手动Job后单独验收。

## 归档与发布

每次构建使用`Release/Ci/<RequestId>-<GUID>/`保存独立状态、固定资源目录、安装器及首包基线，`artifact.json`记录本次身份和逐文件SHA256，所有路径相对本清单，整目录下载搬迁后仍可校验当前副本。请求／报告／Unity日志在`Release/CiRequests/<id>/`。模板归档两者，发布失败仍保留已封存产物；构建未走到清单交付时报告会失败，不把目录存在当成功。

首包基线包括AOT、DLL、YooAsset包、Player和HybridCLR生成文件，携带Unity／Commit／Boot指纹。Host更新恢复至本次新目录并重定位状态，校验同平台、同Unity、同Boot及所有文件；不依赖旧Library，不因失败改跑首包。PC和Android记录隔离。

发布默认关闭。显式发布只消费本次清单，在目标同级暂存完整资源服包，装配同一安装器／APK更新信息后改名公开；不扫描最新源资源、不覆盖旧目标、不启动服务、不部署到线上。文件复制／安装包处理失败则不公开目标，已构建产物继续留在归档目录。

## 取消与显式恢复（2026-10-18接入）

每份节点请求自动生成自己的`CancellationFile`，调用脚本会输出路径。运行`Request-FirepowerCancel.ps1 -RequestFile <绝对request.json>`只写入同目录`cancel.request`，不杀进程。参数模式检查不启动Unity；可在临时目录测试信号。

Unity在每个BuildStageTiming阶段前后、构建后／发布前、发布暂存完成且公开目录之前检查取消。长时间的Unity／外部编译调用需先返回，无法保证立即停止。报告`Status=Cancelled`、`ExitCode=2`，并保留已有产物；模板将此退出码标为ABORTED（模板未部署实跑）。发布已经改名公开时不撤回，报告实际Succeeded并置CancellationDeferred=true。

真实构建动作前在`Client/Temp/FirepowerCiRecovery/<guid>/`保存受限输入快照及SHA256；进行中标记为`Client/Temp/FirepowerLoop.CiInProgress.json`。成功删除进行中标记，快照保留于Temp供节点按留存策略清理。失败／中途取消／进程崩溃时保留标记，后续ValidateOnly和真实构建都会拒绝复用。

显式恢复批处理入口为`Game.Editor.CiBuildEntry.Recover`，参数`-firepowerRecoveryRequest <原RequestId> -firepowerReport <源目录外绝对报告路径>`。只能在独立空闲批处理Unity执行；必须仍是原Commit，原活跃CI进程不能恢复。恢复前核对项目、工作区、RequestId、Commit、清单哈希、固定范围及逐文件SHA256；成功恢复限定DLL／HybridCLR生成输入／生成的StreamingAssets／ProjectSettings，并将恢复前现状留在快照的displaced隔离目录，再清除进行中标记。下次Unity启动重新导入恢复文件，本入口不触发构建或发布。恢复失败尽量逆序还原先前现状并保留标记；回退也失败则同时报告两种错误，禁止直接重跑。

不会恢复普通手写脚本、玩家存档、Config权威源或回退已消耗的Windows版本台账／Android版本预留。恢复不是续跑到上次阶段：在同一固定Commit上重新提交任务，Host仍需原独立基线。取消信号需要由操作者为新任务使用新请求目录，旧信号不自动删除。快照包含构建原始输入，应使用节点专属权限及留存策略。

以上已用临时文件和模拟长阶段／发布事务定向验证；未执行真实Player Build、安装器、部署或Jenkins流水线。强制结束的标记及死进程路径已测试，实际工具链中止／授权／进程树退出仍需独立节点验收，不按进程名批量结束进程。
