"""拼接右侧海中进场的Unity临时截图；只生成预览文件，不编辑游戏资源。"""
from pathlib import Path
import hashlib
import json
import subprocess
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/DockSeaEntryPreview-v2'
target = root / 'Backup/Artifacts/DockSeaEntry-v2-20261024'
target.mkdir(parents=True, exist_ok=True)
ffmpeg = root / 'Client/Temp/ElectricTowerFeedbackTools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
assert ffmpeg.exists(), '缺少本机已配置的视频编码器'
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 25)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 20)
large = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 29)

comparison = Image.new('RGB', (1280, 720), '#13252b')
draw = ImageDraw.Draw(comparison)
draw.text((640, 23), '第二关临时试做 v2：补充屈膝、蹬腿与脚掌落地', font=large, fill='white', anchor='mt')
draw.text((640, 67), 'Unity 正式地图克隆 · 正式主角同框 · 尚未修改正式资源', font=small, fill='#acd5d7', anchor='mt')
for i, kind in enumerate(['before', 'candidate']):
    with Image.open(source / f'overview-{kind}.png') as image:
        comparison.paste(image.crop((114, 0, 1166, 720)).resize((630, 432)), (5 + i * 640, 162))
    draw.text((320 + i * 640, 124), ['当前：保留右侧外伸通道', '候选：移除整段右侧木通道'][i], font=font, fill=['white', '#98e5c2'][i], anchor='mt')
draw.text((640, 625), '其他两路、主平台、货箱、绳栏及停船保持；只试做右侧来敌', font=font, fill='#c9e2e2', anchor='mt')
draw.text((640, 668), '交替划腿 → 抓边收膝 → 前脚踩住平台 → 后腿蹬起跟上 → 站稳接步', font=small, fill='#98e5c2', anchor='mt')
comparison.save(target / 'layout-comparison.png')

files = sorted(source.glob('[0-9][0-9][0-9].png'))
slow = sorted(source.glob('slow-[0-9][0-9][0-9].png'))
assert len(files) == 180 and len(slow) == 110, (len(files), len(slow))
video = target / 'candidate-sea-entry.mp4'
command = [str(ffmpeg), '-y', '-hide_banner', '-loglevel', 'warning', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '1280x720', '-r', '20', '-i', 'pipe:0', '-an', '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(video)]
process = subprocess.Popen(command, stdin=subprocess.PIPE)
for _ in range(60):
    process.stdin.write(comparison.tobytes())
shots = {}
for paths, is_slow in [(files, False), (slow, True)]:
    for i, path in enumerate(paths):
        with Image.open(path) as image:
            frame = image.convert('RGB')
        d = ImageDraw.Draw(frame)
        d.rectangle((0, 0, 1280, 76), fill='#12252b')
        d.text((640, 10), '临时候选 v2：双腿收膝、前脚落地、后脚蹬起' + (' · ½速细看' if is_slow else ''), font=font, fill='white', anchor='mt')
        d.text((640, 45), '正式主角与便服丧尸同框 · 人工时间轴与临时骨架动作 · 非真实对局', font=small, fill='#b9d2d5', anchor='mt')
        t = 3 + i / 40 if is_slow else i / 20
        if t < 2.1:
            stage = '海面靠近：双腿在水下交替划动，脚踝配合摆动'
        elif t < 2.7:
            stage = '双手抓边：两膝开始交替收起，准备发力'
        elif t < 3.6:
            stage = '收腿撑身：膝盖弯曲、双脚向身下收回'
        elif t < 4.45:
            stage = '前腿跨上平台：明显抬膝，脚尖越边，脚掌落地'
        elif t < 5.15:
            stage = '前脚踩稳：后腿蹬起跟上，腰部抬高'
        elif t < 5.75:
            stage = '后脚补步：双脚站稳，膝盖逐渐伸展'
        else:
            stage = '已登上码头：恢复行走，后续敌人错峰上岸'
        d.rectangle((0, 658, 1280, 720), fill='#12252b')
        d.text((640, 673), stage, font=font, fill='#a8e8d0', anchor='mt')
        if not is_slow and i in (25, 48, 66, 78, 88, 100, 112, 124):
            name = {25:'approach',48:'grab',66:'tuck-knees',78:'lift-knee',88:'plant-front-foot',100:'push-back-leg',112:'stand-settle',124:'walking'}[i]
            frame.save(target / f'{name}.png')
            shots[name] = frame.copy()
        process.stdin.write(frame.tobytes())
process.stdin.close()
assert process.wait() == 0, '预览视频编码失败'
subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(video), '-f', 'null', '-'], check=True)

sheet = Image.new('RGB', (1280, 1080), '#12252b')
for i, key in enumerate(['tuck-knees','lift-knee','plant-front-foot','push-back-leg','stand-settle','walking']):
    sheet.paste(shots[key].resize((640, 360)), ((i % 2) * 640, (i // 2) * 360))
sheet.save(target / 'motion-contact-sheet.png')
hashes = json.loads((target / 'formal-source-hashes.json').read_text(encoding='utf-8'))
changed = [path for path, expected in hashes.items() if hashlib.sha256((root / path).read_bytes()).hexdigest() != expected]
assert not changed, f'正式码头资源在预览期间变化：{changed}'
import csv
motion = list(csv.DictReader((source / 'leg-motion.tsv').read_text().splitlines(), delimiter='|'))
climb = [row for row in motion if 2.7 <= float(row['time']) <= 5.75]
leg_ranges = {name:[min(float(r[name]) for r in climb),max(float(r[name]) for r in climb)] for name in ['leftKneeBend','rightKneeBend','leftFootX','leftFootY','rightFootX','rightFootY']}
assert all(leg_ranges[k][1] - leg_ranges[k][0] > 25 for k in ['leftKneeBend','rightKneeBend']), leg_ranges
(target / 'leg-motion.tsv').write_text((source / 'leg-motion.tsv').read_text(),encoding='utf-8')
audit = {'scope':'temporary cloned scene, manually sampled skeleton, no formal map or spawn changes', 'version':2, 'frames':350, 'fps':20, 'seconds':17.5, 'width':1280, 'height':720, 'full_decode':'passed', 'formal_assets_checked':len(hashes), 'formal_asset_changes':changed, 'climb_leg_ranges':leg_ranges}
(target / 'audit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')
(target / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>第二关海中进场试做 v2 · 腿脚动作</title><style>body{margin:0;background:#12252b;color:#e5f0ee;font:16px system-ui;padding:24px;max-width:1280px;margin:auto}h1{font-size:24px}video,img{width:100%;border-radius:10px}p{color:#bad3d6}figure{margin:24px 0}a{color:#9de6cf}</style><h1>第二关海中进场 v2：收膝、蹬腿、踩稳起身</h1><p>补充双腿交替划动、抬膝越边、前脚踩稳、后脚蹬起补步；末尾半速看腿脚。正式主角同框，Unity临时骨架演示，非真实对局；正式资源尚未修改。</p><video controls autoplay muted loop playsinline src="candidate-sea-entry.mp4" poster="lift-knee.png"></video><figure><img src="motion-contact-sheet.png"><figcaption>收膝 → 抬腿 → 落脚 → 蹬起 → 站稳 → 接步</figcaption></figure><p><a href="../DockSeaEntry-v1-20261023/index.html">查看原v1（动作未采用）</a></p></html>''', encoding='utf-8')
print(json.dumps(audit, ensure_ascii=False))
print(video)
