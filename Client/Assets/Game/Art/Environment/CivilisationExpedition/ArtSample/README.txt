河岸营地美术样片（2026-10-10）

场景：Assets/Game/Scenes/ValleyArtSample.unity
制作入口：Tools/Unity/ValleyArtSampleAuthoring.cs，通过 Unity MCP script_execute 执行 Run。
实拍：Assets/Tests/Results~/ValleyArtSample/camp-landscape.png、gameplay-angle.png、layout-overview.png。

范围：独立美术验收场景，包含雕刻地形、弯曲河流、浅岸、林地、远山、枝木兽皮棚和静态火堆造型。未加入地图目录、未替换正式随机河谷、未修改存档、科技、采集与地图入口。此场景不包含玩家控制或正式探索闭环；gameplay-angle 只用于俯视画面验收，不代表可玩验证。

资源来源：复用正式 CivilisationExpedition 下的 SimpleNaturePack 树木、岩石、草灌与 RPGPP_LT 色板材质；RPGPP_LT 松树从本地 Assets/Art/RPGPP_LT/Prefabs/Nature/Vegetation/Trees/rpgpp_lt_tree_pine_01.prefab 导出实际网格到 PineVisual.asset。原资源包未修改，样片的依赖闭包不含 Assets/Art。其余地形、山体、云、兽皮棚与火堆为项目自行制作的网格。材质与调色仅保存在 ArtSample 目录，不修改正式地图共用材质。

水面：复用正式 Poseidon 项目 Shader 与独立 SampleWater 材质，三角形顶点按其 UV/颜色协议烘焙。草灌按末级 LOD 与材质合为一组网格。远山、云、兽皮棚是美术表现，无玩法碰撞；地形和已有河岸石块保留碰撞。手机性能、玩家导航、采集点与正式随机地形的适配尚未验收。

查看：在 Unity 中单独打开 ValleyArtSample.unity，避免与 Boot 一起进入播放态。样片场景不加入打包流程。

草丛补充（2026-10-10）：UpdateGrass 只重做样片草灌；新增高低混合的弯叶草簇，树下、营地边缘及近岸更密，保留棚前至岸边走查空隙。草灌共4个Renderer、无Collider，样片草灌74379三角形，未进行手机性能验收。最新实拍为camp-grass.png，结构证据为grass-verification.json。

石景补充（2026-10-10）：UpdateRocks 幂等重做水中露头石群、地面大小碎石和前景主石；按Renderer真实包围盒贴地或确定露水高度，保留棚前空隙。最新实拍camp-rocks.png，数量及依赖证据rocks-verification.json。此补充仍仅属于独立样片。
