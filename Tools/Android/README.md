# FirepowerLoop Android 固定签名

所有构建设备必须使用同一份 FirepowerLoop 签名私钥及同一个 key alias，才能继续覆盖更新现有安装。
当前沿用 0.1.24、0.1.25 的签名，公开证书 SHA-256 为：

```text
ee30b78a81c36576770e53ebad16e927f662ef84080815aa511b7ee8e37bbfee
```

权威证书白名单在 `Server/ResourceServer/android-signing.json`。不要为通过校验而改成新机器的证书；新证书不能覆盖更新已有安装。

## 其他设备配置

1. 同步本次代码修改。
2. 从已配置的原设备，通过安全渠道复制整个 `.codex/signing/` 目录，放到另一台设备的 **FirepowerLoop 仓库根目录**。这是本项目私钥副本；不需要替换系统的 `.android/debug.keystore`。
3. 保留目录中的 `FirepowerLoop.keystore` 和 `android-signing.local.json`，并在 Unity 菜单执行 `Tools/打包/Android/校验固定签名（不打包）`。
4. 只有校验成功后才使用项目已有打包入口。本次修改及验证不执行 Unity 打包。

`.codex/signing/`、`*.keystore`、`*.jks` 已排除出 Git。私钥和密码不随普通 Git 同步，也不放入资源服或客户端资源。
这份原调试密钥用于现有测试包兼容；此流程不代表商店正式签名配置。

本机配置格式如下；相对密钥路径相对于本配置文件所在目录：

```json
{
  "keystorePath": "FirepowerLoop.keystore",
  "keyAlias": "androiddebugkey",
  "keystorePassword": "由原设备提供",
  "keyPassword": "由原设备提供"
}
```

节点也可设置 `FIREPOWER_ANDROID_SIGNING_CONFIG`，指向同格式配置的绝对路径。没有配置、密码错误、别名错误或证书不匹配都会停止，绝不生成新密钥或回退到本机默认调试签名。
每次 Player 构建只在调用期间临时应用这份配置，结束或异常后恢复原来的 Unity 签名设置。只更新代码和资源的 Host 流程仍不构建 APK。

## 发布前验签

Unity 发布入口和 `Server/ResourceServer/publish_app_update.ps1` 都会先验证 APK 签名完整性与固定证书，复制后再次验证待公开文件。
验签不需要私钥；签名校验失败不会切换 `app-update.json`。

独立资源服包运行服务本身不需要 Unity。手动发布 APK 时需要 Unity 自带 OpenJDK、Android SDK build-tools，并通过 `FIREPOWER_UNITY_EXE` 或 `-UnityExe` 指定工具链位置：

```powershell
.\publish_app_update.ps1 -Apk 'D:\Packages\FirepowerLoop-0.1.26.apk' -VersionName '0.1.26' -VersionCode 16 -UnityExe 'D:\Unity\2022.3.55f1c1\Editor\Unity.exe'
```

只验证已有 APK，不构建、不签名、不发布：

```powershell
.\Server\ResourceServer\validate_android_signature.ps1 -InputFile 'D:\Packages\FirepowerLoop-0.1.26.apk' -UnityExe 'D:\Unity\2022.3.55f1c1\Editor\Unity.exe'
```

既有资源服发布目录不会被本次修改自动更改。再次通过项目资源服发布或更新流程时会同步公开策略和验签脚本；手动维护旧发布包时，应一起更新 `publish_app_update.ps1`、`validate_android_signature.ps1`、`android-signing.json`。

## 已生成的错误签名 APK

其他设备已经生成的 0.1.26 仍需用原密钥重新签名，固定签名配置不会追溯修改它。
重签后 APK 内容哈希和大小可能变化，必须使用对应的新发布描述；同版本已发布文件不允许原位覆盖，应按现有发布规则使用新的版本身份。

## 定向测试夹具

`Client/Assets/Tests/EditMode/Flow/AndroidSignedFixture.apk.bytes` 是只含项目 AndroidManifest 的合成签名 ZIP，仅供签名/文件发布测试，不能作为游戏安装包使用。
它使用一次性测试证书（指纹 `cda3f92a5d2ede27605d93eae6345cd62f9dfe2460ed51a171b723a71614813b`），不包含生产私钥，生产证书策略应拒绝它。
