from pathlib import Path
from PIL import Image, ImageChops
import json
root=Path(__file__).parent
rows=[]
for applied in sorted(root.glob('*/*-applied.png')):
    accepted=applied.with_name(applied.name.replace('-applied','-accepted'))
    a=Image.open(applied).convert('RGB'); b=Image.open(accepted).convert('RGB')
    diff=ImageChops.difference(a,b); pixels=list(diff.getdata())
    changed=sum(p!=(0,0,0) for p in pixels)
    rows.append(dict(image=str(applied.relative_to(root)),size=list(a.size),changedPixels=changed,maxChannelDifference=max(max(p) for p in pixels)))
(root/'pixel-comparison.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(rows,ensure_ascii=False,indent=2))
