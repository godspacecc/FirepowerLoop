"""仅组合本批Unity隔离渲染结果；无需修改Unity工程。"""
import argparse
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', required=True, type=Path)
args = parser.parse_args()
assert args.ffmpeg.is_file(), '需明确指定已安装ffmpeg'
folder = Path(__file__).resolve().parent
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)

def pair(left, right, shot):
    # 保持原始图像纵横比；两个640x360窗口在1280画面上并排。
    result = Image.new('RGB', (1280, 450), '#14272d')
    for x, source in [(0, left), (640, right)]:
        with Image.open(source) as image:
            result.paste(image.convert('RGB').resize((640, 360), Image.Resampling.LANCZOS), (x, 52))
    draw = ImageDraw.Draw(result)
    draw.text((18, 12), '当前正式水面', font=font, fill='#e0e8e9')
    draw.text((658, 12), '候选预览 · 未接入正式资源', font=font, fill='#b4dfd5')
    draw.text((20, 420), shot + ' / 相同灯光、相同机位 / 慢水纹、青蓝深浅、克制岸边泡沫', font=small, fill='#b1c8cd')
    return result

for shot, prefix in [('全场', 'overview'), ('岸边近景', 'close')]:
    pair(folder/f'{prefix}-current.png', folder/f'{prefix}-candidate.png', shot).save(folder/f'{prefix}-comparison.png')

cmd = [str(args.ffmpeg), '-y', '-hide_banner', '-loglevel', 'warning', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '1280x450', '-r', '20', '-i', '-', '-an', '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(folder/'water-comparison.mp4')]
process = subprocess.Popen(cmd, stdin=subprocess.PIPE)
try:
    for shot in range(2):
        for frame in range(80):
            left = folder/'frames'/f'{shot}-current-{frame:03}.png'
            right = folder/'frames'/f'{shot}-candidate-{frame:03}.png'
            process.stdin.write(pair(left,right,'全场' if shot==0 else '岸边近景').tobytes())
finally:
    process.stdin.close()
assert process.wait()==0, '视频编码失败'
(folder/'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>第二关码头水体候选</title><style>body{max-width:1280px;margin:24px auto;padding:0 20px;background:#14272d;color:#e0e8e9;font:16px system-ui}video,img{width:100%;border-radius:8px}p{color:#b1c8cd}</style><h1>第二关码头水体候选</h1><p>左侧为当前正式资源克隆，右侧为临时候选。码头布局、灯光与镜头一致；正式资源尚未修改。候选减少刺眼高光，水纹缓慢流动，平台边青绿、远水深蓝，并增加少量断续泡沫。</p><video controls autoplay muted loop playsinline src="water-comparison.mp4" poster="close-comparison.png"></video><h2>全场对照</h2><img src="overview-comparison.png"><h2>岸边近景对照</h2><img src="close-comparison.png"><p>Unity隔离场景按确定时间轴渲染，非真实对局录像。岸线深浅由正式平台网格提取；不改变碰撞、地图或敌人规则。</p></html>''',encoding='utf-8')
print(folder/'water-comparison.mp4')
