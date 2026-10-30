# 幸存者加载背景验收

用户确认候选 survivor-loading-v1.png 后接入正式 Loading.prefab。新图用于幸存者入口资源准备、地图加载和重开，闯关仍按关卡选图。

- 正式实拍：[加载界面](loading-live.png)。候选插画：[原图](survivor-loading-v1.png)。
- UiBackgroundTests：10/10通过，含原闯关与联机选图、残留关卡、资源准备、旧请求释放、新闯关入口及其它地图清理。详见 background-tests.json。
- SurvivorEntryTests：2/2通过，详见 entry-tests.json。采用真实短类过滤，includePassingTests=true；未运行全套测试或Player Build。
- 实际开始和再来一局验证：restart=True; active=True; loadingVisible=False; rifle=1，见 live-entry.txt。重开验证存档隔离到本目录isolated-save。
- 正式图片：Client/Assets/Game/Art/UI/ArcadeBackgrounds/survivor-loading.png，使用Unity导入Sprite及保存Prefab引用。
