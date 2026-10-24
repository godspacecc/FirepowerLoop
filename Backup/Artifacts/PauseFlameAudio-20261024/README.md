# 暂停按钮与纯火焰声音修正（2026-10-24）

用户明确暂停声音指点击暂停、继续等按钮的提示声；要求降低音量，喷火器只保留火焰声。

暂停入口沿用ArcadeUiClick、菜单沿用FoundationClick，单次增益均从1降至0.25（约-12.04dB）；不改用户总音量、其他UI音量或暂停规则。

喷火起声与循环来自本项目已经正式接入的AmbienceFire.wav燃烧录音。起声0.22秒并淡入淡出，循环保留完整8.156秒录音，二者按整段录音共同归一化；起声不会单独放大。原录音和正式音频meta内容未改，备用库不成为运行时依赖。

修复射击间隔改成0.20秒但声音只维持0.16秒的衔接遗漏。完整武器定义把实际基础间隔交给声音表现，持声时间为间隔加0.06秒；保留一条循环与一次短火焰起声，不逐发叠加。停火、换枪、隐藏、离场及联机暂停清理继续保留。

## 试听

[pause-buttons-new.wav](pause-buttons-new.wav)依次是新暂停入口和菜单点击；[flame-new.wav](flame-new.wav)是新循环五秒。另有pause-buttons-before-after.wav和flame-before-after.wav，均先旧后新，中间静音。试听按单次增益渲染，不包含用户系统音量、全局混音器、空间衰减或游戏其他声音，不是真人完整对局录音。

原两份喷火录音以-before.wav留存；candidate目录是生成后的单声道录音，与正式两份WAV逐字节相同。audio-verification.json记录全部试听/候选音频的时长、声道、峰值、RMS与SHA256，全部无削波。来源AmbienceFire.wav SHA256：55d5a760925dc6b81fc925bf07267b8fdd0bb0ad2903edd7018e1c7e99870e1f。

## 验证

正式间隔回归先RED：旧实现8次循环申请而非1次。修复后ArcadeFlamethrowerFeedbackTests 15/15，AudioPauseTests 2/2，PauseUiFlowTests.DirectPauseAction_UsesSamePauseMenuRouteAsKeyboard及PauseExit_RepeatedShowInvokesQuitOnlyOnce各1/1，共19项通过；includePassingTests=true，限定范围15/2/1/1。未运行完整套件或Player Build，未进行真人双端整局试听。独立只读审查无发现，源码资源与音频转换核对通过；审查者未以静态核对代替主观听感评价。最终提交结果见执行总清单。
