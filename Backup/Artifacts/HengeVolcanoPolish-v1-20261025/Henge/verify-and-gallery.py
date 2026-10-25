"""只整理巨石环阵预览证据；不修改Unity工程或其它会话输出。"""
from pathlib import Path
import hashlib, json
from PIL import Image, ImageDraw, ImageFont, ImageStat

ROOT = Path(__file__).resolve().parents[4]
OUT = Path(__file__).resolve().parent
BASE = OUT.parent
font_path = 'C:/Windows/Fonts/msyh.ttc'
font = ImageFont.truetype(font_path, 26)
small = ImageFont.truetype(font_path, 19)

records = []
for path in sorted(OUT.glob('round-*.png')):
    with Image.open(path) as im:
        im.load()
        assert im.size == (1600, 1200), (path.name, im.size)
        assert max(ImageStat.Stat(im).stddev) > 20, path.name
        records.append({'file': path.name, 'size': list(im.size), 'decoded': True,
                        'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
assert len(records) == 11, len(records)

def pair(before, after, name, title):
    sheet = Image.new('RGB', (1920, 852), '#17222b')
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 12), title, fill='#f2e6c9', font=font)
    draw.text((24, 52), '左：当前正式资源实拍    右：临时候选（未接入）', fill='#d4dfdc', font=small)
    for x, source in [(0, before), (960, after)]:
        with Image.open(OUT / source) as im:
            sheet.paste(im.resize((960, 720), Image.Resampling.LANCZOS), (x, 90))
    draw.text((24, 817), '隔离采样 · 同机位与光照 · 正式主角原尺寸同框 · 四轮布局和规则保持', fill='#aebdb9', font=small)
    sheet.save(OUT / name)

for n in range(1, 5):
    pair(f'round-{n}-before.png', f'round-{n}-candidate.png', f'round-{n}-compare.jpg', f'巨石环阵 · 第 {n} 轮全景对照')
pair('round-1-close-before.png', 'round-1-close-candidate.png', 'round-1-close-compare.jpg', '巨石环阵 · 首轮近景与正式主角比例')

before = json.loads((BASE / 'before-formal-hashes.json').read_text(encoding='utf-8-sig'))
changed = []
for relative, old in before.items():
    path = ROOT / relative
    now = hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None
    if now != old: changed.append({'path': relative, 'before': old, 'after': now})
report = {'image_count': len(records), 'images': records, 'formal_file_count': len(before),
          'formal_hash_mismatch_count': len(changed), 'formal_hash_mismatches': changed,
          'video_required': False, 'business_tests_run': False,
          'scope': '静态四轮场景临时候选，具体视觉已确认采用但尚未接入，非自然对局或双端验证'}
(OUT / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

parts = ['''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>巨石环阵 · 四轮临时预览</title><style>body{font:17px/1.7 system-ui, sans-serif;background:#17222b;color:#e4e9e3;max-width:1600px;margin:28px auto;padding:0 20px}h1{color:#e6d9ad}img{width:100%;height:auto;border-radius:5px}section{margin:36px 0}a{color:#9ed5d0}.note{padding:18px;background:#26343c;border-left:4px solid #b2bb84}figure{margin:12px 0}small{color:#adbdbb}</style><h1>巨石环阵 · 第7关四轮预览</h1><div class="note">临时候选，已于2026-10-25确认采用，待总会话统一正式接入；图片来自 Unity 独立临时场景的实际渲染，并非概念图或自然对局。左为当前正式环境，右为候选；两侧相同机位、光照和正式主角原尺寸。<br>预计效果：方块墙换成有破损切面、断梁和顶面青苔的巨石遗迹；平地增加柔和草土变化与低草。石门底部保留连续石基，仍按原障碍绕行；中心与四向通路保持开阔。</div>''']
for n in range(1, 5):
    parts.append(f'<section><h2>第 {n} 轮</h2><a href="round-{n}-compare.jpg"><img src="round-{n}-compare.jpg" alt="第{n}轮同机位对照"></a><small><a href="round-{n}-before.png">正式原图</a> · <a href="round-{n}-candidate.png">候选原图</a></small></section>')
parts.append('''<section><h2>首轮近景：主角原尺寸</h2><a href="round-1-close-compare.jpg"><img src="round-1-close-compare.jpg"></a><a href="round-1-close-before.png">正式近景原图</a> · <a href="round-1-close-candidate.png">候选近景原图</a></section><section><h2>背面几何与支撑检查</h2><a href="round-1-back-check.png"><img src="round-1-back-check.png"></a><small>补光用于辨认背面闭合与石梁支撑；正式与候选使用同组预览光照。外圈地标位于活动范围外，深色背景用于隔离展示。</small></section><section><h2>验证证据</h2><p><a href="layout-footprint-checks.tsv">逐轮参数与障碍投影检查</a> · <a href="verification.json">图片完整解码与正式资源SHA256</a> · <a href="README.md">范围与验证说明</a></p><p>四轮原始配置直接回读，39×39米活动范围；逐顶点投影包围盒与配置一致，候选碰撞体为0。未改配置、伤害、人数、计时和单双人规则；未运行游戏业务测试、未打包。</p></section></html>''')
(OUT / 'index.html').write_text('\n'.join(parts), encoding='utf-8')
print(json.dumps({'images': len(records), 'formal_files': len(before), 'mismatches': len(changed)}, ensure_ascii=False))
if changed: print(json.dumps(changed[:6], ensure_ascii=False))
