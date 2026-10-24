"""比较采用版候选与正式资源回读，输出逐项差异及正式对照图。"""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont
import hashlib,json

root=Path(__file__).resolve().parents[2]
candidate=root/'Client/Temp/AllExplosions-v1-20261023'
applied=root/'Client/Temp/AllExplosionsApplied-20261024'
out=root/'Backup/Artifacts/AllExplosionsApplied-20261024'
out.mkdir(parents=True,exist_ok=True)
names=['energy','barrel','bomb','drone','cluster-main','cluster-child','enemy-death','decoy','orbit-barrel']
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',20)
report=[]
poster=Image.new('RGB',(1000,len(names)*385),(14,23,26))
for row,name in enumerate(names):
    exact=0;worst=0;total_pixels=600*420
    hashes=[]
    for frame in range(48):
        a=candidate/name/f'1-{frame:03}.png';b=applied/name/f'1-{frame:03}.png'
        assert a.exists() and b.exists(),(a,b)
        with Image.open(a) as before,Image.open(b) as after:
            diff=ImageChops.difference(before.convert('RGB'),after.convert('RGB'))
            red,green,blue=diff.split();maximum=ImageChops.lighter(ImageChops.lighter(red,green),blue)
            changed=sum(maximum.histogram()[4:])
            if diff.getbbox() is None:exact+=1
            worst=max(worst,changed/total_pixels)
        hashes.append(hashlib.sha256(b.read_bytes()).hexdigest())
    report.append(dict(name=name,frames=48,exact_frames=exact,max_changed_fraction=round(worst,6),sha256=hashes))
    tile=Image.new('RGB',(1000,385),(14,23,26));draw=ImageDraw.Draw(tile)
    draw.text((12,8),f'{name} | 左：已采用候选  右：正式回读',font=font,fill='white')
    for col,base in enumerate([candidate,applied]):
        image=Image.open(base/name/'1-006.png').convert('RGB').resize((500,350),Image.Resampling.LANCZOS);tile.paste(image,(col*500,35))
    poster.paste(tile,(0,row*385))
    print(f'{name}: {exact}/48 exact, maximum >3/255 changed pixels {worst:.3%}',flush=True)
poster.save(out/'comparison.png')
(out/'frame-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(out,flush=True)
