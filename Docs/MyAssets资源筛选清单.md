# Package Manager My Assets 资源筛选清单

> 盘点时间：2026-09-24  
> 当前项目环境：Unity `2022.3.55f1c1`，URP  
> 本清单仅记录候选，不代表已经下载、导入或完成兼容性验证。

## 1. 盘点结论

- Package Manager 共显示 164 行，按 Asset Store 产品 ID 去重后为 161 个资产。
- 其中 139 个为 `DownloadAvailable`，即尚未下载；22 个为 `ImportAvailable`，即已经缓存在本机但尚未导入或可再次导入。
- 本次不按何家村当前美术风格淘汰资源。只要未来可能用于场景、天空、天气、光照、角色、动画、特效、声音、UI、原型或编辑器生产流程，就保留为候选。
- 未下载资产只能标记为“待下载验证”。在实际采用前，仍需检查 URP 材质、Unity 2022.3 兼容性、模型尺度、碰撞体、动画骨骼、移动端性能和授权说明。

## 2. 建议优先查看的未下载资源

### 2.1 天空、天气与场景氛围

| ID | 资源 | 用途 | 初步判断 |
| --- | --- | --- | --- |
| `150926` | 8K Skybox Pack Free | 高分辨率天空盒 | 通用价值高，优先下载验证 URP 天空材质 |
| `103633` | Skybox Series Free | 多套天空盒 | 已导入并选取日夜两套天空盒接入和家村首块场景 |
| `92717` | Starfield Skybox | 星空天空盒 | 夜景、梦境、特殊地图候选 |
| `107400` | FREE Skybox Extended Shader | 天空盒扩展 Shader | 有潜力，但需先验证 URP 和 Unity 2022.3 |
| `154133` | InfiniCLOUD HDRP - URP, Volumetric clouds & particles | 体积云与粒子 | 氛围价值高；运行成本和 URP 版本需重点验证 |
| `122218` | Bakery - GPU Lightmapper | GPU 光照烘焙 | 可改善静态场景光照；要求 Windows + NVIDIA GPU |
| `20000944` | CN Asset - Bakery - GPU Lightmapper | Bakery 中文区条目 | 与 `122218` 重复，二选一，优先版本更新者 |

### 2.2 自然、地形与村庄周边

| ID | 资源 | 用途 | 初步判断 |
| --- | --- | --- | --- |
| `187052` | Low Poly Environment - Nature Free | 低模自然环境 | 已导入并整理树木、灌木、岩石正式副本，完成和家村出生点附近试布置 |
| `162153` | Low-Poly Simple Nature Pack | 低模自然环境 | 与项目已有自然资源对比后再选用 |
| `165645` | Nature Package - Forest Environment | 森林植被 | 树林、村边和远景候选 |
| `52977` | Nature Starter Kit 2 | 基础自然环境 | 通用地形与植被补充 |
| `107004` | Mountain Trees - Dynamic Nature | 山地树木 | 山林层次和远景候选，需验证 Shader |
| `103208` | Free Trees | 树木 | 轻量补充候选 |
| `104753` | Free Island Collection | 岛屿与景观 | 水域、孤岛或特殊地图候选 |
| `192388` | Brute Force - Grass Shader | 草地 Shader | 价值高但应先做 URP、性能和风场验证 |
| `228178` | (BIRP) Procedural Terrain System | 程序化地形 | 标明 BIRP，不应直接用于当前 URP；可作技术参考 |
| `195642` | Low Poly Tools Bundle | 地形工具 | 已缓存；可在隔离场景验证工作流 |
| `28672` | Noise Brush | 地形笔刷 | 可用于地形快速塑形 |

### 2.3 建筑、道路与环境道具

