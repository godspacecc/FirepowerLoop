# My Assets 资源用途分析

本报告逐项分析 Unity Package Manager `My Assets` 中的资源。表格按 Asset Store 产品 ID 标识资源，不记录随设备变化的下载或导入状态。已确认不支持项目 URP 渲染管线的资源不收录；兼容性未知的资源保留并标明需验证。“是否适用”面向 **FirepowerLoop 独立街机**的候选评估；资源拥有或旧项目试用记录不代表当前正式采用。“需验证”表示仅凭资产名称或商店简介不能确认实际模型、材质、渲染管线或性能。何家村旧地图已退役，不再用其范围约束规划当前街机关卡。

## 地形与场景制作

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| Free Island Collection（`104753`） | 岛屿 Terrain 与泥土、草、岩石等地表贴图。 | **岛屿地图和地表材质可评估**。 | 可提取地表贴图或参考岛屿地貌；不应直接覆盖已确定高程的地图，URP 效果需验证。 |
| Noise Brush（`28672`） | Terrain 高度或地表的噪声笔刷。 | **地形编辑可评估**。 | 虚构地图可用来塑形；现实参照地图则须保留高程依据和人工修正记录。 |
| Low Poly Tools Bundle（`195642`） | 旧版套装实包包含 Polaris 2021 网格地形、Poseidon 水面、Jupiter 程序化天空与 TextureGraph 节点贴图工具；另有示例场景和低模自然物。 | **风格化地图制作可评估；和家村优先只评估局部水面与贴图**。 | 已核对本地导入内容，详见下表。Polaris 是另一套网格地形工作流，不应直接替换和家村现有四块 Unity Terrain；Poseidon 的部分后处理 Shader 在当前项目报错，不能把整套功能视为已验证。 |
| Tileable Pack 01（`49278`） | 可平铺表面贴图。 | **需验证**。 | 可试作道路、地面或场景材质；名称不足以确认具体贴图种类及 URP 效果。 |
| Free Stylized Textures（`204742`） | 风格化地表与物件贴图。 | **风格化地图可评估**。 | 适合明确采用 stylized 风格的场景；不能仅靠换贴图得到真实植被。 |
| Stone（`651`） | 名称指向石材或石头资源。 | **需验证**。 | 可考虑岩石或地表细节，但须先核对包内实际资源类型和风格。 |

### Low Poly Tools Bundle（`195642`）实包核对

核对对象是 `Assets/Art/Polaris - Low Poly Ecosystem/` 中已导入的旧版套装，不是商店中的 `Low Poly Tools Bundle 2`。包内四个工具目录均有源码、材质或 Shader，并有共 12 个示例场景；这些内容目前仍属于备用资源，尚未接入正式地图。

| 工具 | 包内证据与可做的事 | 对当前项目的判断 |
| --- | --- | --- |
| Polaris 2021 | `Polaris - Low Poly Terrain Engine/` 含地形编辑器、网格地形运行时、植被与样例树石，以及 3 个演示场景；随包 changelog 最新记录为 `V2021.1.13`。 | 可在虚构低模地图中试作地形、道路或植被布景。和家村已有基于现实高程的 Unity Terrain，直接改用 Polaris 会改变地形数据和制作流程；不能把样例地形当成现实地貌。 |
| Poseidon | `Poseidon - Low Poly Water System/` 含 URP 水面和河流 Shader、水面配置及 4 个演示场景；随包 changelog 最新记录为 `V1.8.6`。 | 可先在独立样板场景试池塘、河道的风格化水面，再比较和家村现有视觉。Unity Console 已出现其 `WetLensURP`、`UnderwaterURP` 及依赖旧 Post Processing 包的后处理 Shader 错误；水下和镜头水滴效果目前不可作为可用功能。基础水面仍须单独验证画面和安卓性能。 |
| Jupiter | `Jupiter - Procedural Sky/` 含天空 Shader、昼夜配置和 4 个演示场景；随包 changelog 最新记录为 `1.2.11`。 | 可用于其他风格化地图的天空原型。何家村旧天空与远征时间流程已退役；新的采用需先提供视觉预览并确认，不引入独立时间权威。 |
| TextureGraph | `TextureGraph/Core/` 含节点编辑器、噪声、渐变、混合等生成 Shader 和示例场景；随包 README 标明 beta，changelog 最新记录为 `V0.3.1b`。 | 可试作泥土、岩石、道路等可平铺贴图，再导出项目自有资源；生成贴图不会自动给地表分类或道路位置提供现实依据，编辑器稳定性与输出效果仍需验证。 |

