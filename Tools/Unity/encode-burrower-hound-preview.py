"""为隔离采样加说明，导出原速/半速、对照和完整解码证据；不修改正式资产。"""
from pathlib import Path
import argparse
import csv
import hashlib
import json
import math
import subprocess
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
out = root / 'Backup/Artifacts/HengeVolcanoPolish-v1-20261025/Creatures'
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 25)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 19)
report = {'scope': '临时候选，隔离手动采样；非实战、不验证判伤/计时/单双人规则。', 'clips': {}}


def state(kind, t):
    if kind == 'burrower':
        if t < .4: return '低伏待机'
        if t < 1.35: return '收爪下潜'
        if t < 3: return '地下原位扰动（展示既有原位方向）'
        if t < 3.5: return '前爪破土、伸爪抓地'
        if t < 4.65: return '以前爪撑起肩背，后肢跟出'
        if t < 6.65: return '低伏交替爬行'
        if t < 7.8: return '近身张口、探头咬击'
        return '收招与恢复爬行'
    if t < 2.6: return '四足错相跑动、接近'
    if t < 3.76: return '停稳锁向、压身蓄势'
    if t < 4.76: return '后肢推蹬、前冲张口扑咬'
    if t < 5.55: return '前足先落地、后足补落、缓冲'
    return '恢复、转身退离（仅拍摄路径）'


def decorated(path, title, phase, t, slow=False):
    frame = Image.new('RGB', (1280, 840), (19, 27, 31))
    frame.paste(Image.open(path).convert('RGB'), (0, 74))
    d = ImageDraw.Draw(frame)
    d.text((22, 7), f'{title} · 临时候选 v1 · {"0.5× 慢放" if slow else "1× 正常速度"}', font=font, fill=(243, 226, 180))
    d.text((22, 43), '正式主角原尺寸同框 / 隔离手动采样 / 新视觉未采用、未接入 / 地面网格每格0.5米', font=small, fill=(196, 210, 215))
    d.text((22, 803), f'{phase}    |    动作时间 {t:.2f} 秒', font=small, fill=(234, 241, 243))
    return frame


