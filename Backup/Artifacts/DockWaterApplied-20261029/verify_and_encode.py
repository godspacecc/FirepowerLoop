"""核对正式水面回读与采用候选，并按真实拍摄间隔编码；不改Unity资源。"""
import argparse
import csv
import json
import subprocess
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageStat

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', required=True, type=Path)
args = parser.parse_args()
folder = Path(__file__).resolve().parent
assert '完成' in (folder/'capture-status.txt').read_text(encoding='utf-8-sig')
rows = list(csv.DictReader((folder/'times.csv').open(encoding='utf-8-sig')))
assert len(rows) == 80 and all(float(r['clockSeconds']) > 0 for r in rows)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)

def difference(a, b):
    with Image.open(a) as x, Image.open(b) as y:
        diff = ImageChops.difference(x.convert('RGB'), y.convert('RGB'))
        return sum(ImageStat.Stat(diff).mean)/3

def pair(reference, formal, title):
    image = Image.new('RGB', (1280, 450), '#14272d')
    for x, path in [(0, reference), (640, formal)]:
        with Image.open(path) as source:
            image.paste(source.convert('RGB').resize((640, 360), Image.Resampling.LANCZOS), (x, 52))
    draw = ImageDraw.Draw(image)
    draw.text((18, 12), '已采用候选 · 同时刻参考', font=font, fill='#c2ded8')
    draw.text((658, 12), '正式资源回读 · Unity时钟', font=font, fill='#e0e8e9')
    draw.text((20, 420), title+' / 同机位同灯光 / 水面未替换为临时材质', font=small, fill='#b1c8cd')
    return image

report = {'frames': 80, 'clock': 'URP editor realtimeSinceStartup / in-game _Time.y', 'shots': []}
video_frames = folder/'encoded-frames'
video_frames.mkdir(exist_ok=True)
concat = []
duration = 0
for shot, prefix in enumerate(['overview', 'close']):
    selected = [r for r in rows if int(r['shot']) == shot]
    errors = []
    shot_duration = float(selected[-1]['clockSeconds'])-float(selected[0]['clockSeconds'])
    for i, row in enumerate(selected):
        frame = int(row['frame'])
        formal = folder/'frames'/f'{shot}-formal-{frame:03}.png'
        reference = folder/'frames'/f'{shot}-reference-{frame:03}.png'
        errors.append(difference(formal, reference))
        output = video_frames/f'{shot}-{frame:03}.png'
        pair(reference, formal, '全场' if shot == 0 else '岸边近景').save(output)
        delta = (float(selected[i+1]['clockSeconds'])-float(row['clockSeconds'])) if i < 39 else shot_duration/39
        assert 0 < delta < 3
        duration += delta
        concat += [f"file '{output.as_posix()}'", f'duration {delta:.6f}']
    dynamic = difference(folder/'frames'/f'{shot}-formal-000.png', folder/'frames'/f'{shot}-formal-039.png')
    translated = difference(folder/f'{prefix}-formal.png', folder/f'{prefix}-translated.png')
    assert max(errors) < 1, f'{prefix} 与采用候选差异过大: {max(errors)}'
    assert dynamic > .3, f'{prefix} 未出现可测动态: {dynamic}'
    assert translated < 1, f'{prefix} 平移后岸距/水纹错位: {translated}'
    report['shots'].append({'name': prefix, 'referenceMeanError': sum(errors)/40, 'referenceMaxFrameError': max(errors), 'dynamicMeanDifference': dynamic, 'translationMeanDifference': translated, 'realSeconds': shot_duration})
    pair(folder/f'{prefix}-reference.png', folder/f'{prefix}-formal.png', '全场' if shot == 0 else '岸边近景').save(folder/f'{prefix}-comparison.png')
concat.append(f"file '{output.as_posix()}'")
(folder/'concat.txt').write_text('\n'.join(concat)+'\n', encoding='utf-8')
video = folder/'water-applied.mp4'
subprocess.run([str(args.ffmpeg), '-y', '-hide_banner', '-loglevel', 'warning', '-f', 'concat', '-safe', '0', '-i', str(folder/'concat.txt'), '-fps_mode', 'vfr', '-an', '-c:v', 'libx264', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(video)], check=True)
subprocess.run([str(args.ffmpeg), '-v', 'error', '-i', str(video), '-f', 'null', '-'], check=True)
report.update({'video': video.name, 'durationSeconds': duration, 'fullDecodePassed': True})
(folder/'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
contact = Image.new('RGB', (1280, 450*4), '#14272d')
for y, (shot, frame) in enumerate([(0, 0), (0, 39), (1, 0), (1, 39)]):
    with Image.open(video_frames/f'{shot}-{frame:03}.png') as source:
        contact.paste(source, (0, y*450))
contact.save(folder/'motion-contact-sheet.png')
(folder/'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>正式码头水体回读</title><style>body{max-width:1280px;margin:24px auto;padding:0 20px;background:#14272d;color:#e0e8e9;font:16px system-ui}video,img{width:100%;border-radius:8px}</style><h1>正式码头水体回读</h1><p>左为已采用候选的同时刻参考，右为正式Prefab材质。使用Unity真实编辑器时钟，视频按测得间隔播放；先全场后近景。隔离环境回读，非真实对局录像。</p><video controls muted loop playsinline src="water-applied.mp4" poster="close-comparison.png"></video><h2>全场</h2><img src="overview-comparison.png"><h2>近景</h2><img src="close-comparison.png"></html>''', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
