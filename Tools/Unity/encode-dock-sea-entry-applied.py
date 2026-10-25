"""编码正式资源隔离回放，保留原始时间轴，不改游戏资源。"""
import argparse
import json
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', required=True, type=Path)
args = parser.parse_args()
assert args.ffmpeg.is_file(), '请指定本机已安装的编码器'
root = Path(__file__).resolve().parents[2]
folder = root / 'Backup/Artifacts/DockSeaEntryApplied-20261025'
source = folder / 'frames'
main = sorted(source.glob('[0-9][0-9][0-9].png'))
slow = sorted(source.glob('slow-[0-9][0-9][0-9].png'))
assert (len(main), len(slow)) == (180, 100)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 25)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 20)
video = folder / 'applied-sea-entry.mp4'
command = [str(args.ffmpeg), '-y', '-hide_banner', '-loglevel', 'warning', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '1280x720', '-r', '20', '-i', '-', '-an', '-c:v', 'libx264', '-preset', 'medium', '-crf', '19', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(video)]
process = subprocess.Popen(command, stdin=subprocess.PIPE)
shots = {}
for is_slow, files in [(False, main), (True, slow)]:
    for i, path in enumerate(files):
        frame = Image.open(path).convert('RGB')
        d = ImageDraw.Draw(frame)
        d.rectangle((0, 0, 1280, 76), fill='#12252b')
        d.text((640, 8), '码头海中攀爬 v2 · 已接入正式资源', font=font, fill='white', anchor='mt')
        d.text((640, 44), '三种正式丧尸与主角同框 · ' + ('近景四分之一速度' if is_slow else '隔离场景按当前进度采样'), font=small, fill='#afd8d6', anchor='mt')
        t = 3.75 + i / 40 if is_slow else i / 20
        if t < 2.1:
            label = '海中靠近：双腿交替划动，身体下沉并带水纹'
        elif t < 2.7:
            label = '抓边收膝：双手向码头边缘伸出'
        elif t < 3.6:
            label = '撑身抬起：膝盖弯曲，双腿收回身下'
        elif t < 4.45:
            label = '前脚登台：抬膝越边，脚掌落地'
        elif t < 5.15:
            label = '后腿蹬起：前脚支撑，身体抬高'
        elif t < 5.75:
            label = '后脚补步：逐渐站稳，双腿伸展'
        else:
            label = '接回已采用的地面走路动作（本回放按0.9米/秒示范续步）'
        d.rectangle((0, 660, 1280, 720), fill='#12252b')
        d.text((640, 678), label, font=font, fill='#a8e8d0', anchor='mt')
        if not is_slow and i in {28, 50, 67, 87, 103, 130}:
            frame.save(folder / f'keyframe-{i:03}.png')
            shots[i] = frame.copy()
        # 近景每秒40个源帧，以20fps重复两遍：四分之一速度，细看脚掌登台。
        for _ in range(2 if is_slow else 1):
            process.stdin.write(frame.tobytes())
process.stdin.close()
assert process.wait() == 0
subprocess.run([str(args.ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(video), '-f', 'null', '-'], check=True)
sheet = Image.new('RGB', (1280, 1080), '#12252b')
for i, key in enumerate(sorted(shots)):
    sheet.paste(shots[key].resize((640, 360)), ((i % 2) * 640, (i // 2) * 360))
sheet.save(folder / 'contact-sheet.png')
audit = {'source': 'formal prefabs and bound clips, isolated EnemyView sampling, not natural battle or live dual-client session', 'main_source_frames': 180, 'slow_source_frames': 100, 'encoded_frames': 380, 'fps': 20, 'seconds': 19, 'full_decode': 'passed', 'width': 1280, 'height': 720}
(folder / 'video-audit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')
(folder / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>码头海中攀爬v2正式接入</title><style>body{margin:24px auto;max-width:1280px;background:#12252b;color:#e5f0ee;font:16px system-ui}video,img{width:100%}p{color:#bad3d6}</style><h1>码头海中攀爬v2 · 已接入</h1><p>正式地图与三种丧尸、主角同框。隔离场景按正式EnemyView与权威年龄采样；末尾近景四分之一速度。站稳后的0.9米/秒为参考续步；真实追击、受击与联机状态另有入口检查，此视频不冒充自然对局录像。</p><video controls muted loop playsinline src="applied-sea-entry.mp4" poster="keyframe-067.png"></video><img src="contact-sheet.png"></html>''', encoding='utf-8')
print(json.dumps(audit, ensure_ascii=False))
print(video)
