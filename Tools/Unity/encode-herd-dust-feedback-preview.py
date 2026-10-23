"""为隔离牛群候选合成落蹄、刨地鼻息和闷撞声，同步编码；不写正式音频资源。"""
from pathlib import Path
import csv
import json
import subprocess
import sys
import wave
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Client/Temp/ElectricTowerFeedbackTools'))
import imageio_ffmpeg

APPLIED = '--applied' in sys.argv
SOURCE = ROOT / ('Client/Temp/HerdDustFeedbackApplied-v1-20261023' if APPLIED else 'Client/Temp/HerdDustFeedback-v1-20261023')
OUTPUT = ROOT / ('Docs/Plans/Artifacts/HerdFeedbackApplied-20261023' if APPLIED else 'Docs/Plans/Artifacts/HerdDust-Unity-v1-20261023')
OUTPUT.mkdir(parents=True, exist_ok=True)
RATE, FPS, DURATION = 44100, 20, 11.6
random = np.random.default_rng(2310237)


def band_noise(seconds, low, high):
    """固定种子带限噪声，仅作本次可审听的独立候选音色。"""
    count = round(seconds * RATE)
    frequencies = np.fft.rfftfreq(count, 1 / RATE)
    spectrum = np.fft.rfft(random.standard_normal(count))
    signal = np.fft.irfft(spectrum * ((frequencies >= low) & (frequencies <= high)), n=count)
    return signal / max(1e-9, np.sqrt(np.mean(signal ** 2)))


def shape(signal, peak, attack=.003, release=.025):
    signal = signal.copy()
    a, b = round(attack * RATE), round(release * RATE)
    signal[:a] *= np.linspace(0, 1, a)
    signal[-b:] *= np.linspace(1, 0, b)
    return signal * peak / max(1e-9, np.max(np.abs(signal)))


def hoof(pitch):
    """低频蹄重、木质短敲和微弱土砂；避免长低音或高频电声。"""
    t = np.arange(round(.22 * RATE)) / RATE
    weight = np.sin(2 * np.pi * (pitch * t - 26 * t ** 2)) * np.exp(-t * 33)
    knock = band_noise(.22, 550, 2300) * np.exp(-t * 65)
    soil = band_noise(.22, 1300, 6200) * np.exp(-t * 18)
    signal = .7 * weight + .30 * knock + .075 * soil
    # 同次对角落蹄错开18毫秒，仍作为一组有重量的步点。
    offset = round(.018 * RATE)
    signal[offset:] += signal[:-offset].copy() * .48
    return shape(signal, .38)


hooves = [hoof(pitch) for pitch in (110, 124, 101, 117)]
t = np.arange(round(.32 * RATE)) / RATE
scrape = shape(band_noise(.32, 300, 4200) * np.sin(np.pi * t / .32) ** 2, .18, .012, .04)
t = np.arange(round(.42 * RATE)) / RATE
snort = shape((band_noise(.42, 200, 1250) + .13 * np.sin(2 * np.pi * 105 * t)) * np.sin(np.pi * t / .42) ** 2,
              .15, .04, .09)
t = np.arange(round(.35 * RATE)) / RATE
hit = shape(.8 * np.sin(2 * np.pi * (82 * t - 35 * t ** 2)) * np.exp(-t * 22)
            + .42 * band_noise(.35, 160, 2200) * np.exp(-t * 31), .66, .001, .035)


def save(name, audio, folder=OUTPUT):
    with wave.open(str(folder / name), 'wb') as handle:
        handle.setnchannels(1 if audio.ndim == 1 else audio.shape[1])
        handle.setsampwidth(2)
        handle.setframerate(RATE)
        handle.writeframes((np.clip(audio, -.99, .99) * 32767).astype('<i2').tobytes())


save('hoof-candidate.wav', hooves[0])
save('scrape-candidate.wav', scrape)
save('snort-candidate.wav', snort)
save('impact-candidate.wav', hit)
if '--apply-confirmed-audio' in sys.argv:
    # 用户采用整段有声候选后，逐样本复用相同音色；不做二次响度或音高加工。
    destination = ROOT / 'Client/Assets/Game/Audio/Sound'
    for index, signal in enumerate(hooves):
        save('ArcadeHerdHoof' + (str(index + 1) if index else '') + '.wav', signal, destination)
    for name, signal in [('Scrape', scrape), ('Snort', snort), ('Impact', hit)]:
        save('ArcadeHerd' + name + '.wav', signal, destination)
    print('Persisted 4 approved hoof variants plus scrape, snort and impact; no configuration changed.')
    sys.exit(0)
mix = np.zeros((round(DURATION * RATE), 2))


def add(signal, time, gain, pan=0):
    """离线模拟远近与左右变化；这不是正式AudioSystem或真人联机录音。"""
    offset = round(time * RATE)
    count = min(len(signal), len(mix) - offset)
    if count <= 0:
        return
    left, right = np.sqrt((1 - pan) / 2), np.sqrt((1 + pan) / 2)
    mix[offset:offset + count] += signal[:count, None] * gain * np.array([left, right])


