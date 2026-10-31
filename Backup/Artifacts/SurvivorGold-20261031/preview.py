from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
p=Path(__file__).parent
im=Image.new('RGB',(1280,800),'#101820'); d=ImageDraw.Draw(im)
font='C:/Windows/Fonts/msyh.ttc'
def txt(x,y,s,size=24,color='#e4e9ec'):
 d.text((x,y),s,font=ImageFont.truetype(font,size),fill=color)
txt(45,30,'幸存者金币 · 布局示意（非游戏实拍）',25)
txt(45,74,'点击开始界面的「幸存者模式」后显示',18,'#9baab4')
d.rounded_rectangle((270,135,1010,625),radius=16,fill='#23303a',outline='#647581',width=2)
txt(325,175,'幸存者模式',36); txt(948,175,'×',30)
d.line((325,240,955,240),fill='#52616d',width=2)
txt(325,280,'持有金币',24,'#adbcc5')
d.ellipse((325,328,375,378),fill='#e5b552',outline='#ffe4a1',width=3)
txt(341,333,'G',26,'#5b4018'); txt(399,315,'1,200',54,'#f3cd78')
txt(325,412,'击败敌人有概率掉落金币，拾取后永久保留。',22)
txt(325,453,'金币将用于后续开放的局外成长。',22,'#adbcc5')
d.rounded_rectangle((325,530,510,585),radius=7,fill='#354651')
txt(392,540,'返回',23)
d.rounded_rectangle((555,530,955,585),radius=7,fill='#c49b4f')
txt(705,540,'开始生存',23,'#182027')
txt(55,667,'战斗显示：本局金币  +1     结算显示：本局获得  12   /   持有金币  1,200',23)
txt(55,716,'示例数值仅演示布局；金币图标为占位。正式掉落拟复用已有金币资源。',19,'#9baab4')
im.save(p/'entry-preview.png')
