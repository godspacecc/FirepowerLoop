# 开始页商店独立审查

## Spec结论：通过

本次任务增量符合计划：开始页新增商店入口；独立商店右上角紧凑显示金币、中部商品暂未开放、返回和关闭可用；幸存者按钮直接进入既有StartSurvivorAsync，不再弹原金币入口。旧入口资源与meta保留。未加入商品、购买或任何金币写入逻辑。

## Quality结论：通过

未发现Critical或Important待修问题。

- StartUICtrl的新增按钮绑定OnShopClick，startingRun时禁用，处理器也同时校验startingRun和IsShow，防止迟到调用打开商店。幸存者直接开局仍由原异步流程及门禁管理，存档覆盖保护与闯关继续流程未改变。
- MainMenuShopUICtrl仅调用现有TryReadGold，成功显示真实余额，坏档显示未知与本地化错误，没有伪造0或保存写入路径。OnShow和语言事件刷新，返回/关闭复用Hide并清理自身焦点；重开可重新读取修复后的文件。
- 生成StartUI新增BtnShop；商店生成View只暴露两按钮和两个动态文本。Prefab带ReferenceCollector，动态BalanceText未挂LocalizedText，固定标题/空态/返回使用配置本地化。查看正式Prefab渲染，顶部小金币和中部空态与已确认布局一致；该图是编辑器资源渲染，不冒充实战截图。
- 契约测试检查Unity反序列化后的正式引用；交互测试使用临时钱包覆盖关闭、坏档不覆盖、返回重开恢复余额、切换英文刷新、正常读后文件不变。未触碰正式玩家钱包。实现者提供契约1/1与交互1/1通过结论，本审查未重跑。初次读取时原始结果JSON与implementation-report尚未落盘，根代理收尾时应纳入本批目录。
- source-collection.txt记录精确地址Prefabs/UI/Home/MainMenuShop及startup标签，与Controller路径一致；runtime-load.txt记录真实Game.YooAssetResourceLoader加载该资源并释放异步句柄。结合实现者正常Play验证说明，当前编辑器加载链成立。generation.log记录Config生成完成及双端输出。

审查只涉及changes.json/review.patch指定内容，没有纳入其他任务工作区差异。未运行Unity操作、测试或打包，未修改生产文件。发布包与完整实战未验证，不扩大本次交付声明。
