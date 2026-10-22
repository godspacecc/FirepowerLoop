from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
src=Path('Client/Temp/RemainingStagesPreview-v3');dst=Path('Docs/Plans/Artifacts/RemainingStages-v3-20261022');dst.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',25);small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',19)
def caption(im,title,detail):
 im=im.convert('RGB');out=Image.new('RGB',(im.width,im.height+92),(15,22,28));out.paste(im,(0,92));d=ImageDraw.Draw(out);d.text((24,12),title,font=font,fill='white');d.text((24,50),detail,font=small,fill=(190,207,217));return out
captions={
'docks':('第二关码头｜项目布局候选 v3','临时模型预览：连续木平台、吊架、停船、水槽；正式主角与 AK 同框'),
'farm':('第三关雪地农场｜项目布局候选 v3','四个约28米区域、门洞和破墙缺口互通；主角与牛保持原体量'),
'race':('第11轮后竞速｜项目布局候选 v3','长宽约为上一版两倍，车辆体量不变；临时轨迹，非驾驶实录'),
'scale':('体量对照｜正式主角 + 临时牛 / 车辆','左：正式主角及 AK　中：临时牛　右：临时车辆；均非正式新资源')}
for name,(a,b) in captions.items():caption(Image.open(src/(name+'.png')),a,b).save(dst/(name+'-v3.png'))
for kind,prefix,title,detail in [('herd','herd','牛群动作候选 v3｜每批只有一排','五头临时牛并排通过两区破墙缺口；体量不变，无新增 HUD'),('race','race','车辆动作候选 v3｜转弯与跳坡','临时轨迹演示，非可操控实录；车轮转动、转向、跃起后落地')]:
 frames=[caption(Image.open(p),title,detail) for p in sorted((src/'frames').glob(prefix+'-*.png'))]
 frames[0].save(dst/(kind+'-v3.gif'),save_all=True,append_images=frames[1:],duration=67,loop=0,optimize=False)
print(dst.resolve())
