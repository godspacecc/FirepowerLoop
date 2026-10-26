"""给隔离Unity采样加时间说明，输出原速动作候选，不修改正式资源。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/ZombieUnarmedAction-v2-20261023'
output = root / 'Backup/Artifacts/ZombieUnarmedAction-v2-20261023'
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
report = {'scope': 'Temporary clone sampling, not live combat or multiplayer capture.', 'clips': {}}
for kind, title in [('shambler-claw', '空手扑抓'), ('gaunt-claw', '疾行丧尸扑抓'), ('bloated-claw', '重型丧尸扑抓'), ('shambler-bite-draft', '近身咬击草案（临时骨骼动作）')]:
    frames = []
    for index, path in enumerate(sorted((source / kind).glob('frame-*.png'))):
        frame = Image.new('RGB', (900, 720), (18, 26, 31))
        frame.paste(Image.open(path).convert('RGB'), (0, 90))
        draw = ImageDraw.Draw(frame)
        draw.text((18, 8), f'{title}：准备0.4秒 → 攻击；跑开则取消', font=font, fill='white')
        draw.text((18, 47), '左：空手丧尸 / 右：正式主角（持枪） / 隔离候选，未接入 / 20fps原速', font=small, fill=(205, 218, 224))
        t = index * .05
        if t < .6:
            state, color = '第一次：靠近玩家', (210, 225, 235)
        elif t < 1:
            state, color = f'准备出手 {t - .6:.2f} / 0.40秒（尚未判伤害）', (255, 216, 115)
        elif t < 1.4:
            state, color = '完成前摇：咬击草案' if 'bite' in kind else '完成前摇：空手扑抓', (255, 160, 140)
        elif t < 1.7:
            state, color = '收招，随后重置演示第二种情况', (210, 225, 235)
        elif t < 2.3:
            state, color = '第二次：重新靠近玩家', (210, 225, 235)
        elif t < 2.55:
            state, color = f'准备出手 {t - 2.3:.2f} / 0.40秒', (255, 216, 115)
        elif t < 2.8:
            state, color = '玩家离开范围：收手取消，本次不判伤害', (147, 232, 170)
        else:
            state, color = '取消后恢复追击，再靠近需要重新准备', (147, 232, 170)
        draw.rectangle((0, 666, 900, 720), fill=(18, 26, 31))
        draw.text((18, 678), f'{state}   |   {t:.2f}s', font=small, fill=color)
        frames.append(frame)
    assert len(frames) == 80
    gif = output / f'{kind}-melee-action.gif'
    frames[0].save(gif, save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
    frames[17].save(output / f'{kind}-windup.png')
    frames[22].save(output / f'{kind}-strike.png')
    with Image.open(gif) as image:
        count = image.n_frames
        duration = sum(image.seek(i) or image.info['duration'] for i in range(count))
        assert count == 80 and duration == 4000
    report['clips'][kind] = {'frames': count, 'durationMs': duration, 'sha256': hashlib.sha256(gif.read_bytes()).hexdigest()}
shutil.copyfile(source / 'inspection.txt', output / 'inspection.txt')
(output / 'verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
(output / 'index.html').write_text('''<!doctype html><meta charset="utf-8"><title>丧尸空手动作候选 v2</title>
<style>body{margin:0;background:#121a1f;color:#e2e9ed;font:16px system-ui}main{max-width:1050px;margin:auto;padding:24px}h1{font-size:26px}p{line-height:1.7}img{display:block;width:100%;max-width:900px;margin:20px auto;border-radius:12px}button{padding:9px 18px;cursor:pointer}small{color:#bdcbd3}h2{font-size:21px;margin-top:32px}.notice{border-left:4px solid #e9bf66;padding:10px 16px;background:#283035}</style>
<main><h1>丧尸只用空手：抓挠、近身咬击</h1><p class="notice">上一版持刀候选已被否决。本页所有左侧丧尸都没有武器；右侧持枪者是正式主角，用于同框比较。0.4秒前摇和跑开取消继续保留。</p>
<button onclick="document.querySelectorAll('img').forEach(i=>{i.src=i.dataset.src+'?t='+Date.now()})">重新播放</button>
<h2>扑抓：复用现有丧尸攻击片段</h2><p>靠近后抬臂准备，持续贴近才出手；第二次演示中途跑开，取消并追击。</p>
<img src="shambler-claw-melee-action.gif" data-src="shambler-claw-melee-action.gif" alt="丧尸空手扑抓候选">
<h2>近身咬击：临时动作草案</h2><p>用当前丧尸克隆叠加探身和头部前倾，仅展示咬击方向。现有模型没有独立下颌骨，尚无嘴部开合或正式撕咬片段；本段不是已接入的完整撕咬动作。</p>
<img src="shambler-bite-draft-melee-action.gif" data-src="shambler-bite-draft-melee-action.gif" alt="近身咬击临时草案">
<details><summary>查看另外两类丧尸的空手扑抓</summary>
<img src="gaunt-claw-melee-action.gif" data-src="gaunt-claw-melee-action.gif" alt="疾行丧尸空手扑抓">
<img src="bloated-claw-melee-action.gif" data-src="bloated-claw-melee-action.gif" alt="重型丧尸空手扑抓"></details>
<p><small>正式模型克隆、保持原尺寸；每段4秒、20fps。未修改正式Prefab、动画、控制器或战斗代码，不是当前实战录像。具体动作仍待采用后接入。</small></p></main>''', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
