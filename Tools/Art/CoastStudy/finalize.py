"""冻结当前海岸样板所需资源与截图；不移动或删除备用资源。"""
from pathlib import Path
import hashlib
import json
import re
import shutil

ROOT = Path(__file__).resolve().parents[3]
STUDY = ROOT / "Client/Assets/Art/CoastStudy"
OUTPUT = ROOT / "Docs/Plans/Artifacts/CoastArtStudy-20261021"
OUTPUT.mkdir(parents=True, exist_ok=True)
guid_paths = {}
for meta in STUDY.rglob("*.meta"):
    match = re.search(r"^guid: (\w+)", meta.read_text(encoding="utf-8"), re.M)
    if match:
        guid_paths[match[1]] = Path(str(meta)[:-5])

pending = [STUDY / "CoastArtStudy.unity"]
assets = set()
while pending:
    path = pending.pop()
    if path in assets:
        continue
    if not path.exists():
        raise RuntimeError(f"Missing sample asset: {path}")
    assets.add(path)
    if path.suffix.lower() not in (".fbx", ".png", ".jpg", ".tif", ".tga"):
        for guid in re.findall(r"guid: ([0-9a-f]{32})", path.read_text(encoding="utf-8")):
            if guid in guid_paths:
                pending.append(guid_paths[guid])

files = set(assets)
for asset in assets:
    meta = Path(str(asset) + ".meta")
    if not meta.exists():
        raise RuntimeError(f"Missing sample meta: {meta}")
    files.add(meta)
    for parent in asset.parents:
        if parent == STUDY.parent:
            break
        folder_meta = Path(str(parent) + ".meta")
        if folder_meta.exists():
            files.add(folder_meta)

rows = []
for path in sorted(files):
    rows.append({"path": path.relative_to(ROOT).as_posix(), "bytes": path.stat().st_size,
                 "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
(OUTPUT / "sample-assets.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
for name in ("coast-overview.png", "dock-detail.png", "checks.txt", "note.txt"):
    source = ROOT / "Client/Temp/CoastArtStudy" / name
    if not source.exists():
        raise RuntimeError(f"Missing Unity capture: {source}")
    shutil.copy2(source, OUTPUT / name)
print(f"Sample dependency closure: {len(assets)} assets, {len(files)} files, {sum(r['bytes'] for r in rows):,} bytes")
print("No production assets changed; candidate files preserved.")
