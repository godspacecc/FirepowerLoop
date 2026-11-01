from PIL import Image,ImageDraw
from pathlib import Path
root=Path('Backup/Artifacts/SpecialZombieImplementation-20261101/stage2')
for k in range(3):
 frames=[]
 for p in sorted((root/f'sample-{k}').glob('*.png')):
  im=Image.open(p).convert('RGB');d=ImageDraw.Draw(im);d.rectangle((0,0,960,40),fill=(10,15,22));d.text((16,12),'FORMAL PREFABS / REAL SYSTEM + VIEW PORTS / CONTROLLED 0.1s STEP SAMPLE',fill='white');frames.append(im)
 frames[0].save(root/f'attack-{k}.gif',save_all=True,append_images=frames[1:],duration=100,loop=0)
