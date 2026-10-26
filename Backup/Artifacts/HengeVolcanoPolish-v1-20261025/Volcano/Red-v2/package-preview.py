"""只读取Unity采样并添加说明，保存本会话对照页、视频及哈希验证。"""
from pathlib import Path
import hashlib, json, subprocess
from PIL import Image, ImageDraw, ImageFont, ImageChops, ImageStat

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[4]
FFMPEG = next((HERE.parent / '_encoder/imageio_ffmpeg/binaries').glob('ffmpeg*.exe'))
FONT = 'C:/Windows/Fonts/msyh.ttc'
title = ImageFont.truetype(FONT, 27)
small = ImageFont.truetype(FONT, 20)

def pair(before, after, label, out, before_name='现行正式表现（隔离回读）'):
    sheet = Image.new('RGB', (2560, 816), '#17232d')
    d = ImageDraw.Draw(sheet)
    d.text((25, 12), f'{label}｜{before_name}', font=title, fill='#ecf2f5')
    d.text((1305, 12), f'{label}｜红色修订候选（尚未采用）', font=title, fill='#ffd18c')
    sheet.paste(Image.open(HERE / before).convert('RGB'), (0, 55))
    sheet.paste(Image.open(HERE / after).convert('RGB'), (1280, 55))
    d.text((25, 784), '同机位、同灯光、正式主角原尺寸同框；只比较表现，通路／入口／障碍足迹与规则保留。', font=small, fill='#c6d5de')
    sheet.save(HERE / out)

for i in range(1,5):
    pair(f'round-{i}-before.png', f'round-{i}-candidate.png', f'熔火巢穴 · 第{i}轮', f'round-{i}-comparison.png')
pair('close-before.png', 'close-candidate.png', '第一轮近景', 'close-comparison.png')
pair('../close-candidate.png', 'close-candidate.png', '红色修订v2', 'revision-comparison.png', '上一版临时候选（未采用）')

