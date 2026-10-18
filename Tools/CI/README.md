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

取消与超时不冒充可安全回滚Unity内部操作。当前入口没有实现长调用协作取消；模板未配置强制杀进程或自动重试，取消后的工作区必须人工核对生成文件、DLL、StreamingAssets和版本台账后再复用。未来需要在允许真实构建的独立节点验收授权、失败恢复及取消行为。
