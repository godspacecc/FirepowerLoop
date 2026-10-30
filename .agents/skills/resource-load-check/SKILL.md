---
name: resource-load-check
description: Use when adding, replacing, renaming, moving, configuring, or diagnosing dynamically loaded Unity resources in FirepowerLoop, including YooAsset addresses, collectors, download tags, and resource loading failures.
---

# 动态资源加载检查

文件存在、Prefab可被AssetDatabase打开，只能证明源资源存在。交付动态资源前，还要证明实际请求的地址进入真实YooAsset收集结果，并通过既有下载、预加载或按需加载流程在使用时可用。

## 适用范围

适用于配置或代码通过地址加载的Prefab、场景、图片、音频及其他资源，也适用于新增独立配置表引入的资源。仅修改不涉及资源寻址的纯数值或逻辑时无需触发。沿用已确认视觉的加载修复不要求重新设计外观；新增视觉仍遵循项目预览规则。

## 核对实际加载链

1. 从本次变更的配置和调用者列出**实际请求的逻辑地址**，追踪资源名如何经`ResourceAddress`及运行时配置适配器变成地址。不能只遍历旧的公共配置表，遗漏新模式的独立表或代码拼出的地址。
2. 核对源文件、`.meta`、嵌套Prefab、Mesh、材质和贴图引用。以现有收集规则为准；仅被已收集Prefab序列化引用的依赖通常不必新增独立主资源收集项，直接按地址加载的资源则需要可寻址主资源。对关键依赖核对可查询的收集/包依赖关系，不能仅凭引用存在认定发布依赖完整；缺少对应包证据时标注验证边界。
3. 读取`Client/Assets/AssetBundleCollectorSetting.asset`和实际地址规则。检查启用的包/组、收集路径、GUID、MainAssetCollector、地址规则、过滤规则及标签。不要把本项目的逐文件收集误当成目录自动收集。
4. 用正式收集器的`BeginCollect`查询结果验证精确地址、对应AssetPath和AssetTags；避免只搜设置文本或用AssetDatabase加载器替身。检查地址冲突，确认同一地址没有对应多个资源。若查询失败或API/反射结构不符，修复查询或报告无法验证，不能降级到替身加载后算作通过。
5. 沿启动下载标签和模式入口检查资源能在请求前到达。标签来自现有Boot及下载流程，不盲目给所有资源加startup或arcade。既有流程需要预加载时，将新资源纳入对应准备事务并核对缓存键；按需加载则检查真实异步加载和失败出口，不强行新建预加载事务。两者均沿既有资源句柄所有权及释放路径管理。

## 项目检查入口

- 地址定义：`Client/Assets/Game/Scripts/Utility/Res/ResourceAddress.cs`。
- 收集设置：`Client/Assets/AssetBundleCollectorSetting.asset`。
- 真实收集检查：`Client/Assets/Tests/EditMode/Flow/GameplayResourceDistributionTests.cs`中的`CollectAssets()`，调用`BeginCollect("DefaultPackage", true, false)`；此处查询收集结果，不打包。
- 已有掉落回归：`Game.Tests.GameplayResourceDistributionTests.ConfiguredDropVisualsAreCollectedForArcadeEntry`，包含公共街机掉落及幸存者独立经验地址。新增其他来源时应扩展对应来源的覆盖，不能假定此测试天然覆盖所有动态资源。
- 地图掉落预加载：`Client/Assets/Game/Scripts/Controller/Loot/LootCtrl.cs`。其他资源按实际调用者查找所属准备事务，不能强行接入掉落流程。
- 资源包校验代码：`Client/Assets/Editor/YooAsset/YooAssetBuildUtility.cs`。先阅读目标方法再决定是否调用；不能为了检查而调用构建、发布或拷贝入口。

可参考已有测试的只读查询方式，按所需资源类型检查结果；不必重复生成一份收集器实现：

```csharp
var setting = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
    "Assets/AssetBundleCollectorSetting.asset");
var result = setting.GetType().GetMethod("BeginCollect").Invoke(
    setting, new object[] { "DefaultPackage", true, false });
// 读取result.CollectAssets，比较实际请求地址与Address、AssetInfo.AssetPath、AssetTags。
```

## 证据与验证边界

| 已有证据 | 可以认定 | 仍不能认定 |
| --- | --- | --- |
| AssetDatabase读取成功 | 源资源存在、序列化引用可检查 | 已收集、已下载、运行时能加载 |
| 真实BeginCollect地址及标签通过 | 源收集规则可产生正确寻址项 | 旧发布包已包含它 |
| 当前运行清单及真实加载器加载成功 | 当前运行环境可加载 | 其他平台或旧安装包可加载 |
| 现有发布包清单检查通过 | 所检查包包含对应寻址项 | 已发布到用户设备、整局效果通过 |

按任务权限选择最小充分检查。Unity打开时先核对MCP工程路径、场景和Console；测试使用真实非空短testClass或完整testMethod，预先声明范围与最大数量，includePassingTests=true。不运行全套或无效过滤回退。报告保存在`Backup/Artifacts/`本批目录。

修复加载失败时，先让针对真实收集结果或实际加载链的回归复现遗漏，再修改最小相关配置。沿用现有测试即可，不为每个新资源复制一套测试。验证发现同类遗漏时核对真实引用和文件后再修复，不把缺失地址批量标成可忽略。

**本项目禁止资源打包和Player Build时不得以验证为由触发。** 不调用模拟构建、重建清单、清Library/缓存、服务器发布或自动刷新旧发布包来制造通过；需要这些动作时按既有授权边界处理。源检查通过后，清楚标注未验证的运行/发布环境。编辑器旧模拟清单可能需要下次正常进入Play才刷新，不能把提示用户重进Play当成已验证加载。

## 常见误判

- “Prefab实拍正常，所以加载已修复”：实拍可能直接通过AssetDatabase取资源，跳过了收集和下载。
- “所有掉落检查通过”：确认地址枚举是否覆盖本次新增的独立模式配置。
- “添加collector就完成”：核对实际生成的地址、下载标签与预加载缓存键是否一致。
- “未收集的Mesh必须单独加collector”：先区分序列化依赖和直接动态加载，避免重复收集及地址冲突。
- “源清单检查通过，安装包也好了”：旧发布包不会因源码变化自动更新。

修改Luban权威源时同时使用[luban-config](../luban-config/SKILL.md)；修改运行时职责或资源事务时同时使用[code-role](../code-role/SKILL.md)。本技能不授权新增美术方案、构建或发布。
