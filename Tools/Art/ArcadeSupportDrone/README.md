# 街机四旋翼支援无人机

项目自制美术资源：灰白机身、深色旋翼和保护圈、黄色识别条、前方蓝色镜头。旋翼均为独立网格和中心轴；未添加自动播放动画或玩法脚本。

- `ArcadeSupportDrone.blend`：可编辑美术源，包含模型及预览摄影棚。
- `preview.png` / `preview_top.png`：透视及俯视预览。
- `create_drone.py`：Blender Python 生成入口，使用空启动文件，不继承设备上的资源链接。
- `verify_drone.py`：独立导入 FBX，核对网格、UV、材质、尺寸与源文件外部依赖。
- 游戏导出文件：`Client/Assets/Game/Art/Arcade/SupportDrone/Models/ArcadeSupportDrone.fbx`。

模型采用米制，约宽 1.41 米、长 1.31 米、高 0.42 米，共 3,384 个三角面、5 个网格、4 个纯色材质，无外部贴图。四个旋翼命名为 `Rotor_FL/FR/RL/RR`，子网格为 `Propeller_FL/FR/RL/RR`。源坐标前方为 Blender -Y，FBX 使用 -Z Forward / Y Up 转换。

生成及验证命令（将 Blender 路径替换为本机路径）：

```powershell
& '<blender.exe>' --background --python-exit-code 1 --python 'Tools/Art/ArcadeSupportDrone/create_drone.py'
& '<blender.exe>' --background --python-exit-code 1 --python 'Tools/Art/ArcadeSupportDrone/verify_drone.py'
```

2026-10-08 已通过 Unity MCP 接入现有 `ArcadeSupportDroneVisual`，替换旧 Bird 子节点及其 Animator，保留 Prefab GUID `849107d38a6d0b64fa25c0e3b397608f`、根节点 0.57 比例和原资源地址。没有新增玩法组件或碰撞体，没有修改运行时代码和配置。

Unity 导入开启轴转换与层级保留，关闭动画导入。业务模型实例绕 Y 轴旋转 180 度，使前方镜头朝向 Unity +Z；保留 FBX 内部的坐标转换节点。四个旋翼节点的自身旋转轴为局部 Z，不能未经轴转换就按局部 Y 旋转。

正式 `Materials/` 目录包含四个项目自有 URP Lit 材质，FBX 导入器按源材质名称进行重映射。Unity 导入后的网格实际为 3,112 个三角面；Blender 多边形按 n-2 计算为 3,384 个三角面，源网格包含近零面积的三角化结果，两种统计并不相同。游戏 Prefab 包围盒约为 0.804 × 0.243 × 0.747 米。

`verify_unity.cs` 通过 Unity MCP `script-execute` 执行，核对 GUID、网格预算、UV、正式材质引用、镜头朝向、旋翼轴心、组件和依赖。使用 PreviewRenderUtility 渲染 `unity_preview.png`、`unity_preview_top.png`，验证记录为 `unity_verification.txt`。预览工具创建并清理临时预览场景，不修改 Boot 场景。

Unity `.meta` 已由 AssetDatabase 自动生成。离线生成脚本负责模型与 Blender 预览；重新生成后须重新执行 Unity 验证，检查材质重映射和模型实例的方向。未运行测试、打包或 Player Build；街机运行中的悬浮位置、跟随和远近可读性仍需实际游戏画面验收。

FBX 材质用于传递颜色，正式游戏使用独立 URP 材质；Blender 摄影棚和 Unity 资源预览不能代替实际游戏画面验收。
