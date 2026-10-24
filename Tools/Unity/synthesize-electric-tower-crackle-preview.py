"""电塔v2滋啦拉弧音色：仅制作可试听候选，不写正式音效和运行时代码。"""
from pathlib import Path
import sys
import wave
import json
import subprocess
import numpy as np

root = Path(__file__).resolve().parents[2]
target = root / "Backup/Artifacts/ElectricTowerFeedback-v2-20261023"
target.mkdir(parents=True, exist_ok=True)
rate = 44100


def coloured_noise(count, rng, low=450, high=10500):
    """宽频摩擦电弧底噪：圆滑带通与宽峰，不叠加正弦音或升降调。"""
    f = np.fft.rfftfreq(count, 1 / rate)
    low_cut = np.clip((f - low) / 300, 0, 1)
    high_cut = np.clip((high - f) / 1800, 0, 1)
    presence = .55 + .8 * np.exp(-((f - 2300) / 1500) ** 2)
    spectrum = np.fft.rfft(rng.standard_normal(count)) * low_cut * high_cut * presence
    result = np.fft.irfft(spectrum, n=count)
    return result / max(1e-9, np.std(result))


def arc(seconds, seed, density=1):
    """不规则长拉弧中夹短断续和密集微爆裂，形成滋啦、滋啦的粗糙电流声。"""
    rng = np.random.default_rng(seed)
    count = round(seconds * rate)
    phrases = np.zeros(count)
    cursor = 0
    while cursor < count:
        width = min(int(rng.uniform(.16, .36) * rate), count - cursor)
        envelope = np.ones(width) * rng.uniform(.65, 1)
        attack = min(round(.002 * rate), width)
        release = min(round(.018 * rate), width)
        envelope[:attack] *= np.linspace(.1, 1, attack)
        envelope[-release:] *= np.linspace(1, .05, release)
        phrases[cursor:cursor + width] = envelope
        cursor += width + int(rng.uniform(.008, .035) * rate)
    grain = np.zeros(count)
    cursor = 0
    while cursor < count:
        # 每次间隔和衰减都不同，避免稳定电机或蜂鸣的音高。
        width = min(int(rng.uniform(.002, .008) * rate), count - cursor)
        grain[cursor:cursor + width] += np.exp(-np.arange(width) / max(1, width * .32)) * rng.uniform(.3, 1)
        cursor += max(1, int(rng.uniform(.004, .015) * rate / density))
    bed = coloured_noise(count, rng) * phrases * (.22 + .95 * grain)
    pops = np.zeros(count)
    cursor = 0
    while cursor < count:
        width = min(int(rng.uniform(.002, .022) * rate), count - cursor)
        if width > 1:
            burst = coloured_noise(width, rng, 900, 12000)
            burst *= np.exp(-np.arange(width) / max(1, width * .22))
            pops[cursor:cursor + width] += burst * rng.uniform(.18, .7)
        cursor += max(1, int(rng.uniform(.018, .070) * rate / density))
    result = np.tanh((bed + pops * phrases) * 1.3)
    fade = min(round(.012 * rate), count // 2)
    result[:fade] *= np.linspace(0, 1, fade)
    result[-fade:] *= np.linspace(1, 0, fade)
    return result


def level(signal, rms, peak=.80):
    signal = signal * rms / max(1e-9, np.sqrt(np.mean(signal ** 2)))
    return signal * min(1, peak / max(1e-9, np.max(np.abs(signal))))


def save(name, signal):
    assert np.isfinite(signal).all() and np.max(np.abs(signal)) < 1
    with wave.open(str(target / name), "wb") as output:
        output.setnchannels(1 if signal.ndim == 1 else signal.shape[1])
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes((signal * 32767).astype("<i2").tobytes())


charge = arc(1.5, 231024, .75)
charge *= np.linspace(.18, 1, len(charge)) ** 1.15
charge = level(charge, .105, .55)
active = level(arc(3, 231025, 1.35), .165, .68)
onset = arc(.22, 231026, 2) * np.exp(-np.arange(round(.22 * rate)) / rate * 14)
onset = level(onset, .16, .72)
hit = arc(.38, 231027, 2.4) * np.exp(-np.arange(round(.38 * rate)) / rate * 13)
hit = level(hit, .16, .82)
for name, signal in [("charge-crackle.wav", charge), ("discharge-crackle.wav", active),
                     ("power-on-crackle.wav", onset), ("hit-crackle.wav", hit)]:
    save(name, signal)
# 短试听直接从蓄能开始，避免上一版完整视频前4秒的安全静音。
audition = np.concatenate((charge, np.zeros(round(.15 * rate)), active))
save("current-crackle-audition.wav", audition)

mix = np.zeros(round(10.6 * rate))


def add(signal, start, gain=1):
    offset = round(start * rate)
    length = min(len(signal), len(mix) - offset)
    mix[offset:offset + length] += signal[:length] * gain


add(charge, 4, .85)
add(active, 5.5, .85)
add(onset, 5.5, .85)
add(hit, 6.1)
add(active[:round(2.1 * rate)], 8.5, .28)
add(hit, 9.1)
mix *= min(1, .92 / max(1e-9, np.max(np.abs(mix))))
save("preview-mix.wav", np.column_stack((mix * .92, mix)))

sys.path.insert(0, str(root / "Client/Temp/ElectricTowerFeedbackTools"))
import imageio_ffmpeg
video = target / "tower-feedback-crackle-candidate.mp4"
command = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-hide_banner", "-loglevel", "error",
           "-i", str(root / "Backup/Artifacts/ElectricTowerFeedback-v1-20261023/tower-feedback-candidate.mp4"),
           "-i", str(target / "preview-mix.wav"), "-map", "0:v:0", "-map", "1:a:0",
           "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-t", "10.6", "-movflags", "+faststart", str(video)]
subprocess.run(command, check=True)
audit = {"sample_rate": rate, "audition_seconds": len(audition) / rate,
         "audition_peak": float(np.max(np.abs(audition))), "mix_peak": float(np.max(np.abs(mix))),
         "charge_rms": float(np.sqrt(np.mean(charge ** 2))), "active_rms": float(np.sqrt(np.mean(active ** 2))),
         "tonal_oscillators": 0, "visual_stream": "copy of v1, unchanged", "video_bytes": video.stat().st_size}
(target / "audit.json").write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(audit, ensure_ascii=False, indent=2))
print(target / "current-crackle-audition.wav")
