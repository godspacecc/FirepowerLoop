"""只编码预览视频并配入现有音效，不修改游戏资源。"""
from pathlib import Path
import hashlib
import shutil
import subprocess
import sys
import wave

ROOT = Path(__file__).resolve().parents[4]
FOLDER = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "Client/Temp/BarrelAVTools"))
import imageio_ffmpeg

ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
report = []
for label, source, output in [
    ("A: existing energy explosion", "ArcadeExplosionSciFi.wav", "purple-explosion-energy.mp4"),
    ("B: existing ordinary explosion", "ArcadeExplosion.wav", "purple-explosion-ordinary.mp4"),
]:
    audio = ROOT / "Client/Assets/Game/Audio/Sound" / source
    shutil.copyfile(audio, FOLDER / source)
    command = [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-framerate", "20",
               "-i", str(FOLDER / "purple-frame-%03d.png"), "-i", str(audio),
               "-map", "0:v:0", "-map", "1:a:0", "-vf", "scale=1120:630",
               "-af", "adelay=800|800,apad", "-t", "4.2", "-r", "20",
               "-c:v", "libx264", "-preset", "fast", "-crf", "19",
               "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
               "-movflags", "+faststart", str(FOLDER / output)]
    subprocess.run(command, check=True)
    # 解码整段视频及声音，确认两条流均存在且没有编码错误。
    subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "error", "-i", str(FOLDER / output),
                    "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"], check=True)
    pcm = subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "error", "-i", str(FOLDER / output),
                          "-vn", "-ac", "1", "-ar", "44100", "-f", "s16le", "-"],
                         check=True, capture_output=True).stdout
    import numpy as np
    samples = np.frombuffer(pcm, dtype="<i2").astype(float) / 32768
    assert np.max(np.abs(samples[:int(.7 * 44100)])) < .001
    assert np.max(np.abs(samples[int(.8 * 44100):int(1.3 * 44100)])) > .01
    with wave.open(str(audio), "rb") as clip:
        seconds = clip.getnframes() / clip.getframerate()
    assert hashlib.sha256(audio.read_bytes()).digest() == hashlib.sha256((FOLDER / source).read_bytes()).digest()
    report.append(f"PASS {label}: {output}; source={source}; source duration={seconds:.3f}s; "
                  f"video=4.2s; explosion and sound onset=0.8s; video+audio decode OK; copied WAV identical.")

(FOLDER / "audio-video-verification.txt").write_text("\n".join(report) + "\n", encoding="utf-8")
print("\n".join(report))
