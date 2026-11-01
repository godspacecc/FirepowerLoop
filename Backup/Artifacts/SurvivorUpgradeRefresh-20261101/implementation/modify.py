from pathlib import Path
import uuid

def edit(path, fn):
 p=Path(path);raw=p.read_bytes();bom=raw.startswith(b"\xef\xbb\xbf");s=raw.decode("utf-8-sig");nl="\r\n" if "\r\n" in s else "\n";s=s.replace("\r\n","\n");p.write_bytes((b"\xef\xbb\xbf" if bom else b"")+fn(s).replace("\n",nl).encode("utf-8"))
def create(path,s):
 p=Path(path);p.write_text(s,encoding="utf-8");meta=p.with_suffix(p.suffix+".meta")
 if not meta.exists():meta.write_text("fileFormatVersion: 2\nguid: "+uuid.uuid4().hex+"\n",encoding="utf-8")
