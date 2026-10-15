"""Apply the manually verified full-map road correction to map masks."""

from __future__ import annotations

import json
import os
import tempfile
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter


SCRIPT_DIR = Path(__file__).resolve().parent
DATA_PATH = SCRIPT_DIR / "HeJiaVillage_VerifiedRoads.json"
REPO_ROOT = SCRIPT_DIR.parents[2]
CONTRACT_PATH = SCRIPT_DIR / "metadata.json"
SATELLITE_MASK_PATH = (
    REPO_ROOT
    / "Client"
    / "Assets"
    / "Game"
    / "MapTerrainSources"
    / "HeJiaVillage"
    / "HeJiaVillage_SatelliteRoadMask.png"
)


def load_verified_roads() -> dict:
    return json.loads(DATA_PATH.read_text(encoding="utf-8"))


def _pixel_xy(point: list[float], contract: dict, output_size: int) -> tuple[float, float]:
    utm = contract["utm"]
    center_easting = float(utm["centerEasting"])
    center_northing = float(utm["centerNorthing"])
    easting = center_easting + float(point[0])
    northing = center_northing + float(point[1])
    column = (easting - float(utm["west"])) / (float(utm["east"]) - float(utm["west"])) * output_size
    row = (float(utm["north"]) - northing) / (float(utm["north"]) - float(utm["south"])) * output_size
    return column, row


def _bounds_box(data: dict, contract: dict, output_size: int) -> tuple[int, int, int, int]:
    bounds = data["correctionBoundsLocalMeters"]
    north_west = _pixel_xy([bounds["west"], bounds["north"]], contract, output_size)
    south_east = _pixel_xy([bounds["east"], bounds["south"]], contract, output_size)
    return tuple(round(value) for value in (*north_west, *south_east))


def verified_core_mask(contract: dict, output_size: int) -> Image.Image:
    data = load_verified_roads()
    image = Image.new("L", (output_size, output_size), 0)
    draw = ImageDraw.Draw(image)
    for road in data["roads"]:
        points = [_pixel_xy(point, contract, output_size) for point in road["localMeters"]]
        width = max(6, round(float(road["visibilityWidthMeters"])))
        draw.line(points, fill=255, width=width, joint="curve")
        radius = width / 2
        for point in (points[0], points[-1]):
            draw.ellipse((point[0] - radius, point[1] - radius,
                          point[0] + radius, point[1] + radius), fill=255)
    return image


def apply_verified_roads(mask: Image.Image, contract: dict, *, cartographic_border: bool) -> Image.Image:
    """Replace the reviewed map extent while preserving pixels outside its declared bounds."""
    output = mask.convert("L").copy()
    data = load_verified_roads()
    ImageDraw.Draw(output).rectangle(_bounds_box(data, contract, output.width), fill=0)
    core = verified_core_mask(contract, output.width)
    if cartographic_border:
        border = core.filter(ImageFilter.MaxFilter(9)).point(lambda value: 128 if value else 0)
        output = ImageChops.lighter(output, border)
    return ImageChops.lighter(output, core)


def main() -> int:
    """Patch the published minimap mask when the full raster toolchain is unavailable."""
    contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
    with Image.open(SATELLITE_MASK_PATH) as image:
        corrected = apply_verified_roads(image, contract, cartographic_border=True)
    with tempfile.NamedTemporaryFile(suffix=".png", delete=False,
                                     dir=SATELLITE_MASK_PATH.parent) as stream:
        temp_path = Path(stream.name)
    try:
        corrected.save(temp_path, optimize=True)
        os.replace(temp_path, SATELLITE_MASK_PATH)
    finally:
        temp_path.unlink(missing_ok=True)
    print(f"Patched {SATELLITE_MASK_PATH.relative_to(REPO_ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