套装含编辑器工具和运行时代码，不只是模型素材。导入备用库后，Unity 能通过 AssetDatabase 找到上述示例场景，但这只证明资源已被索引。若要正式采用，先在独立样板场景逐项验证 URP 效果、编译日志与目标平台性能；只将选中的输出及依赖整理到 `Assets/Game/Art/`，不让正式地图长期引用备用目录。

## 植被与自然环境

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| [Nature Package - Forest Environment](https://assetstore.unity.com/packages/3d/vegetation/nature-package-forest-environment-165645)（`165645`） | 森林场景的树、灌木、草和花等自然物。 | **写实森林、野外地图优先候选**。 | 商店页定位偏写实且标注 URP 兼容；各地图仍需核对树种、模型尺度、LOD 与性能。 |
| [Mountain Trees - Dynamic Nature](https://assetstore.unity.com/packages/3d/vegetation/trees/mountain-trees-dynamic-nature-107004)（`107004`） | PBR 山地冷杉及不同树形。 | **山地针叶林地图可评估**。 | 可补针叶林层次；地图植物类型仍须与场景设定相符。 |
| Free Trees（`103208`） | 少量独立树木模型。 | **需验证**。 | 可补树种变化；商店简介列出冷杉、橡树、棕榈和杨树，选用须符合当地外观。 |
| Nature Starter Kit 2（`52977`） | 自然环境入门资源。 | **需验证**。 | 可作为草木、地表候选；当前缓存缺少足够明细，不能预判写实度或 URP 适配。 |
| TreePack1_Free（`83972`） | 树木资源包。 | **需验证**。 | 可补树种，但需先看模型、材质、LOD 和实际树种。 |
| Low-Poly Simple Nature Pack（`162153`） | 低模树、草、石等自然物。 | **风格化地图和原型适用**。 | 可快速布景；若地图追求写实，需要另选造型。 |
| 低多边形简约自然套装（`20000544`） | 低模自然物套装。 | **风格化地图和原型可评估**。 | 需与 `162153` 核对是否同一系列及具体内容，避免重复选型。 |
| Low Poly Environment - Nature Free - LOWPOLY MEDIEVAL FANTASY SERIES（`187052`） | 低模树木、灌木、岩石等。 | **风格化地图或占位布景适用**。 | 和家村少量试摆已随旧地图退役；用于当前街机场景前需重新核对资源与视觉确认。 |
| [Brute Force - Grass Shader](https://marketplace.unity.com/packages/vfx/shaders/brute-force-grass-shader-192388)（`192388`） | 草的着色、风动与交互效果；不提供完整树木资源。 | **草地表现可评估**；不宜作为移动端 Terrain 草地的唯一方案。 | [开发者说明](https://www.bruteforcegame.com/grass-compatibilityfeatures)称支持 URP，但基于 Terrain splat map 的草地绘制尚不支持移动端 Terrain；需按地图和目标平台验证。 |

## 道路、场景构件与布景

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| Dreamteck Splines（`61926`） | 用样条线编辑曲线和沿线内容。 | **道路、河道等线状内容制作可评估**。 | 可按各地图的路径依据制作曲线；本身不提供写实道路材质。 |
| GSpawn - Level Designer（`45021`） | 编辑器内批量摆放、散布场景物件。 | **多种地图的布景流程可评估**。 | 可提高植被和物件摆放效率；摆放范围仍应由地图设计数据决定。 |
| Lake Race Track（`55908`） | 湖区赛道环境资源。 | **赛道或湖区地图可评估**。 | 主题明确；不宜直接套用到需要按现实资料还原的乡村道路。 |
| Rope Tool（`200856`） | 制作绳索类线状物件。 | **局部场景道具可用**。 | 可用于营地、桥梁等装饰，不能替代道路或地形工具。 |
| Medieval Houses Modular Vol 1（`196678`） | 模块化中世纪房屋。 | **中世纪主题地图可评估**。 | 是否采用取决于当前街机关卡设定和用户的视觉确认。 |
| The Big Castle Kit（`75818`） | 城堡建筑构件。 | **奇幻城堡地图可评估**。 | 可用作特定主题地图的建筑素材，不适合所有地图通用。 |
| MyTown Light（`201936`） | 轻量城镇场景构件。 | **城镇地图原型可评估**。 | 可快速搭建城镇；若用于现实参照地图还需核对当地形态。 |
| POLYGON - City Pack - Art by Synty（`95214`） | 低模城市道路、建筑和道具。 | **低模城市地图可评估**。 | 适用于风格匹配的街机城市场景；采用前仍须预览确认。 |
| POLYGON - 城市资源包(City Pack) - Art by Synty（`20000945`） | 城市低模环境资源。 | **低模城市地图可评估**。 | 与 `95214` 可能为同产品不同商店条目，实际内容须核对。 |
| POLYGON - Prototype Pack - Art by Synty（`137126`） | 通用灰盒和低模原型构件。 | **各类关卡原型适用**。 | 便于验证比例与通路；最终美术需按地图风格选择。 |
| POLYGON - Prototype Pack - Art by Synty（`20001023`） | 通用灰盒和低模原型构件。 | **关卡原型适用**。 | 与 `137126` 可能重复，采用前核对版本与内容。 |
| Snaps Prototype \| Sci-Fi / Industrial（`136759`） | 科幻、工业场景模块。 | **科幻或工业地图原型适用**。 | 模块用途与主题明确；最终视觉仍需按地图需求核对。 |
| CScape City System（`86716`） | 城市环境制作系统。 | **大规模城市地图需单独评估**。 | 偏城市生产工作流；具体适配、依赖和性能尚未验证。 |
| 3D Scifi Kit Vol 3（`121447`） | 科幻场景构件。 | **科幻地图可评估**。 | 可供不同主题关卡使用，具体模块需核对。 |
| 3D Scifi Kit Vol 3（`20001319`） | 科幻场景构件。 | **科幻地图可评估**。 | 与 `121447` 可能是不同商店条目，避免重复接入。 |
| Dark Fantasy Kit（`123894`） | 黑暗奇幻场景构件。 | **黑暗奇幻地图可评估**。 | 可用于风格匹配的地图；不作为现实地图的空间依据。 |
| Dark Fantasy Gigantic Environment（`155143`） | 大型黑暗奇幻环境。 | **大型奇幻地图可评估**。 | 适合相应主题；接入前需检查尺寸、依赖与性能。 |
| Fantasy Adventure Environment（`70354`） | 奇幻冒险场景环境。 | **奇幻地图可评估**。 | 可用于遗迹、野外等虚构地图，实际素材类型待核对。 |
| Desert Environment - Town & Palace \| CITADEL（`194885`） | 沙漠城镇与宫殿环境。 | **沙漠主题地图可评估**。 | 气候、建筑和地表主题明确；不适合所有地图通用。 |
| Post Apocalyptic World Pack（`188358`） | 末日废墟与环境构件。 | **末日地图可评估**。 | 作为完整环境接入前需检查依赖与性能。 |
| Secret Radio Room - The Swamp House（`155339`） | 沼泽房屋或室内场景。 | **沼泽、室内关卡可评估**。 | 是否可拆用及其实际风格需查看包内内容。 |
| Shooting Range Interiors（`62603`） | 射击场室内环境。 | **训练或射击关卡可评估**。 | 室内用途明确；与自然野外地图的用途不同。 |
| Polygon Dining Room（`199435`） | 低模餐厅室内场景。 | **风格化室内地图可评估**。 | 需与地图整体视觉风格一致。 |
| Wood Texture Floor（`24970`） | 木地板表面贴图。 | **室内地表可评估**。 | 用途偏室内地面；须核对贴图内容及渲染效果。 |
| Megapoly.Art - Containers（`197278`） | 集装箱场景道具。 | **工业或营地地图可评估**。 | 可用作主题匹配的场景道具。 |
| Sci-Fi Police Pack（`199158`） | 科幻警察主题资源。 | **科幻地图需验证**。 | 名称未表明是否包含场景构件，先核对包内内容。 |
| RPG Poly Pack - Lite（`148410`） | 低模 RPG 主题资源。 | **风格化 RPG 地图需验证**。 | 名称不足以确认有多少地图构件。 |

## 天空、天气与场景氛围

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| 8K Skybox Pack Free（`150926`） | 高分辨率天空盒。 | **场景氛围可评估**。 | 可试日夜天空，但需核对视觉方向、移动端内存与项目昼夜系统。 |
| Skybox Series Free（`103633`） | 多种天空盒。 | **适用**。 | 曾用于已退役的何家村日夜天空；新场景需按实际光照与预览确认重新核对。 |
| Starfield Skybox（`92717`） | 星空天空背景。 | **夜景可评估**。 | 适合夜晚或特殊地图，不替代完整天气与光照逻辑。 |
| FREE Skybox Extended Shader（`107400`） | 扩展天空盒 Shader。 | **需验证**。 | 这是渲染工具而非天空图；先检查 URP 兼容性。 |
| InfiniCLOUD HDRP - URP, Volumetric clouds & particles（`154133`） | 体积云和天气氛围表现。 | **可评估**。 | 标题包含 URP，但性能及与当前天空、昼夜系统的配合需实测。 |
| Great Battle Background_05(SunshineForest)（`182785`） | 阳光森林背景。 | **远景可评估**。 | 名称显示偏背景用途；先核对是 2D 图像还是 3D 环境及视角限制。 |
| Bakery - GPU Lightmapper（`122218`） | GPU 静态光照烘焙工具。 | **地图光照流程可评估**。 | 属于编辑器生产工具，不提供场景模型；需检查设备与项目光照流程。 |
| CN Asset - Bakery - GPU Lightmapper（`20000944`） | GPU 静态光照烘焙工具。 | **地图光照流程可评估**。 | 与 `122218` 可能为同产品不同商店条目，避免重复选型。 |

## 角色、动物与动画

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| Robot Hero : PBR HP Polyart（`106154`） | 机器人角色模型。 | **科幻角色可评估**。 | 名称指向高模 PBR；需检查骨骼、材质和 URP 效果。 |
| POLYGON WW2 German soldiers（`106450`） | 二战德军风格士兵角色。 | **历史或军事题材可评估**。 | 题材和低模风格明确；需核对动画与项目设定。 |
| RPG Hero PBR HP Polyart（`121480`） | PBR RPG 英雄角色。 | **奇幻角色可评估**。 | 需检查角色骨骼、动画和 URP 材质。 |
| FurryS2: Sorceres and Archer（`125966`） | 兽人风格施法者与弓箭手。 | **奇幻角色可评估**。 | 职业与造型方向由名称判断，实际模型和动画需核对。 |
| Animated Hands with Weapons Pack（`132915`） | 带武器动作的第一人称手部模型。 | **第一人称玩法可评估**。 | 主要服务第一人称视角；需核对武器种类和动画。 |
| Dog Knight PBR Polyart（`135227`） | 犬类骑士角色。 | **奇幻 NPC 或敌人可评估**。 | 需检查风格、骨骼及 URP 材质。 |
| Modular Animal Knight Polyart（`135722`） | 可组合动物骑士角色。 | **奇幻角色变体可评估**。 | 模块化有利于制作变体，需核对部件和骨骼兼容性。 |
| Robot Soldier（`142438`） | 机器人士兵角色。 | **科幻敌人可评估**。 | 需查看具体模型、动画和材质。 |
| Human Basic Motions FREE（`154271`） | 人形基础动作。 | **Humanoid 动画可评估**。 | 可补待机、移动等基础动作；需检查动作清单及重定向效果。 |
| Fantasy Chess RPG Character - Arthur（`160647`） | 棋盘或 RPG 风格角色。 | **风格化角色可评估**。 | 需核对比例、动画与项目角色风格。 |
| Human Melee Animations FREE（`165785`） | 人形近战动作。 | **近战角色可评估**。 | 需检查动作覆盖、Root Motion 与 Humanoid 重定向。 |
| Meshtint Free Barbarian（`17013`） | 野蛮人角色模型。 | **奇幻角色可评估**。 | 造型和材质风格需与目标场景核对。 |
| Unity-Chan! Model（`18705`） | Unity-chan 角色模型。 | **角色原型或动画验证可用**。 | 更适合作为风格明确的样例角色；商业场景需核对授权条款。 |
| SCI FI CHARACTERS MEGA PACK VOL 2（`186888`） | 科幻角色合集。 | **科幻阵营角色可评估**。 | 需核对角色数量、骨骼统一性和 URP 材质。 |
| Casual RPG Character - Ekard（`207786`） | 休闲 RPG 角色。 | **风格化 NPC 可评估**。 | 需核对模型比例及与其他角色的一致性。 |
| Casual RPG Character - Avelyn（`207789`） | 休闲 RPG 角色。 | **风格化 NPC 可评估**。 | 与 Ekard 可作为同系列候选，具体动画需核对。 |
| Casual RPG Monster - Wolf（`208130`） | 狼类怪物模型。 | **野外敌人或动物可评估**。 | 需检查骨骼、动作、尺度和视觉写实度。 |
| Stylized Customizable Avatars FREE（`230391`） | 可定制风格化头像或角色。 | **角色原型可评估**。 | 名称不足以判断实际是 2D 头像还是 3D 模型，需看包内内容。 |
| Barbarian warrior（`75519`） | 野蛮人战士角色。 | **奇幻角色可评估**。 | 需检查模型、动画和材质表现。 |
| Warrior Pack Bundle 1 FREE（`36405`） | 战士角色或动作资源包。 | **需验证**。 | 名称不能确定具体模型与动作清单。 |
| RPG Character Mecanim Animation Pack FREE（`65284`） | RPG 人形 Mecanim 动作。 | **角色动作可评估**。 | 需核对动作清单、骨架和重定向质量。 |
| RPG Fantasy (Pack)（`47749`） | RPG 奇幻题材资源包。 | **需验证**。 | 名称未说明角色、道具和环境各占多少。 |
| Fish - PolyPack（`202232`） | 低模鱼类模型。 | **水域生态可评估**。 | 需核对鱼种、游动动画和水域场景风格。 |
| Robot Kyle \| URP（`4696`） | Unity 机器人示例角色。 | **URP 角色原型可用**。 | 标题标注 URP；更适合控制器和动画验证。 |
| Robot Kyle \| URP（`20005946`） | Unity 机器人示例角色。 | **URP 角色原型可用**。 | 与 `4696` 可能为不同商店地区条目，避免重复接入。 |

## 武器、载具与战斗特效

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| Medieval Weapons - Melee Weapon Pack - Swords, Axes, Maces, Polearms（`162491`） | 中世纪近战武器模型。 | **奇幻或历史题材可评估**。 | 武器类别明确；需核对握持点、碰撞与材质。 |
| FREE - Low Poly Swords - RPG Weapons（`198166`） | 低模 RPG 剑模型。 | **风格化近战可评估**。 | 需与角色和场景美术风格一致。 |
| FREE - RPG Weapons（`199738`） | RPG 武器资源。 | **需验证**。 | 名称未明确武器类型、模型数量和风格。 |
| Weapons Pack - Realistic LowPoly（`200967`） | 偏写实的低面数武器模型。 | **低成本战斗道具可评估**。 | 需核对具体武器、贴图和 URP 材质。 |
| Sci-fi Weapons Arsenal（`68367`） | 科幻武器合集。 | **科幻战斗可评估**。 | 需核对武器种类、特效与项目性能目标。 |
| Ammunition pack (demo)（`82208`） | 弹药道具示例。 | **射击场景道具可评估**。 | Demo 版本内容可能有限，需查看实际模型。 |
| APC Military low-poly 3D Vehicle（`201843`） | 低模装甲运兵车。 | **军事地图载具可评估**。 | 需核对模型比例、动画与可驾驶性。 |
| Kart Skins - Karting Microgame Add-Ons（`174460`） | 卡丁车样例外观。 | **竞速玩法原型可评估**。 | 属于 Karting Microgame 附加内容，需核对依赖。 |
| [Legacy] Impact - Physics Interaction System（`161617`） | 物理碰撞触发音效、粒子等反馈。 | **研究或局部参考**。 | 标注 Legacy，需确认维护状态、URP 特效及与现有反馈系统的职责重叠。 |
| AOE Magic spells Vol.1（`133012`） | 范围魔法特效。 | **奇幻技能可评估**。 | 需检查粒子 Shader 的 URP 表现与移动端开销。 |
| Highlight Plus - All in One Outline & Selection Effects（`134149`） | 物体描边和选中高亮。 | **交互反馈可评估**。 | 需验证当前 URP 版本、摄像机叠加和性能。 |
| Realistic Effects Pack 3（`27523`） | 写实类粒子与视觉特效。 | **战斗或环境反馈可评估**。 | 需逐个检查特效内容与 URP 材质。 |
| Stylized Slash VFX（`200233`） | 风格化斩击特效。 | **近战反馈可评估**。 | 需核对风格、URP Shader 和动作时序。 |
| War FX（`5669`） | 战争题材特效。 | **战斗场景可评估**。 | 资源年代较早，URP 兼容性与性能需验证。 |
| Action RPG FX（`38222`） | 动作 RPG 技能特效。 | **奇幻战斗可评估**。 | 需核对特效目录、URP 材质和与技能系统的配合。 |

## 音乐与音效

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| Sci-Fi Gun Sounds（`162226`） | 科幻枪械音效。 | **科幻武器可评估**。 | 需试听音色与武器类型。 |
| Gun & Explosion Sounds（`176739`） | 枪声与爆炸音效。 | **射击战斗可评估**。 | 可用于战斗反馈，需试听并检查授权。 |
| Fantasy Sounds Bundle（`193760`） | 奇幻题材音效合集。 | **奇幻玩法可评估**。 | 需查看音效分类、质量和重复度。 |
| Fantasy Ambience Sounds Pack（`196349`） | 奇幻环境氛围音。 | **场景氛围可评估**。 | 需试听是否匹配具体地图的环境与昼夜。 |
| UI SFX Mega Pack（`245595`） | 界面操作音效。 | **UI 反馈可评估**。 | 需统一界面音色并检查音量规范。 |
| Footstep and Foley Sounds（`85360`） | 脚步与拟音音效。 | **角色移动和交互可评估**。 | 需核对地表分类与现有脚步逻辑。 |
| Colossal Game Music Collection（`88190`） | 游戏背景音乐合集。 | **配乐候选**。 | 需试听曲目、循环点与不同场景情绪。 |

## 界面、图标与二维资源

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| Fantasy Inventory Icons [Free]（`143805`） | 奇幻物品栏图标。 | **背包 UI 可评估**。 | 需核对图标类别、尺寸和项目 UI 风格。 |
| Fantasy Inventory Icons [Free] - 奇幻背包图标【免费版】（`20006864`） | 奇幻物品栏图标。 | **背包 UI 可评估**。 | 与 `143805` 可能为不同商店地区条目，避免重复接入。 |
| GUI Pro - Fantasy RPG（`170168`） | 奇幻 RPG 界面素材。 | **奇幻界面可评估**。 | 需核对切片、字体、分辨率与现有 UI 结构。 |
| GUI Pro - Fantasy RPG（`20000212`） | 奇幻 RPG 界面素材。 | **奇幻界面可评估**。 | 与 `170168` 可能重复，比较内容与版本。 |
| GUI Pro - Survival Clean（`194741`） | 生存游戏风格界面素材。 | **生存玩法 UI 可评估**。 | 风格较明确，需核对现有 UI 的视觉方向。 |
| GUI Pro - Survival Clean（`20000443`） | 生存游戏风格界面素材。 | **生存玩法 UI 可评估**。 | 与 `194741` 可能重复，比较内容与版本。 |
| POLYGON - Icons Pack - Art by Synty（`202117`） | POLYGON 系列图标。 | **低模风格 UI 可评估**。 | 需核对图标语义与场景风格。 |
| POLYGON - Icons Pack - Art by Synty（`20001009`） | POLYGON 系列图标。 | **低模风格 UI 可评估**。 | 与 `202117` 可能重复，避免双份导入。 |
| Pretty Objects（`201639`） | 装饰性物件或图像素材。 | **需验证**。 | 名称不足以确定是 UI、2D 还是 3D 资源。 |
| UX Flat Icons [Free]（`202525`） | 扁平化 UI 图标。 | **工具界面可评估**。 | 需检查图标集合与项目视觉规范。 |
| Dragon Crashers \| UI Toolkit Sample Project（`231178`） | UI Toolkit 示例项目。 | **UI 技术参考**。 | 可参考界面结构，整套样例不宜直接替代项目 UI。 |
| Dragon Crashers - UI Toolkit示例项目（`20006106`） | UI Toolkit 示例项目。 | **UI 技术参考**。 | 与 `231178` 可能为不同地区条目。 |
| UI Samples（`20005954`） | Unity UI 示例资源。 | **界面原型参考**。 | 需核对使用的是 UGUI 还是 UI Toolkit。 |
| UI Examples \| uGUI Sample Project（`25468`） | UGUI 界面样例。 | **UGUI 实现参考**。 | 可参考组件和布局，需按项目 UI 架构改造。 |
| Knight Sprite Sheet (Free)（`93897`） | 骑士二维角色帧图。 | **2D 玩法或图标原型可评估**。 | 当前用途取决于是否需要二维角色。 |
| Ninja Sprite Sheet (Free)（`93901`） | 忍者二维角色帧图。 | **2D 玩法或图标原型可评估**。 | 需核对帧数和动画内容。 |
| ProTips - Tooltip System（`44361`） | 提示框系统。 | **UI 交互可评估**。 | 需检查与现有 UI 控制层和本地化的集成成本。 |
| Item & Inventory System（`45568`） | 物品和背包系统。 | **设计参考**。 | 属于完整业务系统，需先比较现有数据与存档职责。 |
| FairyGUI（`49479`） | 独立 UI 框架。 | **架构研究价值**。 | 与项目现有 UI 技术栈重叠，接入成本需单独评估。 |
| Unknown Package（`195224`） | 用途未知。 | **暂不能判断**。 | My Assets 缓存未提供可信产品名称，先核对产品页再分类。 |

## 编辑器、输入与通用开发工具

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| Better Streaming Assets（`103788`） | 统一访问 StreamingAssets 文件。 | **资源读取流程可评估**。 | 需核对目标平台、现有加载方式和包版本。 |
| Editor Console Pro（`11889`） | 增强 Unity Console 日志查看。 | **开发效率工具可评估**。 | 仅影响编辑器工作流，需确认 Unity 版本兼容性。 |
| EditorToolbox（`198306`） | Unity 编辑器功能扩展。 | **开发效率工具可评估**。 | 名称笼统，需核对具体功能与维护状态。 |
| Colourful Hierarchy Category GameObject（`205934`） | 层级面板分类着色。 | **场景整理可评估**。 | 编辑器辅助工具，需检查版本和团队使用习惯。 |
| Odin Inspector and Serializer（`89041`） | Inspector 扩展和序列化。 | **编辑器与数据工具可评估**。 | 需核对项目现有 Odin 使用情况及序列化边界。 |
| Odin Inspector and Serializer（`20000098`） | Inspector 扩展和序列化。 | **编辑器与数据工具可评估**。 | 与 `89041` 可能为不同地区条目，避免重复导入。 |
| DOTween (HOTween v2)（`27676`） | 补间动画工具。 | **动画与 UI 过渡可评估**。 | 需检查项目是否已有 DOTween，避免版本冲突。 |
| DOTween (HOTween v2)（`20002646`） | 补间动画工具。 | **动画与 UI 过渡可评估**。 | 与 `27676` 可能重复，比较版本。 |
| iTween（`84`） | 旧式补间动画工具。 | **旧项目维护参考**。 | 资源年代较早，接入前需验证 Unity 2022 与项目现有动画方案。 |
| Lean Localization（`28504`） | 文本和资源本地化工具。 | **本地化流程可评估**。 | 需与项目当前语言配置和 UI 生命周期衔接。 |
| Fingers - Touch Gestures for Unity（`41076`） | 触屏手势识别。 | **移动端输入可评估**。 | 需检查与项目输入系统和触控 UI 的冲突。 |
| Resize Pro（`61344`） | 资源尺寸或缩放处理工具。 | **资源优化流程可评估**。 | 具体支持的资源类型与输出质量需核对。 |

## 角色控制、AI 与玩法框架

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| Character Controller SUPER（`135316`） | 角色移动控制框架。 | **控制手感研究可评估**。 | 与现有控制器职责重叠，需先查看架构与 URP 示例。 |
| Character Movement Fundamentals（`144966`） | 角色移动基础框架。 | **移动机制参考**。 | 需比较项目现有移动、碰撞和网络需求。 |
| RV Smart Ai（`146170`） | AI 行为或感知系统。 | **NPC AI 可评估**。 | 需核对与现有 AI、导航和事件架构的集成成本。 |
| RV Honor Ai（`188764`） | AI 行为系统。 | **NPC AI 可评估**。 | 名称不足以确认具体功能；需和 RV Smart Ai 比较。 |
| Simple Grid Pathfinding（`194954`） | 网格寻路。 | **格子地图或战术玩法可评估**。 | 开放式地形是否适用需看路径算法与动态障碍支持。 |
| Physics Prediction（`199756`） | 物理运动预测。 | **弹道或交互预览可评估**。 | 需核对支持的物理类型和实时计算成本。 |
| Love/Hate（`33063`） | 角色关系或情感数值系统。 | **NPC 关系玩法参考**。 | 完整玩法系统需与现有数据、事件和存档模型对接。 |
| Love/Hate（`20005294`） | 角色关系或情感数值系统。 | **NPC 关系玩法参考**。 | 与 `33063` 可能为不同地区条目，避免重复接入。 |
| NodeCanvas（`14914`） | 行为树、状态机等可视化逻辑工具。 | **AI 或剧情逻辑可评估**。 | 需核对现有逻辑框架、运行成本和授权席位。 |
| Polarith AI Pro \| Movement with 3D Sensors（`71465`） | 传感器驱动的 AI 移动。 | **群体或避障行为可评估**。 | 需比较现有导航方案及目标平台性能。 |
| Sensor Toolkit（`88036`） | 感知、检测组件。 | **NPC 感知可评估**。 | 需检查检测方式、性能和现有碰撞层规则。 |
| RPG Builder（`177657`） | RPG 玩法编辑框架。 | **玩法设计参考**。 | 涉及角色、装备和任务等大量系统，不能仅按单个模块评估。 |
| RPG Editor: ORK Framework 2（`125912`） | RPG 系统编辑框架。 | **玩法架构参考**。 | 框架覆盖面大，需比较现有数据模型与维护状态。 |

## 网络、完整系统与示例项目

| 名称 | 作用 | 是否适用 | 判断依据 |
| --- | --- | --- | --- |
| UFPS: Ultimate FPS (legacy)（`106748`） | 旧版第一人称射击框架。 | **历史实现参考**。 | 标注 legacy；Unity 2022、URP 和项目控制架构均需核对。 |
| 3D Game Kit \| 3D Sample Project（`115747`） | 第三人称 3D 游戏示例。 | **关卡和交互参考**。 | 属于完整样例，需按模块查看，不宜整包替换现有系统。 |
| PUN 2 - FREE（`119922`） | Photon PUN 2 网络框架。 | **联网方案评估候选**。 | 需核对当前服务、授权及项目网络架构。 |
| Super Multiplayer Shooter Template（`124977`） | 多人射击游戏模板。 | **联网射击原型参考**。 | 完整模板包含大量逻辑与配置，需拆解评估。 |
| Mirror（`129321`） | Unity 网络框架。 | **联网方案评估候选**。 | 需比较项目现有同步模型、平台和维护成本。 |
| Mobile Tools + Complete Game（`132284`） | 移动端工具与游戏样例。 | **移动端技术参考**。 | 名称无法确认具体功能，需核对包内容及 Unity 版本。 |
| uMMORPG Remastered - MMORPG Engine（`159401`） | MMORPG 完整框架。 | **多人 RPG 架构参考**。 | 大型框架与现有数据、服务器和 UI 职责可能重叠。 |
| Bolt \| Now is Unity Visual Scripting & is built-in to Unity 2021 & newer（`163802`） | 旧版可视化脚本工具。 | **旧工程维护参考**。 | Unity 新版本已有 Visual Scripting，需核对当前产品维护情况。 |
| JU TPS 2 : Third Person Shooter System + Vehicle Physics（`179767`） | 第三人称射击与载具物理系统。 | **射击或载具玩法参考**。 | 完整系统需比较项目控制器、输入和物理架构。 |
| (Deprecated) Rival - DOTS Character Controller for Unity（`225129`） | DOTS 角色控制器。 | **历史实现参考**。 | 明确标注 Deprecated；不宜直接作为长期基础。 |
| BR200 - Battle Royale Multiplayer - Photon Fusion（`226753`） | Photon Fusion 大逃杀多人模板。 | **大规模联网玩法参考**。 | 依赖网络服务和完整游戏结构，需单独评估。 |
| Standard Assets 2018.4 \| Check out Starter Assets: First Person & Third Person（`32351`） | Unity 旧版标准资源。 | **旧资源迁移参考**。 | 标题指向 Unity 2018.4；URP 材质和 Unity 2022 兼容性需验证。 |
| Tanknarok Multiplayer - Photon Fusion（`202468`） | Photon Fusion 多人坦克样例。 | **联网载具原型参考**。 | 需核对网络服务依赖与复用范围。 |
| Tanks! \| 3D Sample Project（`46209`） | Unity 坦克游戏样例。 | **战斗和关卡教学参考**。 | 适合学习实现结构，需核对旧版资源在 URP 的显示效果。 |
| 坦克对战 \| 完整项目（`20006081`） | Unity 坦克游戏样例。 | **战斗和关卡教学参考**。 | 与 `46209` 可能为不同地区条目，避免重复接入。 |
| For Unity Learn \| 3D Game Kit（`20005962`） | 3D Game Kit 学习资源。 | **关卡和交互参考**。 | 与 `115747` 可能为不同地区条目。 |
| Starter Assets - ThirdPerson \| URP 初始资产 - 第三人称控制器（`20005950`） | Unity URP 第三人称控制器示例。 | **角色控制原型可评估**。 | 标题标注 URP；需比较项目现有输入和控制器职责。 |
| First Person + Third Person \| Character Controllers（`196526`） | 第一、第三人称角色控制器。 | **控制方式原型可评估**。 | 需核对切换机制、Unity 2022 和 URP 示例。 |