events = list(csv.DictReader((SOURCE / 'events.csv').open(encoding='utf-8-sig')))
gains = []
for event in events:
    time, x, cow, kind = float(event['time']), float(event['x']), int(event['cow']), int(event['kind'])
    if time < 6.4:
        distance = np.hypot(x + 13, 3)
        gain = max(0, 1 - distance / 24) / (1 + (distance / 9) ** 2)
        pan = float(np.clip((x + 13) / 18, -.8, .8))
    else:
        gain, pan = .75, 0
    if kind == 0:
        # 撞击瞬间留出短空隙，避免蹄声遮住需要审听的闷撞。
        if abs(time - 9.1) < .09:
            gain *= .35
        add(hooves[(cow + int(event['pair'])) % len(hooves)], time, gain, pan)
        gains.append((time, gain))
    elif kind == 1:
        add(scrape, time, .7, -.35)
    elif kind == 2:
        add(hit, time, .95)
    else:
        add(snort, time, .8, pan)
if not APPLIED:
    add(snort, .29, .8, -.3)
mix *= min(1, .90 / max(1e-9, np.max(np.abs(mix))))
# 结束淡出，避免预览停止时切断最后一声蹄音。
mix[-round(.12 * RATE):] *= np.linspace(1, 0, round(.12 * RATE))[:, None]
save('preview-mix.wav', mix)

files = sorted((SOURCE / 'frames').glob('[0-9][0-9][0-9].png'))
assert len(files) == 232 and all(path.name == f'{i:03}.png' for i, path in enumerate(files))
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 24)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 17)
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
video = OUTPUT / ('herd-feedback-applied.mp4' if APPLIED else 'herd-dust-feedback-v1.mp4')
command = [ffmpeg, '-y', '-hide_banner', '-loglevel', 'warning', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
           '-s', '1280x840', '-r', str(FPS), '-i', 'pipe:0', '-i', str(OUTPUT / 'preview-mix.wav'),
           '-map', '0:v:0', '-map', '1:a:0', '-c:v', 'libx264', '-crf', '18', '-preset', 'medium',
           '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', '-shortest', str(video)]
process = subprocess.Popen(command, stdin=subprocess.PIPE)
for index, path in enumerate(files):
    time = index / FPS
    result = Image.new('RGB', (1280, 840), '#121e24')
    result.paste(Image.open(path).convert('RGB'), (0, 64))
    draw = ImageDraw.Draw(result)
    draw.text((16, 5), '牛群声音与扬尘 · 正式表现类隔离回读' if APPLIED else '牛群声音与扬尘 v1 · Unity隔离动态候选', font=font, fill='white')
    draw.text((16, 36), '正式模型／材质／反馈类；声音取实际表现事件与同源片段离线混音，非真实对局' if APPLIED else '正式牛／主角／草地农场克隆 ＋ 临时薄尘 ＋ 合成候选声音；未接入正式资源', font=small, fill='#c2d3d9')
    if time < .8:
        text = '起势：刨地小尘团、刨地声与低沉鼻息'
    elif time < 6.4:
        text = '纵队原速：3米间距／7.15米每秒；听牛蹄声由远到近、再远去'
    elif time < 8.4:
        text = '蹄下近景 ¼速：观察小团土尘扩散消失；蹄音保持原音高'
    else:
        text = '撞击近景：短促尘团与一次闷撞；动作只为展示反馈，不是新落点方案'
    draw.text((16, 789), text, font=small, fill='white')
    draw.text((16, 815), f'{time:04.1f}s / 11.6s · 非真实对局／非正式音频系统实录', font=small, fill='#b6cbd3')
    if index in (65, 138, 183):
        result.save(OUTPUT / {65: 'herd-passing.png', 138: 'hoof-closeup.png', 183: 'impact.png'}[index])
    process.stdin.write(result.tobytes())
process.stdin.close()
assert process.wait() == 0
# 完整解码音视频两路，而非只凭输出文件存在判断成功。
subprocess.run([ffmpeg, '-v', 'error', '-i', str(video), '-map', '0:v:0', '-map', '0:a:0', '-f', 'null', '-'], check=True)
audit = dict(frames=len(files), fps=FPS, duration=DURATION, dimensions=[1280, 840], audio_rate=RATE,
             channels=2, peak=float(np.max(np.abs(mix))), rms=float(np.sqrt(np.mean(mix ** 2))),
             hoof_events=len(gains), hit_time=9.1, encoded_bytes=video.stat().st_size,
             full_audio_video_decode_passed=True, offline_synthesized_audio=True, production_assets_written=False,
             using_production_feedback_class=APPLIED)
(OUTPUT / 'audit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(audit, ensure_ascii=False, indent=2))
print(video)
