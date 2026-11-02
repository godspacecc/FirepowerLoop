from PIL import Image
from pathlib import Path
p=Path('Backup/Artifacts/EnchantZombieImplementation-20261102/stageB')
for k in range(3):
 frames=[Image.open(f).convert('RGB') for f in sorted((p/f'sample-{k}').glob('*.png'))]
 frames[0].save(p/f'formal-{k}.gif',save_all=True,append_images=frames[1:],duration=100,loop=0,optimize=False)
 im=Image.open(p/f'formal-{k}.gif');print(k,im.n_frames,im.size)
