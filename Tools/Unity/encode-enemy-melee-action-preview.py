"""给隔离Unity采样加时间说明，输出原速动作候选，不修改正式资源。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/EnemyMeleeAction-v1-20261023'
output = root / 'Backup/Artifacts/EnemyMeleeAction-v1-20261023'
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
report = {'scope': 'Temporary clone sampling, not live combat or multiplayer capture.', 'clips': {}}
for kind, title in [('basic', '普通持刀怪'), ('zombie', '普通丧尸')]:
    frames = []
    for index, path in enumerate(sorted((source / kind).glob('frame-*.png'))):
        frame = Image.new('RGB', (900, 720), (18, 26, 31))
        frame.paste(Image.open(path).convert('RGB'), (0, 90))
        draw = ImageDraw.Draw(frame)
        draw.text((18, 8), f'{title}：靠近准备 → 0.4秒后挥击；中途跑开则取消', font=font, fill='white')
        draw.text((18, 47), '隔离动态候选，未正式接入 / 正式主角同框、原尺寸 / 20fps原速', font=small, fill=(205, 218, 224))
        t = index * .05
        if t < .6:
            state, color = '第一次：靠近玩家', (210, 225, 235)
        elif t < 1:
            state, color = f'准备出手 {t - .6:.2f} / 0.40秒（尚未判伤害）', (255, 216, 115)
        elif t < 1.4:
            state, color = '完成前摇：挥击与伤害判定对齐', (255, 160, 140)
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
(output / 'index.html').write_text('''<!doctype html><meta charset="utf-8"><title>普通近战动作候选 v1</title>
<style>body{margin:0;background:#121a1f;color:#e2e9ed;font:16px system-ui}main{max-width:1100px;margin:auto;padding:24px}h1{font-size:26px}p{line-height:1.7}img{display:block;width:100%;max-width:900px;margin:20px auto;border-radius:12px}button{padding:9px 18px;cursor:pointer}small{color:#bdcbd3}</style>
<main><p style="padding:14px;background:#6a2a22;color:white">本版持刀方向已被否决。请查看<a style="color:white" href="../ZombieUnarmedAction-v2-20261023/index.html">丧尸空手动作v2</a>；旧版仅作历史记录。</p><h1>普通近战：先看得见出手，再判伤害</h1><p>这是正式模型克隆的隔离动作候选，尚未接入正式玩法。先演示持续贴近：进入范围后抬手准备0.4秒再挥击；再演示中途跑开：收手取消并恢复追击。两种怪均与正式主角保持原尺寸同框。</p>
<button onclick="document.querySelectorAll('img').forEach(i=>{i.src=i.dataset.src+'?t='+Date.now()})">重新播放两个候选</button>
<img src="zombie-melee-action.gif" data-src="zombie-melee-action.gif" alt="普通丧尸攻击候选">
<img src="basic-melee-action.gif" data-src="basic-melee-action.gif" alt="普通持刀怪攻击候选">
<p><small>现有攻击片段验证能进入Attack；候选手动调整采样节奏。没有改动正式Prefab、动画、控制器或伤害规则。此页不作为真人联机实战录像。</small></p></main>''', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