| ID | 资源 | 用途 | 初步判断 |
| --- | --- | --- | --- |
| `196678` | Medieval Houses Modular Vol 1 | 模块化中世纪房屋 | 与村庄建筑最直接相关，优先查看 |
| `123894` | Dark Fantasy Kit | 奇幻建筑与环境 | 可拆取建筑、道路和道具，不必整套采用 |
| `155143` | Dark Fantasy Gigantic Environment | 大型奇幻环境 | 适合远景、遗迹和大型地图候选 |
| `70354` | Fantasy Adventure Environment | 奇幻冒险环境 | 村外区域、遗迹、地牢候选 |
| `194885` | Desert Environment - Town & Palace \| CITADEL | 城镇与宫殿 | 沙漠地图和大型聚落候选 |
| `75818` | The Big Castle Kit | 城堡套件 | 城堡、城墙、关卡地标候选 |
| `201936` | MyTown Light | 轻量城镇环境 | 快速原型和远景城镇候选 |
| `95214` | POLYGON - City Pack - Art by Synty | 低模城市 | 城镇道路、商铺、街道物件可拆用 |
| `137126` | POLYGON - Prototype Pack - Art by Synty | 原型环境与道具 | 灰盒、布局验证和缺件占位价值高 |
| `86716` | CScape City System | 城市生成/建模工具 | 仅在需要大型城市时评估，接入成本较高 |
| `55908` | Lake Race Track | 湖区道路/赛道 | 道路、湖岸和特殊关卡候选 |
| `199435` | Polygon Dining Room | 室内家具 | 民居室内、酒馆、餐厅道具候选 |
| `197278` | Megapoly.Art - Containers | 集装箱道具 | 工业区、营地和仓库候选 |
| `200856` | Rope Tool | 绳索道具/工具 | 井、桥、吊装和环境交互候选 |
| `651` | Stone | 石头道具 | 通用场景填充物 |
| `49278` | Tileable Pack 01 | 可平铺地面材质 | 地面、墙面和原型材质候选 |
| `24970` | Wood Texture Floor | 木地板材质 | 民居和室内地面候选 |

### 2.4 关卡制作辅助

| ID | 资源 | 用途 | 初步判断 |
| --- | --- | --- | --- |
| `45021` | GSpawn - Level Designer | 关卡摆放与编辑 | 对大量建筑、植被和道具布景很有价值 |
| `61926` | Dreamteck Splines | 样条线 | 道路、河流、围栏、轨迹和路径候选 |
| `134149` | Highlight Plus | 描边与选择效果 | 可用于交互物、高亮目标和编辑可视化 |
| `61344` | Resize Pro | 资源尺寸处理工具 | 贴图和资源优化候选，先在副本验证 |

## 3. 全部未下载资产（139 个，按用途分组）

以下条目均为 `DownloadAvailable`。名称相近的中文区与国际区资源可能是同一产品的不同上架条目，下载前应比较版本，只保留一份。

### 3.1 环境、天空、植被、建筑与场景道具

- `20001319` CN Asset - 3D Scifi Kit Vol 3
- `121447` 3D Scifi Kit Vol 3
- `150926` 8K Skybox Pack Free
- `192388` Brute Force - Grass Shader
- `86716` CScape City System
- `155143` Dark Fantasy Gigantic Environment
- `123894` Dark Fantasy Kit
- `194885` Desert Environment - Town & Palace | CITADEL
- `70354` Fantasy Adventure Environment
- `104753` Free Island Collection
- `107400` FREE Skybox Extended Shader
- `103208` Free Trees
- `182785` Great Battle Background_05(SunshineForest)
- `154133` InfiniCLOUD HDRP - URP, Volumetric clouds & particles
- `55908` Lake Race Track
- `162153` Low-Poly Simple Nature Pack
- `196678` Medieval Houses Modular Vol 1
- `197278` Megapoly.Art - Containers
- `200856` Rope Tool
- `107004` Mountain Trees - Dynamic Nature
- `201936` MyTown Light
- `165645` Nature Package - Forest Environment
- `52977` Nature Starter Kit 2
- `28672` Noise Brush
- `95214` POLYGON - City Pack - Art by Synty
- `137126` POLYGON - Prototype Pack - Art by Synty
- `199435` Polygon Dining Room
- `188358` Post Apocalyptic World Pack
- `199158` Sci-Fi Police Pack
- `155339` Secret Radio Room - The Swamp House
- `62603` Shooting Range Interiors
- `136759` Snaps Prototype | Sci-Fi / Industrial
- `92717` Starfield Skybox
- `651` Stone
- `75818` The Big Castle Kit
- `49278` Tileable Pack 01
- `83972` TreePack1_Free
- `24970` Wood Texture Floor
- `228178` (BIRP)Procedural Terrain System
- `45021` GSpawn - Level Designer
- `61926` Dreamteck Splines
- `122218` Bakery - GPU Lightmapper
- `20000944` CN Asset - Bakery - GPU Lightmapper

### 3.2 角色、动物、动画、武器与载具

