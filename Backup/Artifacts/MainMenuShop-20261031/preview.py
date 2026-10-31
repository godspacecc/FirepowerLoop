from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
p=Path(__file__).parent
im=Image.new('RGB',(1500,880),'#101820');d=ImageDraw.Draw(im)
def text(x,y,s,size=24,color='#e5e9ec'):
 d.text((x,y),s,font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',size),fill=color)
def box(rect,color='#263742',border=None):
 d.rounded_rectangle(rect,radius=10,fill=color,outline=border,width=2)
text(45,25,'开始界面与商店 · 布局示意（非游戏实拍）',30)
text(45,80,'新增商店入口；幸存者模式直接开始对局',21,'#a6b7c2')
box((45,145,465,755),'#1c2932')
text(92,182,'FIREPOWER LOOP',28)
for i,s in enumerate(['闯关模式','幸存者模式','商店','设置','退出']):
 y=268+i*83
 box((92,y,418,y+62),'#b6934f' if s=='商店' else '#324653')
 text(184 if s=='商店' else 158,y+13,s,26,'#15212a' if s=='商店' else '#e5e9ec')
text(488,426,'→',36,'#e1bd75')
box((565,145,1455,755),'#23303a')
text(615,183,'商店',36);text(1385,185,'×',30)
d.line((615,250,1405,250),fill='#53646f',width=2)
text(1130,195,'金币  1,200',24,'#f3cd78')
box((615,300,1405,595),'#1a2730')
text(830,423,'商品暂未开放',30,'#adbdc7')
box((1155,655,1405,715),'#354953')
text(1218,668,'返回',25)
text(45,798,'金币余额沿用现有存档；此处数值仅为示例。战斗内本局金币与结算统计继续保留。',22,'#adbdc7')
im.save(p/'preview-top-gold.png')
