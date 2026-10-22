from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
src=Path('Client/Temp/RemainingStagesPreview-v2');dst=Path('Docs/Plans/Artifacts/RemainingStages-v2-20261022');dst.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',25);small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',19)
def caption(im,title,detail):
 im=im.convert('RGB');out=Image.new('RGB',(im.width,im.height+92),(15,22,28));out.paste(im,(0,92));d=ImageDraw.Draw(out);d.text((24,12),title,font=font,fill='white');d.text((24,50),detail,font=small,fill=(190,207,217));return out
captions={
'docks':('第二关码头｜项目布局候选 v2','临时模型预览：连续木平台、吊架、停船、水槽；正式主角与 AK 同框'),
'farm':('第三关雪地农场｜项目布局候选 v2','临时模型预览：积雪、草料、围栏、边缘建筑；中央留出躲避空间'),
'race':('第11轮后竞速｜项目布局候选 v2','临时模型预览：岩壁、轮胎夹道、转弯、跳坡；结束后回农场第12轮'),
'scale':('体量对照｜正式主角 + 临时牛 / 车辆','左：正式主角及 AK　中：临时牛　右：临时车辆；均非正式新资源')}
for name,(a,b) in captions.items():caption(Image.open(src/(name+'.png')),a,b).save(dst/(name+'-v2.png'))
for kind,prefix,title,detail in [('herd','herd','牛群动作候选 v2｜身体起势后直线穿场','临时动作研究；主角侧向躲避；没有新增地面警告或方向 HUD'),('race','race','车辆动作候选 v2｜转弯与跳坡','临时轨迹演示，非可操控实录；车轮转动、转向、跃起后落地')]:
 frames=[caption(Image.open(p),title,detail) for p in sorted((src/'frames').glob(prefix+'-*.png'))]
 frames[0].save(dst/(kind+'-v2.gif'),save_all=True,append_images=frames[1:],duration=67,loop=0,optimize=False)
print(dst.resolve())
