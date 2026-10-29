# 资源限定测试失败排查与夹具收口

依据：根代理统一运行的 tests-resource.json。此子任务未调用 Unity、未运行测试。遵循 systematic-debugging：先对照失败堆栈、现役配置、Prefab 专属目录与实际调用链，再修改夹具；最终通过结果须以根代理复验为准。

## 已定位原因

- GunLocomotion 的 MovingPistol 测试仍假设 basic 是手枪且射击/移动姿态必须不同；当前 basic 是已采用 AK，同一姿态合法。保留后坐方向/幅度、上身动作姿态及恢复检查，只移除旧手枪专属“必须不同”前提。
- DynamicResourceAddress 的字典已只列10个现役窗口，却仍断言字典必须19项。删除自计数断言，保留每个窗口实际 Path 与完整资源地址检查。
- Gallery 武器选项数14硬编码过时，改为检查当前目录的每一个显示名与空手选项，保留角色间一致性检查。
- 新感染兵/蛮兵有已转换骨架单位与姿态的专属目录。旧测试把主目录 Entry 直接用于所有骨架，导致副手5.8米和朝向异常。正式实例表现测试改为使用当前角色目录；不增大握持或朝向容差。
- Gallery 真实选择和 LateUpdate 同样存在套用主目录姿态的问题。根代理追加授权后，改为由选项稳定ID查当前角色专属目录。缺失时明确拒绝、清空武器和动作，并显示带武器ID的提示。真实 SelectEquipment 测试覆盖正确专属 Entry 和缺失拒绝，不绕过为直接 SetWeapon。
- GetComponentInChildren 首个 Animator 可能是非Humanoid/旧隐藏模型；挂点与动作检查改用 Presenter 已绑定的 BodyAnimator。
- 两个弓测试仍调用旧 SetWeaponPresentation 开关，现已接入共享 Presenter 与普通物品6的现役共享入口。
- 普通物品映射测试把喷火器 WorldItemId=0当作普通装备；跳过无普通物品身份的目录项，喷火器继续由街机展示测试覆盖。
- Inspector测试 Initialize(null, provider) 明确设置 initialized=false，LateUpdate直接返回；独立夹具显式开启采样，并将临时目录绑定到当前临时角色 Presenter，不加载架构或用户本机姿态覆盖。
- 短剑已采用手局部偏移(0.13,0.05,0)本就不满足旧“根距手掌<0.08m”断言。改为严格检查实际Humanoid挂点、已采用局部偏移与朝向；保留剑身朝向几何检查，未改正式姿态或增大容差。
- 旧BasicEnemy测试按旧Hand_R/Finger/Weapon层级检查静态节点，新感染兵使用专属骨架与动态共享武器；改为实例化后检查正式专属Entry、实际主手骨及准确局部位置/旋转。

## 复验范围

由根代理串行 EditMode，includePassingTests=true：GunLocomotionAnimationTests最多17；DynamicResourceAddressTests最多27；AppearanceGallerySceneTests最多26；根已修正的 CharacterPresentationTests最多8。禁止无效过滤回退或扩大为完整套件。

未修改任何采用的模型、材质、动画、姿态或缩放数据；本次运行时代码只修保留的造型调试目录选择与刷新。git diff --check通过。尚无此修订后的 Unity 结果，不据此宣称资源测试已全部通过。

## tests-final-A 后的六项定位

根代理结果：CharacterPresentation 8/8、GunLocomotion 17/17、DynamicResourceAddress 27/27；Gallery 20/26，尚有六项。再次对照真实失败行后修正：

- Gallery 某个角色的 Presenter 没有 Catalog，真实入口已明确拒绝；测试使用相同的 null-safe 查找，并检查清空与提示，而非自身先抛空引用。
- BowFirePort 的普通物品入口需要真实配置 Provider；夹具现临时注册 CreateConfigProvider，并在 finally 恢复原实例。BowUses 的最后一步失败是“清空后必须恢复原controller引用”的旧前提；现验证现役 Profile 的空手 override 和实际空手动画。
- 三个几何失败的原值：BasicEnemy/basic 枪向前点积 0.794274867（旧统一阈值0.8）、Brute 短剑点积0.879211187（旧统一阈值0.9）、Brute/basic/PistolIdleMatched 副手间隙0.135114715m（旧统一阈值0.07m）。这些值均来自现役角色专属目录，不能反向改正式姿态满足旧测试。
- 枪轴与剑轴现严格核对实际Humanoid手骨乘以已采用的目录姿态，误差小于0.01度，并保持向角色前半空间的方向检查；手部挂点与局部偏移仍严格核对。
- 副手仍在每个已配置动作的五个采样时刻检查。可达目标继续使用0.07m原容差；对于超过当前上下臂可达区间的目标，检查到达理论最近点，额外误差最多0.005m，同时保证上下臂不伸长、锁骨移动最多0.0801m。没有把不可达目标当作握持已完美，若造型需消除实际间隙须另走视觉预览确认，不在本次修改姿态。
- Brute 专属目录的 basic 明确绑定手枪模型、PistolIdleMatched/PistolFireMatched；它是该怪物自己的试装姿态，不能因为玩家 basic 已采用AK而强制改为步枪动画。

本修订仍未运行Unity；待根代理以 AppearanceGallerySceneTests 最大26项、includePassingTests=true复验。若新的可达边界检查失败，将继续按实际骨长和求解结果定位，不扩大数值阈值。
## tests-final-B 与 tests-recheck-A 后的限定修订

- 根复验 Gallery 已25/26，剩余枪轴姿态差34.9738度；EditMode手动 animator.Update 不会自动执行Presenter的LateUpdate。夹具现调用与Gallery相同的 ApplyOffhandGrip(Idle) 正式采样后入口再检查严格目录姿态，未改变阈值或正式数值。
- ArcadeHud正式摇杆存在提示文字，旧“不得有文字”契约来自退役普通地图UI。现检查所有现有TMP提示不接收射线，保留固定Ring、Handle图标与尺寸一致性。
- Pause异步恢复测试仍须回到SafeCheckpoint，不能改期望为Arena。该夹具调用兼容TryStartNewRun，不读取掉落配置，故与loot.arcade.drop身份无关。现先验证恢复字节后InspectPending确实有效，再等待正式重试事务最多10帧；若仍失败，将附LoadingUI.ErrorText作为真正拒绝原因。此处目前是针对异步生命周期的夹具收口与诊断增强，尚无证据宣称恢复问题已解决。