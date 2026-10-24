# 集市井字道路正式接入

用户2026-10-24对MarketplaceRoad-v2/after.png及“采用并正式接入”回复“可以”。已更新集市正式Prefab引用的持久网格，八个巷口逐一接入双横路/双纵路；不改变门洞、摊位、活动区、敌人入口或规则。四轮均由配置引用同一ArcadeMarketplaceVisual。

- [正式Prefab重载全景](after.png)
- [正式门洞连接近景](connection.png)
- [已采用候选](../MarketplaceRoad-v2-20261024/after.png)

以上为独立场景从磁盘加载正式资源的实拍，正式主角原尺寸同框，不是自然对局录像。

核查：全景与采用图仅6像素存在1色阶差，近景112像素位于边缘，逐像素结果见verification.json。原正式单十字路与采用图差17.2164%，接入后已消除。保存原GUID及全部meta，墓园、神庙和暗影资源/配置前后hash无变化；四轮配置1..4及8个实际入口坐标核对通过。真实资源加载验证通过，全部网格/材质是持久资源；同一次重复集市烘焙53个资源文件SHA256全部一致。Boot未脏、编辑态，无编译中。

烘焙时发现原CopySerialized覆盖后CPU数据与原生渲染缓存不同步，已改为Mesh.Clear/CopySerialized/UploadMeshData及材质CopyPropertiesFromMaterial，实拍后确认一致；单独强制ImportAsset不足以解决，此历史失败不记为通过。原BakeAll也使用同一道路函数，后续重新烘焙不会退回十字路。

仅静态资源、制作工具与文档变更；未改配置权威源/生成结果、运行时业务，也未运行完整测试套件、Player Build或资源包构建。独立只读审查无P1/P2；唯一维护建议是同根重复调用避免叠路，已补清理新增道路节点。

制作入口：Tools/Unity/Invoke-NextThreeStagesAssetBake.ps1 -Action BakeMarketplaceRoads。