for kind, title, count in [('burrower', '潜地怪：土壳、阔爪、破土支撑', 230), ('hound', '恶犬：尖耳、四足、扑咬', 192)]:
    paths = sorted((out / kind).glob('*.png'))
    assert len(paths) == count, (kind, len(paths), count)
    item = {'sourceFrames': len(paths), 'sampleFps': 24, 'outputs': {}}
    for slow, fps, suffix in [(False, 24, 'normal'), (True, 12, 'slow')]:
        video = out / f'{kind}-{suffix}.mp4'
        cmd = [str(args.ffmpeg), '-y', '-hide_banner', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '1280x840', '-r', str(fps), '-i', '-', '-an', '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(video)]
        proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
        try:
            for i, path in enumerate(paths):
                frame = decorated(path, title, state(kind, i / 24), i / 24, slow)
                proc.stdin.write(frame.tobytes())
        finally:
            proc.stdin.close()
        assert proc.wait() == 0
        decoded = subprocess.run([str(args.ffmpeg), '-v', 'error', '-i', str(video), '-progress', 'pipe:1', '-nostats', '-f', 'null', '-'], check=True, capture_output=True, text=True)
        values = dict(line.split('=', 1) for line in decoded.stdout.splitlines() if '=' in line)
        assert int(values['frame']) == count
        assert not decoded.stderr.strip(), decoded.stderr
        item['outputs'][suffix] = {'file': video.name, 'fps': fps, 'framesDecoded': count, 'seconds': count / fps, 'bytes': video.stat().st_size, 'sha256': hashlib.sha256(video.read_bytes()).hexdigest(), 'fullDecode': True}
    gif_frames = [decorated(p, title, state(kind, i / 24), i / 24).resize((768, 504), Image.Resampling.LANCZOS) for i, p in enumerate(paths) if i % 2 == 0]
    gif = out / f'{kind}-normal.gif'
    gif_frames[0].save(gif, save_all=True, append_images=gif_frames[1:], duration=83, loop=0, optimize=False)
    compare = Image.new('RGB', (2560, 840), (19, 27, 31))
    compare.paste(Image.open(out / f'{kind}-current.png').convert('RGB'), (0, 74))
    compare.paste(Image.open(out / f'{kind}-candidate.png').convert('RGB'), (1280, 74))
    d = ImageDraw.Draw(compare)
    d.text((26, 15), f'{title} — 左：当前正式几何轮廓回读', font=font, fill='white')
    d.text((1306, 15), '右：本次临时候选（未采用、未接入）', font=font, fill=(243, 226, 180))
    d.text((26, 802), '两边同机位、同尺度、相同正式主角原尺寸；旧轮廓只读取现有几何参数，不含血条与图标。', font=small, fill='white')
    compare.save(out / f'{kind}-comparison.png')
    poses = sorted(out.glob(f'{kind}-pose-*.png'))
    sheet = Image.new('RGB', (1920, math.ceil(len(poses) / 3) * 400 + 50), (19, 27, 31))
    d = ImageDraw.Draw(sheet); d.text((20, 8), f'{title} / 临时候选关键帧 / 隔离采样', font=font, fill='white')
    for i, p in enumerate(poses):
        t = float(p.stem.rsplit('-', 1)[1]); x, y = i % 3 * 640, i // 3 * 400 + 50
        sheet.paste(Image.open(p).convert('RGB').resize((640, 360), Image.Resampling.LANCZOS), (x, y))
        d.text((x + 12, y + 367), f'{t:.2f}s · {state(kind,t)}', font=small, fill='white')
    sheet.save(out / f'{kind}-keyframes.jpg', quality=94)
    rows = list(csv.DictReader((out / f'{kind}-trajectory.csv').open(encoding='utf-8-sig')))
    assert len(rows) == count * 4
    assert all(math.isfinite(float(r[f])) for r in rows for f in ['pawX', 'pawY', 'pawZ', 'kneeY', 'upperLength', 'lowerLength'])
    steady = [r for r in rows if (.2 < float(r['time']) < 2.1 if kind == 'hound' else 5.2 < float(r['time']) < 6.35)]
    drifts=[]
    for leg in range(4):
        samples=[r for r in steady if int(r['leg'])==leg]
        for a,b in zip(samples,samples[1:]):
            if a['stance']=='True' and b['stance']=='True' and abs(float(a['pawY'])-float(b['pawY']))<.0001:
                drifts.append(abs(float(b['pawZ'])-float(a['pawZ'])))
    grounded=[r for r in rows if (float(r['time'])<.35 or float(r['time'])>4.8) and not(kind=='hound' and 5.55<float(r['time'])<6.1)]
    item['trajectory']={'rows':len(rows),'steadyStanceDriftMaxMeters':max(drifts,default=None),'pawBottomMinMeters':min(float(r['pawBottom']) for r in grounded),'upperLengthRange':[min(float(r['upperLength']) for r in rows),max(float(r['upperLength']) for r in rows)],'lowerLengthRange':[min(float(r['lowerLength']) for r in rows),max(float(r['lowerLength']) for r in rows)]}
    item['trajectory']['perLegLengthVariationMeters'] = {}
    for leg in range(4):
        samples = [r for r in rows if int(r['leg']) == leg]
        variation = {k:max(float(r[k]) for r in samples)-min(float(r[k]) for r in samples) for k in ['upperLength','lowerLength']}
        assert max(variation.values()) < .0002, variation
        item['trajectory']['perLegLengthVariationMeters'][str(leg)] = variation
    if kind == 'hound':
        contacts = []
        for leg in [0,2]:
            contacts.append(min(float(r['time']) for r in rows if int(r['leg']) == leg and float(r['time']) > 4.4 and float(r['pawBottom']) < .002))
        assert .18 < contacts[1]-contacts[0] < .30, contacts
        item['trajectory']['landingFrontRearSeconds'] = contacts
    else:
        underground = [float(r['rootZ']) for r in rows if 1.35 <= float(r['time']) <= 3.0]
        assert max(underground) - min(underground) < .00001
        item['trajectory']['undergroundPlanarDriftMeters'] = max(underground) - min(underground)
    assert max(drifts,default=0)<.003, item['trajectory']
    assert item['trajectory']['pawBottomMinMeters'] >= -.004, item['trajectory']
    # 纯色、缺图和紫色错误材质不得成为交付。
    for p in paths:
        im=Image.open(p).convert('RGB').resize((160,90))
        pix=list(im.get_flattened_data()); assert len(set(pix))>120
        assert sum(1 for r,g,b in pix if r>220 and b>220 and g<45)/len(pix)<.001
    report['clips'][kind]=item

baseline=json.loads((out.parent/'before-formal-hashes.json').read_text(encoding='utf-8-sig'))
changed=[str(p) for p,h in baseline.items() if not(root/p).exists() or hashlib.sha256((root/p).read_bytes()).hexdigest().lower()!=h.lower()]
report['formalResources']={'filesChecked':len(baseline),'changed':changed}
assert not changed, changed
(out/'verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
(out/'index.html').write_text('''<!doctype html><meta charset="utf-8"><title>潜地怪与恶犬 临时候选 v1</title>
<style>body{background:#131b1f;color:#e4ecef;font:16px system-ui;margin:0}main{max-width:1280px;margin:auto;padding:28px}p{line-height:1.7}video,img{width:100%;display:block;margin:18px 0;border-radius:10px}h2{margin-top:40px}a{color:#a6d9eb}.notice{padding:16px;background:#30312a;border-left:4px solid #d4bd7c}</style>
<main><h1>潜地怪与恶犬 · 临时候选 v1</h1><p class="notice">本页是隔离场景中的手动动作采样，尚未采用、尚未接入正式游戏。正式主角保持Prefab原尺寸（实测约1.65米），每格地面网格0.5米。这里只展示现有行为方向，不改地下免伤/原位、锁向冲进、计时、伤害及单双人规则。</p>
<h2>潜地怪：阔爪抓地，再把肩背撑出土面</h2><p>土壳背脊与低伏四足轮廓；完整展示下潜、地下原位扰动、前爪出土、撑身、爬行及近身咬击。地下阶段不发生平面移动。</p>
<img src="burrower-comparison.png"><video controls loop playsinline src="burrower-normal.mp4" poster="burrower-candidate.png"></video>
<details><summary>0.5× 慢放与关键帧</summary><video controls loop playsinline src="burrower-slow.mp4"></video><img src="burrower-keyframes.jpg"></details>
<h2>恶犬：错相跑动，停稳后推蹬扑咬</h2><p>尖耳、长口鼻、独立下颌与三段四肢；先跑近并停稳，锁定朝向后压身扑出，前脚先落地，后脚补落恢复。最后转身退离仅为预览路径，未新增正式技能。</p>
<img src="hound-comparison.png"><video controls loop playsinline src="hound-normal.mp4" poster="hound-pose-4.45.png"></video>
<details><summary>0.5× 慢放与关键帧</summary><video controls loop playsinline src="hound-slow.mp4"></video><img src="hound-keyframes.jpg"></details>
<p>两条动作原始采样24fps。具体视觉等待用户采用。没有业务测试、Player Build或资源打包；此处不能证明真实对局或真人双端效果。</p><p><a href="verification.json">完整解码、足部轨迹与正式资源哈希验证</a> · <a href="capture-inspection.txt">Unity采样与尺寸记录</a></p></main>''',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
