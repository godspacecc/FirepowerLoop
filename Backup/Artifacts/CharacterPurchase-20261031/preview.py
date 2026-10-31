from pathlib import Path
from PIL import Image,ImageDraw,ImageFont,ImageEnhance
p=Path(__file__).parent
portraits=p.parent/'CharacterShopPreview-20261031'
font=lambda n:ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',n)
names={0:'默认角色',1:'男 · 衬衫',7:'男 · 连帽衫',3:'女 · 商务装',4:'女 · 长外套',9:'男 · 警服'}
prices={1:50,7:100,3:150,4:200,9:300}
for mode in ['shop','select']:
 im=Image.new('RGB',(1500,850),'#121c26');d=ImageDraw.Draw(im)
 def text(x,y,s,size=26,color='#e8edf1'):d.text((x,y),s,font=font(size),fill=color)
 def box(r,c,outline=None):d.rounded_rectangle(r,radius=9,fill=c,outline=outline,width=3)
 text(35,20,'商店角色购买 · 布局预览' if mode=='shop' else '幸存者开局选择 · 布局预览',30)
 text(35,65,'角色为现有模型实拍；界面为示意，余额和已拥有状态仅演示。',19,'#a7b8c5')
 box((25,112,1475,790),'#23303a')
 text(65,140,'商店' if mode=='shop' else '选择角色',35)
 text(1190,148,'金币  1,200',24,'#f1ca75');text(1410,142,'×',28)
 ids=[1,7,3,4,9] if mode=='shop' else [0,1,7,3,4,9]
 width=260 if mode=='shop' else 215;gap=16;start=68 if mode=='shop' else 65
 for j,i in enumerate(ids):
  x=start+j*(width+gap);selected=mode=='select' and i==0;locked=mode=='select' and i not in [0,1,7]
  box((x,215,x+width,655),'#192630','#e6bc62' if selected else '#3d515e')
  photo=Image.open(portraits/f'character-{i}.png').convert('RGB').resize((190,285))
  if locked:photo=ImageEnhance.Brightness(ImageEnhance.Color(photo).enhance(0)).enhance(.55)
  im.paste(photo,(x+(width-190)//2,225))
  text(x+18,522,names[i],24)
  label=('已拥有' if i==1 else f'{prices[i]} 金币 · 购买') if mode=='shop' else ('已选择' if selected else ('未解锁' if locked else '选择'))
  c='#a48242' if (mode=='shop' and i!=1) or selected else '#354955'
  box((x+12,582,x+width-12,636),c)
  text(x+24,594,label,22)
 text(65,712,'购买后永久解锁，仅改变外观。' if mode=='shop' else '未解锁角色可在商店购买。',21,'#b0c0cb')
 if mode=='select':
  box((1000,700,1170,758),'#354955');text(1053,714,'返回',24)
  box((1190,700,1435,758),'#b7944e');text(1248,714,'开始生存',24,'#14202a')
 else:
  box((1190,700,1435,758),'#354955');text(1278,714,'返回',24)
 text(35,811,'流程：商店解锁 → 点击幸存者模式 → 选择已拥有角色 → 开始对局',21,'#b0c0cb')
 im.save(p/f'{mode}-preview.png')
