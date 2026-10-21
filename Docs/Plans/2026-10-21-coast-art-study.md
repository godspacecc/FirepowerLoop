# 海岸场景美术样板阶段收尾

用户要求先看 Unity 内真实场景，再决定是否替换正式关卡；本阶段按用户“先收尾”要求停在独立样板。

## 已交付

- [全景实拍](Artifacts/CoastArtStudy-20261021/coast-overview.png)及[码头近景](Artifacts/CoastArtStudy-20261021/dock-detail.png)，均由 Unity Camera 渲染，不是生成式概念图。
- 样板场景为 `Client/Assets/Art/CoastStudy/CoastArtStudy.unity`。三条陆路通过岩壁留口，右侧为海岸及木码头；使用真实地表与巨石贴图，自制细叶棕榈、木板与木桩。
- 场景制作入口为 `Tools/Unity/ArcadeCoastArtStudy.cs`，只处理独立样板目录；不运行战斗、不写玩家数据、不替换正式场景、Prefab、规则或加载图。
- 当前样板实际依赖及 SHA-256 见附件 `sample-assets.json`；未采用的候选资源和 meta 保留在备用目录，不纳入交付依赖。

## 已执行核对

在正确连接的 FirepowerLoop Unity 编辑态中创建并渲染样板；入口先检查所有已打开场景干净，结束时关闭样板并还原原场景、渲染管线与阴影距离。没有 Player Build。

样板内几何核对结果见 `checks.txt`：码头木板顶部向下射线命中、8 根木桩具有非 Trigger 实体碰撞、三个陆路采样中心没有被岩石碰撞覆盖，均为 True。这是场景几何检查，不是 EditMode 测试套件，也不证明正式角色控制器已正确响应碰撞。

## 当前限制

- 正式关卡的通道、出怪和出口坐标、码头穿模问题尚未修复。需要在样板外观获确认后，统一接入现有移动规则与碰撞数据；不能只给装饰加 Collider 就宣称正式玩法完成。
- 当前仍是第一版美术样板，植被密度、细节、光照和镜头构图与参考图仍有差距。角色只用于比例参考，敌人截图处于默认姿态，未验收动画、来敌引导或通关提示。
- 未进行实际游玩、完整移动路径、敌人寻路、四向出口、联机或安卓性能验证。本阶段没有改动业务运行时代码，因此未扩展测试范围或运行整套测试。
- 最后一轮预览之后工具列表已不再暴露 Unity MCP；收尾仅冻结已经实际渲染的结果，没有补做无法执行的 Unity 验证，也没有修改 MCP 配置。

## 资源来源

- 森林包缓存 `Nature Package - Forest Environment`：仅提取所需草本模型与纹理，清单见 `Tools/Art/CoastStudy/forest-selection.json`；没有导入原包业务脚本或修改缓存。
- 免费 CC0 候选：Poly Haven [Coast Sand 01](https://polyhaven.com/a/coast_sand_01)、[Boulder 01](https://polyhaven.com/a/boulder_01)、[Fern 02](https://polyhaven.com/a/fern_02)、[Weathered Brown Planks](https://polyhaven.com/a/weathered_brown_planks)。具体下载 URL、体积和校验值保留在对应 `*-files.json`。授权说明见 [Poly Haven License](https://polyhaven.com/license)。
- 棕榈、码头、水面与双面植被 Shader 为样板自制。所有新增资源均独立保存，原图、正式资源与 meta 保留。
