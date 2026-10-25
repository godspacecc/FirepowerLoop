"""拼接右侧海中进场的Unity临时截图；只生成预览文件，不编辑游戏资源。"""
from pathlib import Path
import hashlib
import json
import subprocess
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/DockSeaEntryPreview-v1'
target = root / 'Docs/Plans/Artifacts/DockSeaEntry-v1-20261023'
target.mkdir(parents=True, exist_ok=True)
ffmpeg = root / 'Client/Temp/ElectricTowerFeedbackTools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
assert ffmpeg.exists(), '缺少本机已配置的视频编码器'
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 25)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 20)
large = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 29)

comparison = Image.new('RGB', (1280, 720), '#13252b')
draw = ImageDraw.Draw(comparison)
draw.text((640, 23), '第二关临时试做：右侧通道改为海中爬上平台', font=large, fill='white', anchor='mt')
draw.text((640, 67), 'Unity 正式地图克隆 · 正式主角同框 · 尚未修改正式资源', font=small, fill='#acd5d7', anchor='mt')
for i, kind in enumerate(['before', 'candidate']):
    with Image.open(source / f'overview-{kind}.png') as image:
        comparison.paste(image.crop((114, 0, 1166, 720)).resize((630, 432)), (5 + i * 640, 162))
    draw.text((320 + i * 640, 124), ['当前：保留右侧外伸通道', '候选：移除整段右侧木通道'][i], font=font, fill=['white', '#98e5c2'][i], anchor='mt')
draw.text((640, 625), '其他两路、主平台、货箱、绳栏及停船保持；只试做右侧来敌', font=font, fill='#c9e2e2', anchor='mt')
draw.text((640, 668), '下一段：海里靠近 → 抓住边缘 → 撑上平台 → 起身行走', font=small, fill='#98e5c2', anchor='mt')
comparison.save(target / 'layout-comparison.png')

files = sorted(source.glob('[0-9][0-9][0-9].png'))
slow = sorted(source.glob('slow-[0-9][0-9][0-9].png'))
assert len(files) == 180 and len(slow) == 64, (len(files), len(slow))
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
        d.text((640, 10), '临时候选：右侧海中进场 / 攀爬' + (' · ½速细看' if is_slow else ''), font=font, fill='white', anchor='mt')
        d.text((640, 45), '正式主角与便服丧尸同框 · 人工时间轴与临时骨架动作 · 非真实对局', font=small, fill='#b9d2d5', anchor='mt')
        t = 2.2 + i / 40 if is_slow else i / 20
        if t < 2.1:
            stage = '海面靠近：上半身露出水面，向原右侧缺口移动'
        elif t < 2.55:
            stage = '伸手抓边：双手够住木平台边缘'
        elif t < 3.35:
            stage = '撑起上身：手掌抓边，身体抬出水面'
        elif t < 4.1:
            stage = '翻上平台：抬腿收身，准备站起'
        elif t < 4.75:
            stage = '起身：从攀爬过渡到普通行走'
        else:
            stage = '已登上码头：恢复行走，后续敌人错峰上岸'
        d.rectangle((0, 658, 1280, 720), fill='#12252b')
        d.text((640, 673), stage, font=font, fill='#a8e8d0', anchor='mt')
        if not is_slow and i in (25, 48, 62, 74, 100):
            name = {25:'approach', 48:'grab', 62:'pull-up', 74:'climb', 100:'standing'}[i]
            frame.save(target / f'{name}.png')
            shots[name] = frame.copy()
        process.stdin.write(frame.tobytes())
process.stdin.close()
assert process.wait() == 0, '预览视频编码失败'
subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(video), '-f', 'null', '-'], check=True)

sheet = Image.new('RGB', (1280, 720), '#12252b')
for i, key in enumerate(['approach', 'grab', 'pull-up', 'standing']):
    sheet.paste(shots[key].resize((640, 360)), ((i % 2) * 640, (i // 2) * 360))
sheet.save(target / 'motion-contact-sheet.png')
hashes = json.loads((target / 'formal-source-hashes.json').read_text(encoding='utf-8'))
changed = [path for path, expected in hashes.items() if hashlib.sha256((root / path).read_bytes()).hexdigest() != expected]
assert not changed, f'正式码头资源在预览期间变化：{changed}'
audit = {'scope':'temporary cloned scene, manually sampled skeleton, no formal map or spawn changes', 'frames':304, 'fps':20, 'seconds':15.2, 'width':1280, 'height':720, 'full_decode':'passed', 'formal_assets_checked':len(hashes), 'formal_asset_changes':changed}
(target / 'audit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')
(target / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>第二关海中进场试做</title><style>body{margin:0;background:#12252b;color:#e5f0ee;font:16px system-ui;padding:24px;max-width:1280px;margin:auto}h1{font-size:24px}video,img{width:100%;border-radius:10px}p{color:#bad3d6}figure{margin:24px 0}</style><h1>第二关：右侧通道改为海中爬上码头</h1><p>Unity临时克隆与正式主角同框。移除右侧通道，三只敌人依次靠近、抓边、爬上平台。人工采样候选，非真实对局；正式资源尚未修改。</p><video controls autoplay muted loop playsinline src="candidate-sea-entry.mp4" poster="grab.png"></video><figure><img src="layout-comparison.png"><figcaption>当前布局 / 临时候选</figcaption></figure></html>''', encoding='utf-8')
print(json.dumps(audit, ensure_ascii=False))
print(video)
