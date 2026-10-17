# 手枪弹丸微调

仅GrayboxBullet/ArcadeBulletBasic子节点缩放由(.9,.9,.8)调整为(.8,.8,.66)，独立材质亮度降低，连射/散射、根碰撞、枪口、后坐、音频及战斗配置保持。before/after为同镜头/尺度正式资源隔离预览，改前仅在克隆中恢复旧手枪参数。

ArcadeGunfireStyleTests最多5项，EditMode/includePassingTests=true，5/5通过，发现3433不是执行数。

实射验收未通过：第一次在Boot未就绪时提前调用菜单，NullResourceLoader错误保留invalid-start-console；重载后确认真实Main/资源加载器/菜单就绪，正式新局与切枪24秒观察只移动、无射击，敌人数110未减少，不能算枪效验收。已核对临时RunId正式放弃并回到菜单、TimeScale=1，停止Play。虚拟输入时序修正尚未得到有效新样本，后续特殊武器实射批次继续验证；该缺口不以资源测试通过替代。

动画补修后的该24秒窗口Warning查询为空，但本轮无射击，不能覆盖受击时的全部池化表现。