- `20005946` CN Asset - Robot Kyle | URP
- `82208` Ammunition pack (demo)
- `132915` Animated Hands with Weapons Pack
- `201843` APC Military low-poly 3D Vehicle
- `207789` Casual RPG Character - Avelyn
- `207786` Casual RPG Character - Ekard
- `208130` Casual RPG Monster - Wolf
- `160647` Fantasy Chess RPG Character - Arthur
- `196526` First Person + Third Person | Character Controllers
- `202232` Fish - PolyPack
- `198166` FREE - Low Poly Swords - RPG Weapons
- `199738` FREE - RPG Weapons
- `125966` FurryS2: Sorceres and Archer
- `154271` Human Basic Motions FREE
- `162491` Medieval Weapons - Melee Weapon Pack - Swords, Axes, Maces, Polearms
- `106154` Robot Hero : PBR HP Polyart
- `4696` Robot Kyle | URP
- `142438` Robot Soldier
- `47749` RPG Fantasy (Pack)
- `121480` RPG Hero PBR HP Polyart
- `186888` SCI FI CHARACTERS MEGA PACK VOL 2
- `230391` Stylized Customizable Avatars FREE
- `18705` Unity-Chan! Model
- `36405` Warrior Pack Bundle 1 FREE

### 3.3 特效、Shader 与场景反馈

- `161617` [Legacy] Impact - Physics Interaction System
- `133012` AOE Magic spells Vol.1
- `134149` Highlight Plus - All in One Outline & Selection Effects
- `27523` Realistic Effects Pack 3
- `200233` Stylized Slash VFX
- `5669` War FX

### 3.4 音乐与音效

- `88190` Colossal Game Music Collection
- `196349` Fantasy Ambience Sounds Pack
- `193760` Fantasy Sounds Bundle
- `85360` Footstep and Foley Sounds
- `176739` Gun & Explosion Sounds
- `162226` Sci-Fi Gun Sounds
- `245595` UI SFX Mega Pack

### 3.5 UI、图标、2D 角色与界面参考

- `20006106` CN Asset - Dragon Crashers - UI Toolkit示例项目
- `20006864` CN Asset - Fantasy Inventory Icons [Free] - 奇幻背包图标【免费版】
- `20000212` CN Asset - GUI Pro - Fantasy RPG
- `20005954` CN Asset - UI Samples
- `231178` Dragon Crashers | UI Toolkit Sample Project
- `143805` Fantasy Inventory Icons [Free]
- `170168` GUI Pro - Fantasy RPG
- `194741` GUI Pro - Survival Clean
- `45568` Item & Inventory System
- `93897` Knight Sprite Sheet (Free)
- `93901` Ninja Sprite Sheet (Free)
- `202117` POLYGON - Icons Pack - Art by Synty
- `201639` Pretty Objects
- `44361` ProTips - Tooltip System
- `25468` UI Examples | uGUI Sample Project
- `202525` UX Flat Icons [Free]
- `195224` Unknown Package

### 3.6 编辑器、动画、输入、本地化与通用工具

- `20002646` CN Asset - DOTween (HOTween v2)
- `20000098` CN Asset - Odin Inspector and Serializer
- `27676` DOTween (HOTween v2)
- `205934` Colourful Hierarchy Category GameObject
- `11889` Editor Console Pro
- `198306` EditorToolbox
- `41076` Fingers - Touch Gestures for Unity
- `84` iTween
- `28504` Lean Localization
- `89041` Odin Inspector and Serializer
- `61344` Resize Pro
- `103788` Better Streaming Assets

### 3.7 AI、寻路、物理与玩法框架

- `20005294` CN Asset - Love/Hate
- `33063` Love/Hate
- `135316` Character Controller SUPER
- `144966` Character Movement Fundamentals
- `199756` Physics Prediction
- `71465` Polarith AI Pro | Movement with 3D Sensors
- `188764` RV Honor Ai
- `146170` RV Smart Ai
- `88036` Sensor Toolkit
- `194954` Simple Grid Pathfinding
- `177657` RPG Builder
- `125912` RPG Editor: ORK Framework 2
- `14914` NodeCanvas

### 3.8 网络、完整系统、样例项目与原型模板

