from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
import math
out=Path('Backup/Artifacts/EnemyOutsideEntry-20261030')
f=lambda n:ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',n)
frames=[]
for frame in range(120):
 t=frame/12
 im=Image.new('RGB',(960,540),'#101820');d=ImageDraw.Draw(im)
 d.text((30,18),'场外入场与提前受击',font=f(27),fill='white')
 d.text((30,58),'规则动态示意 · 非游戏实拍 · 圆点代表角色，不表示正式尺寸',font=f(16),fill='#aebfc9')
 d.rounded_rectangle((24,105,936,445),radius=14,fill='#263e38')
 d.rectangle((330,105,936,445),fill='#375242')
 for y in range(110,440,22):d.line((330,y,330,y+11),fill='#cdd9b2',width=3)
 d.text((65,115),'场外接近区域',font=f(20),fill='#dce6dc')
 d.text((355,115),'战场边界 / 入口',font=f(20),fill='#e8efc5')
 d.text((747,115),'战场内部',font=f(20),fill='#dce6dc')
 px,py=770,285
 d.ellipse((px-18,py-18,px+18,py+18),fill='#64c9f2')
 d.text((px-36,py+25),'玩家',font=f(18),fill='white')
 for lane,(y,delay) in enumerate([(205,0),(295,1.0),(385,2.0)]):
  age=t-delay;x=-45+age*78
  if x<30 or x>720:continue
  shot=2.4 if lane==0 else 3.6 if lane==1 else -1
  dead=lane==0 and t>shot+.2
  if dead:
   x=-45+(shot-delay)*78
   if t<shot+1.4:
    d.line((x-14,y-14,x+14,y+14),fill='#ffcfa0',width=4);d.line((x-14,y+14,x+14,y-14),fill='#ffcfa0',width=4)
    d.text((x-25,y+23),'场外击杀',font=f(17),fill='#ffdfbc')
   continue
  c='#ffedd0' if shot>0 and 0<t-shot<.25 else '#eb9660'
  d.ellipse((x-15,y-15,x+15,y+15),fill=c)
  d.rectangle((x-21,y-26,x+21,y-22),fill='#1b292a')
  hp=.4 if lane==1 and t>shot else 1
  d.rectangle((x-21,y-26,x-21+42*hp,y-22),fill='#b4ef87')
  if shot>0 and -.18<t-shot<.18:
   d.line((px-20,py,x,y),fill='#ffec88',width=3)
  if lane==1 and shot<t<shot+1.5:d.text((x-18,y+20),'场外受伤',font=f(17),fill='#ffdfbc')
 caption='敌人从远处接近，经过场外区域后进入战场'
 if 2.1<t<4.7:caption='还未跨过入口，就可以命中、扣血或击杀'
 elif t>=4.7:caption='存活敌人连续走入战场，不在入口突然生成'
 d.text((30,462),caption,font=f(23),fill='white')
 d.text((30,503),'统一覆盖：单人闯关 / 双人联机 / 幸存者　　保留正常射程与实体遮挡',font=f(16),fill='#aebfc9')
 frames.append(im)
frames[0].save(out/'entry-rule-preview.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,optimize=True)
frames[45].save(out/'entry-rule-still.png')
print(out.resolve()/'entry-rule-preview.gif')
