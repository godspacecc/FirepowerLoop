# 两个后期普通轮实际观察

2026-10-17，代码7924925，Windows Unity Editor Play，正式生成v6配置，release.enabled仍0。明确隔离起点：分别从临时新局候选设置配置15／20与可达场序3／10，经正式地图加载与BeginCombat进入第一普通轮；不是从第1轮自然通关，不计入商店或40轮验收。

两图各实际12秒、各11个每秒样本，CSV中remainingQueue+spawnedSequence始终分别190／400；已实际生成敌人、进行射击／命中并受到伤害，观察最多同场8／10名，不把总量说成同屏数或性能压力已通过。后段Game View两张均已查看：[Nuketown](theme-15-late.png)、[Papaback主题第一普通图](theme-20-late.png)。后者不是独立最终Boss遭遇。

输入为虚拟Gamepad，不代表实体手柄。观察器正常结束，移除事件与设备。核对临时store与当前RunId一致后通过正式TryAbandon结束最后一个隔离局，cleanup.json为地图根空、拥有网格0／材质0、编译失败false；随后停止Play。完整路线、其余轮次、Boss、双端及平衡仍待验，未打包。