- `20005962` CN Asset - For Unity Learn | 3D Game Kit
- `20005950` CN Asset - Starter Assets - ThirdPerson | URP
- `20006081` CN Asset - 坦克对战 | 完整项目
- `115747` 3D Game Kit | 3D Sample Project
- `225129` (Deprecated) Rival - DOTS Character Controller for Unity
- `163802` Bolt | Now is Unity Visual Scripting & is built-in to Unity 2021 & newer
- `226753` BR200 - Battle Royale Multiplayer - Photon Fusion
- `32351` Standard Assets 2018.4 | Check out Starter Assets: First Person & Third Person
- `119922` PUN 2 - FREE
- `124977` Super Multiplayer Shooter Template
- `202468` Tanknarok Multiplayer - Photon Fusion
- `46209` Tanks! | 3D Sample Project
- `159401` uMMORPG Remastered - MMORPG Engine
- `106748` UFPS: Ultimate FPS (legacy)
- `129321` Mirror
- `132284` Mobile Tools + Complete Game
- `49479` FairyGUI

## 4. 已缓存、可导入的资产（22 个）

这些不是本轮“未下载”重点，但可作为现成候选。正式使用前仍需在隔离场景检查，避免整包直接导入主业务目录。

- `20000443` CN Asset - GUI Pro - Survival Clean
- `20001009` CN Asset - POLYGON - Icons Pack - Art by Synty
- `20001023` CN Asset - POLYGON - Prototype Pack - Art by Synty
- `20000945` CN Asset - POLYGON - 城市资源包(City Pack) - Art by Synty
- `20000544` CN Asset - 低多边形简约自然套装
- `38222` Action RPG FX
- `75519` Barbarian warrior
- `135227` Dog Knight PBR Polyart
- `204742` Free Stylized Textures
- `165785` Human Melee Animations FREE
- `179767` JU TPS 2 : Third Person Shooter System + Vehicle Physics
- `174460` Kart Skins - Karting Microgame Add-Ons
- `195642` Low Poly Tools Bundle
- `17013` Meshtint Free Barbarian
- `135722` Modular Animal Knight Polyart
- `106450` POLYGON WW2 German soldiers
- `65284` RPG Character Mecanim Animation Pack FREE
- `148410` RPG Poly Pack - Lite
- `68367` Sci-fi Weapons Arsenal
- `200967` Weapons Pack - Realistic LowPoly
- `103633` Skybox Series Free
- `187052` Low Poly Environment - Nature Free - LOWPOLY MEDIEVAL FANTASY SERIES

## 5. 重复项与风险提示

### 5.1 重复或同系列条目

- Bakery、DOTween、Dragon Crashers、3D Game Kit、Fantasy Inventory Icons、GUI Pro、Love/Hate、Odin、POLYGON、Robot Kyle 等同时存在中文区和国际区条目。下载前比较版本、更新时间和许可证，只保留一份。
- Package Manager 的 164 行中有 3 行重复显示；按产品 ID 去重后为 161 个资产。
- `Unknown Package` 缺乏可信名称和用途信息，暂不建议下载。

### 5.2 暂不建议直接接入主项目

- `(Deprecated) Rival - DOTS Character Controller`、`[Legacy] Impact`、`Bolt`、`iTween`、`Standard Assets 2018.4`、`UFPS legacy` 属于弃用、旧版或已被官方方案替代的内容，只保留研究价值。
- BR200、Super Multiplayer Shooter、uMMORPG、JU TPS、RPG Builder、ORK、完整 Sample Project 等会携带大量脚本、设置和依赖。需要时应先在临时项目或隔离副本中检查，不能整包导入主项目。
- `(BIRP)Procedural Terrain System` 明确面向 Built-in Render Pipeline，与当前 URP 不匹配。
- InfiniCLOUD、草地 Shader、天空扩展 Shader、旧环境包可能需要材质升级或 Shader 替换。
- Bakery 的当前说明要求 Windows 与 NVIDIA GPU；团队或设备不满足时，不应把它设为唯一烘焙流程。

## 6. 后续验证顺序

1. 先验证天空与氛围：`8K Skybox Pack Free`、`Skybox Series Free`、`Starfield Skybox`、`InfiniCLOUD`。
2. 再验证村庄环境：`Medieval Houses Modular Vol 1`、自然环境包、树木、草地、道路、石头、木地板和可平铺材质。
3. 需要提高布景效率时，再评估 `GSpawn`、`Dreamteck Splines`、`Bakery`。
4. 每次只下载一个已确认资产；下载后先检查目录、许可证、URP 材质和示例场景，不立即导入全部内容。
5. 正式采用时只复制实际需要的资源及依赖到 `Client/Assets/Game/Art/`，保留 `.meta`，不要让正式 Prefab 长期依赖第三方原始包目录。
