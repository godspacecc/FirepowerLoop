"""编码现有单人粒子模板的隔离动态预览。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import sys

root = Path(__file__).resolve().parents[2]
out = root / 'Docs/Plans/Artifacts/OnlineWeaponFeedback-20261023'
out.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
frames = []
refined = '--refined' in sys.argv
source = 'OnlineWeaponFeedbackRefined' if refined else 'OnlineWeaponFeedbackPreview'
for path in sorted((root / f'Client/Temp/{source}/frames').glob('frame-*.png')):
    canvas = Image.new('RGB', (1000, 730), (15, 23, 28))
    canvas.paste(Image.open(path).convert('RGB'), (0, 80))
    draw = ImageDraw.Draw(canvas)
    draw.text((20, 10), '爆炸精简候选：保留橙色主体，去掉外围飞散火星（隔离场景）' if refined else '联机拟复用：现有单人反馈（正式模板克隆，隔离场景录制）', font=font, fill='white')
    draw.text((20, 45), '上方命中与撞墙保持原样  |  左下：现有爆炸  |  右下：去掉外围飞散火星' if refined else '左上：敌人命中  |  右上：撞墙火星  |  左下：普通爆炸  |  右下：小型能量爆炸', font=small, fill=(215, 225, 230))
    frames.append(canvas)
assert len(frames) == 32
name = 'refined-feedback' if refined else 'existing-feedback'
frames[0].save(out / f'{name}.gif', save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
frames[2].save(out / f'{name}.png')
print(out / f'{name}.gif')
