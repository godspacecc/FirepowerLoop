from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).parent
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',18)
def label(image,title,subtitle):
    out=image.convert('RGB').copy();d=ImageDraw.Draw(out)
    d.rectangle((0,0,out.width,70),fill='#14232e');d.text((20,9),title,font=font,fill='white');d.text((20,43),subtitle,font=small,fill='#b4c6cf')
    d.rectangle((0,out.height-36,out.width,out.height),fill='#14232e');d.text((20,out.height-30),'隔离临时模型与节奏示意 · 非正式接入或战斗实录',font=small,fill='#bac7ce')
    return out
line=label(Image.open(root/'lineup.png'),'主角 / 自爆丧尸 / 法术丧尸 / 盾牌丧尸','相同地面与镜头，原始角色尺寸；新增装备为临时造型候选')
line.save(root/'comparison.png')
for kind,title in enumerate(['自爆丧尸：接近 → 约1秒预警 → 爆炸','法术丧尸：施法预警 → 直线火球','盾牌丧尸：正面持盾 → 展示侧后弱点']):
    frames=[]
    for path in sorted((root/f'frames-{kind}').glob('*.png')):
        frames.append(label(Image.open(path),title,'当前主角同框；动作、移动与特效按预览脚本采样，不裁决实际伤害'))
    frames[0].save(root/f'preview-{kind}.gif',save_all=True,append_images=frames[1:],duration=100,loop=0,optimize=True)
print('comparison.png and three 4-second GIF previews generated')
