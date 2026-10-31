from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
p=Path(__file__).parent
labels=['0 当前主角（对照）','1 男 · 衬衫','2 男 · 西装','3 女 · 商务装','4 女 · 长外套','5 女 · 夹克','6 女 · 警服','7 男 · 连帽衫','8 男 · 夹克','9 男 · 警服']
out=Image.new('RGB',(2000,1390),'#17212c');d=ImageDraw.Draw(out)
font=lambda n:ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',n)
d.text((25,18),'角色候选｜当前正式Prefab临时切换造型 · 同尺寸 / 同镜头 / 同持枪待机',font=font(29),fill='white')
for i,label in enumerate(labels):
 x=i%5*400;y=80+i//5*640
 out.paste(Image.open(p/f'character-{i}.png'),(x,y))
 d.text((x+14,y+599),label,font=font(26),fill='#f0ce8a' if i==0 else 'white')
d.text((25,1360),'静态外观预览；尚未进行跑步、换弹和穿模验收，也未接入购买或角色选择。',font=font(21),fill='#afbdca')
out.save(p/'comparison.png')
