"""生成独立电塔音效候选，并与Unity临时特效帧同步；不写正式音频或资源。"""
from pathlib import Path
import sys
import subprocess
import wave
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / "Client/Temp/ElectricTowerFeedbackTools"))
import imageio_ffmpeg

source = root / "Client/Temp/ElectricTowerFeedbackPreview"
target = root / "Backup/Artifacts/ElectricTowerFeedback-v1-20261023"
target.mkdir(parents=True, exist_ok=True)
files = sorted(source.glob("[0-9][0-9][0-9].png"))
assert len(files) == 212
rate = 44100
rng = np.random.default_rng(231023)


def noise(seconds, low, high):
    """带限噪声形成电弧嘶鸣，随机种子固定以保证每次候选相同。"""
    count = round(seconds * rate)
    frequencies = np.fft.rfftfreq(count, 1 / rate)
    spectrum = np.fft.rfft(rng.standard_normal(count))
    mask = (frequencies >= low) & (frequencies <= high)
    result = np.fft.irfft(spectrum * mask, n=count)
    return result / max(1e-9, np.sqrt(np.mean(result ** 2)))


def ramp(signal, attack=.008, release=.025):
    result = signal.copy()
    a, b = round(attack * rate), round(release * rate)
    result[:a] *= np.linspace(0, 1, a)
    result[-b:] *= np.linspace(1, 0, b)
    return result


def normalize(signal, peak):
    return signal * peak / max(1e-9, np.max(np.abs(signal)))


def save(name, signal):
    """预览单声道源保留为PCM，正式接入仍需用户采用此音色。"""
    with wave.open(str(target / name), "wb") as output:
        output.setnchannels(1 if signal.ndim == 1 else signal.shape[1])
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes((np.clip(signal, -.99, .99) * 32767).astype("<i2").tobytes())


t = np.arange(round(1.5 * rate)) / rate
u = t / 1.5
phase = 2 * np.pi * (92 * t + 72 * t ** 2)
buzz = sum(np.sin(phase * h + h * .17) / (h ** .8) for h in range(1, 9))
crackle = noise(1.5, 850, 7200) * (.14 + .86 * np.maximum(0, np.sin(2 * np.pi * (21 * t + 6 * t ** 2))) ** 8)
charge = normalize(ramp((buzz * .20 + crackle * .23) * (.10 + .90 * u ** 1.3)), .43)

t = np.arange(3 * rate) / rate
phase = 2 * np.pi * (100 * t + .8 * np.sin(2 * np.pi * 8 * t))
buzz = sum(np.sin(phase * h + h * .31) / h for h in range(1, 12))
flutter = .36 + .64 * np.maximum(0, np.sin(2 * np.pi * 31 * t)) ** 3
active = normalize(ramp(buzz * .15 + noise(3, 350, 8500) * flutter * .23), .43)

t = np.arange(round(.24 * rate)) / rate
onset = noise(.24, 180, 10000) * np.exp(-t * 25)
onset += np.sin(2 * np.pi * (1100 * t - 1700 * t ** 2)) * np.exp(-t * 19) * .7
onset = normalize(ramp(onset, .001, .025), .58)

t = np.arange(round(.40 * rate)) / rate
hit = noise(.40, 500, 11000) * np.exp(-t * 28)
hit += .6 * np.sin(2 * np.pi * (620 * t - 550 * t ** 2)) * np.exp(-t * 20)
hit += .35 * np.sin(2 * np.pi * 90 * t) * np.exp(-t * 16)
hit = normalize(ramp(hit, .001, .035), .72)

for name, signal in [("charge-candidate.wav", charge), ("discharge-candidate.wav", active),
                     ("power-on-candidate.wav", onset), ("hit-candidate.wav", hit)]:
    save(name, signal)

mix = np.zeros(round(10.6 * rate))


def add(signal, start, gain=1):
    offset = round(start * rate)
    count = min(len(signal), len(mix) - offset)
    mix[offset:offset + count] += signal[:count] * gain


add(charge, 4, .78)
add(active, 5.5, .78)
add(onset, 5.5, .80)
add(hit, 6.1)
# 慢放段重复命中声音供审听，保持正常音高；没有重复触发真实伤害。
add(active[:round(2.1 * rate)], 8.5, .22)
add(hit, 9.1)
mix *= min(1, .92 / max(1e-9, np.max(np.abs(mix))))
stereo = np.column_stack((mix * .92, mix))
save("preview-mix.wav", stereo)

font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 25)
small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 19)
video = target / "tower-feedback-candidate.mp4"
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
command = [ffmpeg, "-y", "-hide_banner", "-loglevel", "warning", "-f", "rawvideo", "-pix_fmt", "rgb24",
           "-s", "1280x720", "-r", "20", "-i", "pipe:0", "-i", str(target / "preview-mix.wav"),
           "-map", "0:v:0", "-map", "1:a:0", "-c:v", "libx264", "-preset", "medium", "-crf", "19",
           "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-shortest", str(video)]
process = subprocess.Popen(command, stdin=subprocess.PIPE)
for i, path in enumerate(files):
    frame = Image.open(path).convert("RGB")
    draw = ImageDraw.Draw(frame)
    draw.text((315, 14), "当前：无声音及专用命中反馈", font=font, fill="white", anchor="mt")
    draw.text((965, 14), "候选：补充声音与电击命中反馈", font=font, fill="white", anchor="mt")
    draw.text((640, 51), "正式电塔与主角 · 隔离临时音画候选 · 非真实对局", font=small, fill="#b8cdd5", anchor="mt")
    if i < 80:
        text, color = "安全：熄弧，无电流声", "#adf5bf"
    elif i < 110:
        text, color = "预警蓄能：电流声随顶部蓄能逐渐增强", "#ffba99"
    elif 122 <= i <= 128:
        text, color = "实际命中反馈候选：塔顶连向目标 ＋ 蓝白电火花 ＋ 电击声", "#d8faff"
    elif i < 170:
        text, color = "通电：持续电弧声；只有实际命中才追加电击声与受击火花", "#9fe8ff"
    else:
        text, color = "命中细节 ¼ 速回放（临时视觉与声音候选）", "#d8faff"
    draw.rectangle((0, 642, 1280, 720), fill="#1c3039")
    draw.text((640, 651), text, font=font, fill=color, anchor="mt")
    draw.text((640, 690), "声音为本次候选合成并同步；伤害、死亡、复活规则保持原样", font=small, fill="#b8cdd5", anchor="mt")
    if i in (103, 122, 123):
        frame.save(target / {103: "charging.png", 122: "hit-flash.png", 123: "hit-arcs.png"}[i])
    process.stdin.write(frame.tobytes())
process.stdin.close()
assert process.wait() == 0, "预览视频编码失败"
audit = {"source_frames": len(files), "fps": 20, "duration_seconds": 10.6,
         "charge_start": 4, "active_start": 5.5, "hit": 6.1, "slow_replay_start": 8.5, "slow_replay_hit": 9.1,
         "audio_sample_rate": rate, "audio_channels": 2, "peak": float(np.max(np.abs(stereo))),
         "rms_charge": float(np.sqrt(np.mean(charge ** 2))), "rms_active": float(np.sqrt(np.mean(active ** 2))),
         "rms_hit": float(np.sqrt(np.mean(hit ** 2))), "video_bytes": video.stat().st_size}
(target / "audit.json").write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(audit, ensure_ascii=False, indent=2))
print(video)
