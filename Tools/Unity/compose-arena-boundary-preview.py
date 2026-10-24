from pathlib import Path
import argparse
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser(description='拼接Unity隔离实拍；不编辑游戏资源。')
parser.add_argument('--applied', action='store_true', help='比较已保存的修复前画面与正式修复后的画面')
applied = parser.parse_args().applied
folder = Path(__file__).resolve().parents[2] / 'Backup/Artifacts/ArenaBoundary-v1-20261023'
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 24)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 19)
names = {i:f'地图 {i}' for i in range(1,23)}
names.update({1:'海岛',5:'巨石阵',21:'第二关：码头',22:'第三关：农场'})

def row(arena, width=720):
    height = round(width * .76)
    result = Image.new('RGB', (width*2, height+50), '#111c24')
    d = ImageDraw.Draw(result)
    d.text((18,10), f'{names[arena]} · 现状', font=font, fill='white')
    d.text((width+18,10), '正式修复后' if applied else '临时候选 · 隐藏旧外墙与底板', font=font, fill='#9ee0b1')
    paths = [folder/f'{arena}-before.png', folder/'Applied'/f'{arena}-applied.png' if applied else folder/f'{arena}-candidate.png']
    for i, path in enumerate(paths):
        with Image.open(path) as image:
            result.paste(image.resize((width,height),Image.Resampling.LANCZOS),(i*width,50))
    return result

focus = [row(arena) for arena in [21,22,5]]
sheet = Image.new('RGB',(1440,sum(im.height for im in focus)+66),'#111c24')
draw=ImageDraw.Draw(sheet)
draw.text((18,12),'Unity 正式修复实拍｜左：修复前，右：实际运行代码的修复后' if applied else 'Unity 隔离场景实拍对比｜仅为候选，尚未修改正式代码或资源',font=font,fill='white')
y=66
for im in focus:
    sheet.paste(im,(0,y)); y+=im.height
sheet.save(folder/('applied-comparison.png' if applied else 'comparison.png'))

thumbs = [row(arena,360) for arena in range(1,23)]
overview=Image.new('RGB',(1440,66+11*thumbs[0].height),'#111c24')
ImageDraw.Draw(overview).text((18,12),'22 个主题总览｜每格左：修复前，右：正式修复后' if applied else '22 个主题总览｜每格左为现状，右为临时候选；内部障碍保留',font=font,fill='white')
for i,im in enumerate(thumbs): overview.paste(im,((i%2)*720,66+(i//2)*im.height))
overview.save(folder/('applied-all-themes.png' if applied else 'all-themes.png'))
print(folder/('applied-comparison.png' if applied else 'comparison.png'))
