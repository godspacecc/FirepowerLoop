随机河谷样片布景资源（2026-10-10）

由 Tools/Unity/ValleySceneryAuthoring.cs 将已确认的 ArtSample 样片中所需网格与材质复制到正式目录。正式 Prefab 依赖闭包不含 Assets/Art 和 ArtSample，原资源包与样片不修改。

用途：ValleyGenerationKit 引用松树、草簇、石头、兽皮棚、远山及草地/水面材质。ValleySceneryBuilder 依据已保存布局确定岸边、水中、营地周边布景，按64米块与材质合批；网格归 GeneratedValleyContentHandle 释放。新增布景不包含碰撞、采集身份或脚本，不修改河槽高度、道路、浅滩、导航、科技和存档。

草簇为30片弯叶、90三角形，三种材质；合批源网格必须保持可读。正式运行合批结果上传GPU后释放CPU顶点副本。岩石显示距离沿用资源裁剪，草簇沿用地被裁剪，远山仅作外围轮廓。

预览入口：Tools/Unity/ValleySceneryPreview.cs。实际 ValleyMapBuilder 生成固定 seed37 地图，导出营地、河岸、出发、林地和全图截图至 Assets/Tests/Results~/SceneryValley，结束后释放临时地图并恢复原场景。预览使用固定日间光照，不等同于玩家运行时昼夜画面或手机性能验收。

验证状态：实际 seed37 attempt0 生成与截图成功；先前4项 ValleySceneryTests 曾返回通过。本次未再次调用测试工具，ValleyMapBuilderTests 的超时运行不作为验收证据。玩家探索闭环与手机性能仍待验证。
