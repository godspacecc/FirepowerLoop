# Unity 工程资源目录规范

本规范适用于 `Client/Assets` 中新增、导入、生成、移动或改名的资源。已有路径用于识别现有归属，历史目录迁移按另行确认的范围实施。

## 1. Assets 第一层按工程职责划分

| 路径（相对 `Client/Assets`） | 职责与下一级规则 |
| --- | --- |
| `Game/` | 正式游戏内容。按下节所列内容大类分组。 |
| `Boot/` | 应用启动代码、启动界面和配套素材。启动场景的固定路径见下节。 |
| `Art/<资源包名>/` | 用于浏览选型的备用美术包。保留包内结构，发布商记入来源清单。 |
| `ThirdParty/<发布商>/<资源包名>/` | 工程内维护的第三方原件。保留原包的命名、结构和许可。UPM 依赖由 `Client/Packages` 管理。 |
| `Editor/` | 编辑器工具及其配套资源。引用正式游戏资源的对照场景和素材归 `Editor/ArtPreviews/`。 |
| `Tests/` | 自动化测试及其专用资源。沿用测试模式和被测功能的目录。 |
| `Settings/` | 项目级渲染、输入等设置资源。沿用对应系统的现有路径。 |
| `Plugins/`、`TextMesh Pro/`、`Samples/`、`HybridCLRGenerate/` | 分别由插件、TextMesh Pro、样例导入和 HybridCLR 管线维护，沿用各自约定。 |
| `StreamingAssets/` | 需要由 Unity 原样随应用分发的文件。按使用它的系统或包划分。 |

`Assets` 根部的单个设置文件沿用所属工具约定，例如 `AssetBundleCollectorSetting.asset`。预览成品、报告和日志按批次放在仓库根的 `Backup/Artifacts/`。备用素材与正式游戏资源使用独立副本和 GUID，依赖关系按 [美术资源库使用约定](美术资源库使用约定.md) 处理。

## 2. Game 第一层按内容大类划分

| 目录 | 职责与下一级规则 |
| --- | --- |
| `Art/` | 正式美术素材。先按素材用途分组，再按所属功能或对象分组，详见下一节。 |
| `Prefabs/` | 供界面、玩法或场景直接使用的已组装对象。先按职责分入 `UI`、`Characters`、`Combat`、`Presentation`、`SceneObjects` 或 `WorldHud`，再按使用模块分组。纯视觉零件如枪口火光仍归 `Art`。 |
| `Audio/` | 游戏音频，先分 `Music`、`Sound`，再按使用模块分组。 |
| `Scenes/` | 正式游戏场景，按所属功能或玩法分组。启动场景 `Boot.unity` 使用固定路径。 |
| `Fonts/` | 运行时字体和字体资产，按字库或文字覆盖范围分组。 |
| `Shaders/`、`Materials/` | 游戏共用的着色器和跨素材复用的材质，按渲染用途分组。某个素材专用的材质随其美术组归档。 |
| `Config/` | 客户端消费的配置和本地化资源，按数据域分组。生成物遵循生成器输出路径，源数据在仓库根 `Config` 维护。 |
| `Scripts/` | 游戏代码，沿用现有架构职责和业务模块。 |
| `Dlls/`、`Plugins/` | 游戏级程序集和运行时集成，沿用生成器或集成约定。 |

启动场景固定在 `Client/Assets/Game/Scenes/Boot.unity`。构建设置、构建脚本和启动测试使用 Unity 路径 `Assets/Game/Scenes/Boot.unity`，YooAsset 启动资源地址为 `Scenes/Boot`。如需调整位置，先核对这些入口，再同步更新路径、收集规则和相关测试。

## 3. Game/Art 先按素材用途，再按所属对象

新增资源按下表判断第一层。子目录表示功能、对象或资源组，选用现有名称。确需进一步划分时，才在资源组内用 `Models`、`Textures`、`Materials` 等名称组织依赖。

| 第一层 | 放什么 | 路径示例（相对 `Game/Art`） |
| --- | --- | --- |
| `Models` | 角色、敌人、武器、道具和环境的三维造型，及归该组专用的贴图和材质 | `Models/Characters/`、`Models/Weapons/`、`Models/Environment/` |
| `Animations` | 动作、Avatar 遮罩和作为美术资源维护的动画控制资产 | `Animations/Player/` |
| `Effects` | 粒子、拖尾、命中反馈等纯视觉效果，及其专用材质、纹理和预制件 | `Effects/Player/`、`Effects/Survivor/` |
| `UI` | 图标、头像、背景和界面装饰；先分 `Icons`、`Backgrounds`、`Decorations` 等固定用途，再按所展示的对象或使用功能分组 | `UI/Icons/Characters/`、`UI/Icons/Items/`、`UI/Backgrounds/Home/` |

用途以实际使用方式判断。同一个 PNG 用来显示角色卡片时归角色图标，用作模型表面时随模型归档。同一张角色图标供商店和选角使用，两个界面引用同一份 `UI/Icons/Characters/` 资源。道具图标归 `UI/Icons/Items/`，出售行为由商店系统管理。表中路径是归档规则，现有资源位置以工程实况为准。

## 4. 命名和新增流程

1. **确定归属。** 开工前明确资源用途、使用方和目标路径。先确定是项目自有、第三方原件、备用包、工具生成内容还是编辑器或测试资源，再依目录职责落位。检查正式资源中是否已存在可复用的同一资源。
2. **沿用词汇。** 自建目录用英文 PascalCase，职能、模块或对象类别用项目已有名称。不同目录层分别表达一种维度。供应商目录、Unity 固定路径和生成器输出沿用原约定。资源文件用能识别对象、用途和变体的名称；替换既有资源时保持既定命名和加载契约。
3. **遇到歧义先定规则。** 已有分类下，符合规范的普通子目录可以直接使用。新增一级分类、引入规范未定义的分类、改变目录职责或出现两个同样合理的归属时，先列出候选路径与使用方，经用户确认并记录规则后再落盘。
4. **核对实际结果。** 确认最终路径、`.meta`、引用方及同一资源是否重复。既有资源获准迁移后通过 Unity 保留 GUID，并核对序列化引用、硬编码路径和制作工具。动态加载按 [resource-load-check](../.agents/skills/resource-load-check/SKILL.md) 核对实际请求地址、真实 YooAsset 收集结果、标签和预加载链路。