assert FFMPEG.is_file(), FFMPEG
frames = sorted((HERE / 'frames').glob('*.png'))
assert len(frames)==80, len(frames)
video = HERE / 'volcano-glow-preview.mp4'
cmd = [str(FFMPEG),'-y','-hide_banner','-loglevel','error','-f','rawvideo','-pix_fmt','rgb24','-s','1280x800','-r','20','-i','pipe:0','-an','-c:v','libx264','-preset','medium','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(video)]
with subprocess.Popen(cmd, stdin=subprocess.PIPE) as proc:
    for f in frames:
        canvas=Image.new('RGB',(1280,800),'#17232d'); canvas.paste(Image.open(f).convert('RGB'),(0,60))
        d=ImageDraw.Draw(canvas)
        d.text((22,12),'熔火巢穴 · 临时候选辉光预览｜隔离采样，尚未采用',font=title,fill='#ffd18c')
        d.text((22,775),'正式主角原尺寸；熔岩只在障碍顶部／场外，不增加伤害或碰撞。',font=small,fill='#c6d5de')
        proc.stdin.write(canvas.tobytes())
    proc.stdin.close()
    assert proc.wait()==0
decoded=subprocess.run([str(FFMPEG),'-hide_banner','-loglevel','error','-i',str(video),'-f','rawvideo','-pix_fmt','rgb24','pipe:1'],capture_output=True,check=True)
count=len(decoded.stdout)//(1280*800*3)
assert count==80, count
metadata=subprocess.run([str(FFMPEG),'-hide_banner','-i',str(video),'-map','0:v:0','-c','copy','-f','null','-'],capture_output=True,text=True)
(HERE/'video-metadata.txt').write_text(metadata.stderr,encoding='utf-8')

baseline=json.loads((HERE.parent.parent/'before-formal-hashes.json').read_text(encoding='utf-8-sig'))
changed=[]
for rel, sha in baseline.items():
    path=ROOT/rel
    actual=hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else 'MISSING'
    if actual!=sha: changed.append({'path':rel,'before':sha,'after':actual})
hashreport={'checked':len(baseline),'changed':changed,'unchanged':len(baseline)-len(changed)}
(HERE/'formal-hash-verification.json').write_text(json.dumps(hashreport,ensure_ascii=False,indent=2),encoding='utf-8')

images=[]
for path in sorted(HERE.glob('*.png')):
    with Image.open(path) as im:
        im.verify()
    images.append(path.name)
delta=ImageStat.Stat(ImageChops.difference(Image.open(frames[0]),Image.open(frames[20])))
checks={'PNG_verified':len(images),'source_frames':len(frames),'decoded_video_frames':count,'video_duration_seconds':4,'video_fps':20,'video_size':'1280x800','glow_difference_mean_RGB':delta.mean,'hashes':hashreport}
(HERE/'package-verification.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2),encoding='utf-8')

cards=''.join(f'<section><h2>第{i}轮 · 相同机位</h2><a href="round-{i}-comparison.png"><img src="round-{i}-comparison.png"></a><p><a href="round-{i}-before.png">正式原图</a> · <a href="round-{i}-candidate.png">临时候选原图</a></p></section>' for i in range(1,5))
html='''<!doctype html><meta charset="utf-8"><title>熔火巢穴 · 红色修订v2预览</title>
<style>body{background:#15212b;color:#e5edf4;font:17px/1.7 "Microsoft YaHei",sans-serif;margin:32px auto;max-width:1500px;padding:0 24px}h1,h2{color:#ffd18c}img,video{width:100%;height:auto;border-radius:8px}section{margin:35px 0}a{color:#acd5ff}p{max-width:1100px}.tag{background:#633e27;padding:8px 14px;border-radius:7px;display:inline-block}</style>
<h1>熔火巢穴 · 红色修订v2预览</h1><p class="tag">临时候选，尚未采用 · Unity独立临时场景采样 · 非自然对局录像</p>
<p>实际第8关（配置6）的四轮布局。候选把圆球状岩石改成暗红色切面火山岩，撤掉四角立柱，山脚改为自然铺开的岩坡，障碍顶部与场外火山有真正内凹的熔岩口；场内通路保持冷灰色，亮红橙色熔岩只出现在不能走到的障碍顶部和场外。保留四向入口、活动范围与障碍占地，不新增熔岩伤害、碰撞或规则。</p>
<p>左为当前正式表现隔离回读，右为临时候选。相同镜头、灯光和正式主角原尺寸；俯瞰图中的主角在原出生点，近景靠近原有右下障碍，仅用于看清尺寸。</p>
<section><h2>上一版／红色修订v2 · 同机位</h2><a href="revision-comparison.png"><img src="revision-comparison.png"></a><p>按“火山应该是红色的”修正：暗红岩体、亮红橙熔岩，撤掉祭坛式四角立柱。两版均为未采用的临时候选。</p></section>
<section><h2>正式现版／红色候选 · 主角尺寸与凹口</h2><a href="close-comparison.png"><img src="close-comparison.png"></a></section>
<section><h2>4秒表面辉光演示</h2><video controls loop muted playsinline poster="close-candidate.png" src="volcano-glow-preview.mp4"></video><p>固定镜头；只调临时熔岩表面的轻微亮度，没有新增机关或伤害。</p></section>'''+cards+'''<p><a href="geometry-report.txt">四轮足迹与熔岩位置检查</a> · <a href="formal-hash-verification.json">正式资源哈希</a> · <a href="package-verification.json">图片／视频验证</a></p><p>结束状态：预览已交付，等待具体视觉采用。未修改正式资源、运行时代码、配置或服务器；未运行业务测试、Player Build或资源打包。</p>'''
(HERE/'index.html').write_text(html,encoding='utf-8')
print(json.dumps(checks,ensure_ascii=False))
