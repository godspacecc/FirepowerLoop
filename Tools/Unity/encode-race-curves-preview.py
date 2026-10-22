from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
src=Path("Client/Temp/RaceCurvesPreview-v4");dst=Path("Docs/Plans/Artifacts/RaceCurves-v4-20261022");dst.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype("C:/Windows/Fonts/msyh.ttc",25);small=ImageFont.truetype("C:/Windows/Fonts/msyh.ttc",18)
def caption(im,title,detail):
 im=im.convert("RGB");out=Image.new("RGB",(im.width,im.height+90),(15,22,28));out.paste(im,(0,90));d=ImageDraw.Draw(out);d.text((22,10),title,font=font,fill="white");d.text((22,48),detail,font=small,fill=(190,207,217));return out
caption(Image.open(src/"race.png"),"竞速路线候选 v4｜连续弯道、回头弯与跳坡","连续轮胎界定通路；主角与车辆体量不变；临时模型，非正式实录").save(dst/"race-v4.png")
frames=[caption(Image.open(p),"竞速动作候选 v4｜沿多弯路线行驶","镜头跟车展示转弯和跳坡；手动轨迹演示，尚非可操控实录") for p in sorted((src/"frames").glob("race-*.png"))]
frames[0].save(dst/"race-v4.gif",save_all=True,append_images=frames[1:],duration=67,loop=0,optimize=False)
print(dst.resolve())
