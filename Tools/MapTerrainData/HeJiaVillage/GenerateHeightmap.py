"""Generate the fixed HeJiaVillage terrain inputs from Copernicus GLO-30.

The geographic extent, projection, source priority, resampling and encoding are
deliberately constants. This script accepts no command-line parameters.
"""

from __future__ import annotations

import hashlib
import json
import math
import shutil
import sys
import tempfile
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import png
import rasterio
from PIL import Image, ImageDraw
from rasterio.enums import Resampling
from rasterio.transform import from_origin
from rasterio.warp import reproject, transform


MAP_NAME = "HeJiaVillage"
CENTER_LAT = 27.635130
CENTER_LON = 112.185800
SOURCE_CRS = "EPSG:4326"
TARGET_CRS = "EPSG:32649"
CENTER_EASTING = 616985.992
CENTER_NORTHING = 3057346.487
WEST = 615961.992
EAST = 618009.992
SOUTH = 3056322.487
NORTH = 3058370.487
GRID_SIZE = 1025
SPACING_METERS = 2.0

DATASET = "Copernicus DEM 2021 GLO-30"
DATASET_VERSION = "2021"
VERTICAL_DATUM = "EGM2008"
SOURCE_FILENAME = "Copernicus_DSM_COG_10_N27_00_E112_00_DEM.tif"
SOURCE_URL = (
    "https://copernicus-dem-30m.s3.amazonaws.com/"
    "Copernicus_DSM_COG_10_N27_00_E112_00_DEM/"
    + SOURCE_FILENAME
)
LICENSE_URL = (
    "https://dataspace.copernicus.eu/explore-data/data-collections/"
    "copernicus-contributing-missions/collections-description/COP-DEM"
)
AWS_REGISTRY_URL = "https://registry.opendata.aws/copernicus-dem/"

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[2]
UNITY_DIR = REPO_ROOT / "Client" / "Assets" / "Game" / "MapTerrainSources" / MAP_NAME
TEMP_ROOT = Path(tempfile.gettempdir()) / "AnotherDay-HeJiaVillage-20260911"
SOURCE_PATH = TEMP_ROOT / "downloads" / SOURCE_FILENAME
FLOAT_TIF = SCRIPT_DIR / "HeJiaVillage_Elevation_UTM49N_Float32.tif"
RGB48_PNG = UNITY_DIR / "HeJiaVillage_Heightmap_RGB48.png"
PREVIEW_PNG = SCRIPT_DIR / "HeJiaVillage_Heightmap_Preview.png"
TOOLS_METADATA = SCRIPT_DIR / "metadata.json"
UNITY_METADATA = UNITY_DIR / "HeJiaVillage_Heightmap.metadata.json"
ATTRIBUTION = SCRIPT_DIR / "ATTRIBUTION.md"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def ensure_source() -> None:
    SOURCE_PATH.parent.mkdir(parents=True, exist_ok=True)
    if SOURCE_PATH.is_file() and SOURCE_PATH.stat().st_size > 0:
        return
    partial = SOURCE_PATH.with_suffix(".download")
    urllib.request.urlretrieve(SOURCE_URL, partial)
    partial.replace(SOURCE_PATH)


def validate_constants() -> tuple[float, float]:
    projected_x, projected_y = transform(
        SOURCE_CRS, TARGET_CRS, [CENTER_LON], [CENTER_LAT]
    )
    x, y = projected_x[0], projected_y[0]
    if abs(x - CENTER_EASTING) > 0.001 or abs(y - CENTER_NORTHING) > 0.001:
        raise RuntimeError(f"Projected center changed: {x}, {y}")
    expected = (GRID_SIZE - 1) * SPACING_METERS
    if abs(EAST - WEST - expected) > 1e-6 or abs(NORTH - SOUTH - expected) > 1e-6:
        raise RuntimeError("Fixed UTM bounds do not describe a 2048 m vertex extent")
    return x, y


def resample_dem() -> tuple[np.ndarray, dict]:
    ensure_source()
    with rasterio.open(SOURCE_PATH) as source:
        if str(source.crs) != SOURCE_CRS:
            raise RuntimeError(f"Unexpected source CRS: {source.crs}")
        if source.count != 1 or source.dtypes[0] != "float32":
            raise RuntimeError(f"Unexpected source structure: {source.count}, {source.dtypes}")
        if not (
            source.bounds.left <= CENTER_LON <= source.bounds.right
            and source.bounds.bottom <= CENTER_LAT <= source.bounds.top
        ):
            raise RuntimeError(f"Source does not cover center: {source.bounds}")

        elevations = np.full((GRID_SIZE, GRID_SIZE), np.nan, dtype=np.float32)
        destination_transform = from_origin(
            WEST - SPACING_METERS / 2,
            NORTH + SPACING_METERS / 2,
            SPACING_METERS,
            SPACING_METERS,
        )
        reproject(
            source=rasterio.band(source, 1),
            destination=elevations,
            src_transform=source.transform,
            src_crs=source.crs,
            src_nodata=source.nodata,
            dst_transform=destination_transform,
            dst_crs=TARGET_CRS,
            dst_nodata=np.nan,
            resampling=Resampling.bilinear,
        )
        source_info = {
            "crs": str(source.crs),
            "bounds": [float(v) for v in source.bounds],
            "width": source.width,
            "height": source.height,
            "dtype": source.dtypes[0],
            "nodata": source.nodata,
        }

    if elevations.shape != (GRID_SIZE, GRID_SIZE) or not np.isfinite(elevations).all():
        invalid = int(elevations.size - np.count_nonzero(np.isfinite(elevations)))
        raise RuntimeError(f"Resampled grid has {invalid} invalid values")
    return elevations, {"transform": list(destination_transform), **source_info}


def write_float_tif(elevations: np.ndarray) -> None:
    transform_value = from_origin(
        WEST - SPACING_METERS / 2,
        NORTH + SPACING_METERS / 2,
        SPACING_METERS,
        SPACING_METERS,
    )
    with rasterio.open(
        FLOAT_TIF,
        "w",
        driver="GTiff",
        width=GRID_SIZE,
        height=GRID_SIZE,
        count=1,
        dtype="float32",
        crs=TARGET_CRS,
        transform=transform_value,
        compress="deflate",
        predictor=3,
    ) as target:
        target.write(elevations.astype(np.float32), 1)
        target.update_tags(
            AREA_OR_POINT="Point",
            mapName=MAP_NAME,
            orientation="row 0 north; column 0 west",
            verticalDatum=VERTICAL_DATUM,
        )


def write_rgb48(gray16: np.ndarray) -> None:
    UNITY_DIR.mkdir(parents=True, exist_ok=True)
    writer = png.Writer(
        width=GRID_SIZE,
        height=GRID_SIZE,
        greyscale=False,
        alpha=False,
        bitdepth=16,
        compression=9,
    )
    with RGB48_PNG.open("wb") as stream:
        rows = (np.repeat(row[:, None], 3, axis=1).reshape(-1).tolist() for row in gray16)
        writer.write(stream, rows)


def reread_rgb48() -> tuple[np.ndarray, dict]:
    width, height, rows, info = png.Reader(filename=str(RGB48_PNG)).read()
    values = np.asarray([list(row) for row in rows], dtype=np.uint16).reshape(height, width, 3)
    if (width, height) != (GRID_SIZE, GRID_SIZE):
        raise RuntimeError(f"Unexpected PNG size: {width}x{height}")
    if info.get("bitdepth") != 16 or info.get("planes") != 3:
        raise RuntimeError(f"PNG is not RGB48: {info}")
    if not (np.array_equal(values[:, :, 0], values[:, :, 1]) and np.array_equal(values[:, :, 0], values[:, :, 2])):
        raise RuntimeError("RGB48 channel values differ")
    return values[:, :, 0], {"bitdepth": info["bitdepth"], "planes": info["planes"]}


def write_preview(elevations: np.ndarray) -> None:
    lo, hi = float(elevations.min()), float(elevations.max())
    scaled = np.rint((elevations - lo) / max(hi - lo, 1e-9) * 255).astype(np.uint8)
    image = Image.fromarray(scaled, mode="L").convert("RGB")
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, GRID_SIZE - 1, GRID_SIZE - 1), outline=(255, 0, 0), width=3)
    draw.text((GRID_SIZE // 2 - 5, 8), "N", fill=(255, 0, 0), stroke_width=1, stroke_fill=(255, 255, 255))
    draw.text((GRID_SIZE // 2 - 5, GRID_SIZE - 22), "S", fill=(255, 0, 0), stroke_width=1, stroke_fill=(255, 255, 255))
    draw.text((8, GRID_SIZE // 2 - 5), "W", fill=(255, 0, 0), stroke_width=1, stroke_fill=(255, 255, 255))
    draw.text((GRID_SIZE - 18, GRID_SIZE // 2 - 5), "E", fill=(255, 0, 0), stroke_width=1, stroke_fill=(255, 255, 255))
    image.save(PREVIEW_PNG, format="PNG", optimize=True)


def make_control_points(elevations: np.ndarray, gray16: np.ndarray) -> list[dict]:
    indexes = (0, 256, 512, 768, 1024)
    return [
        {
            "row": row,
            "column": column,
            "gray16": int(gray16[row, column]),
            "elevation": float(elevations[row, column]),
        }
        for row in indexes
        for column in indexes
    ]


def write_attribution(download_date: str, source_size: int, source_sha256: str) -> None:
    text = f"""# HeJiaVillage terrain elevation attribution

- Dataset: Copernicus DEM 2021 GLO-30 (digital surface model, approximately 30 m source sampling)
- Adopted source: `{SOURCE_FILENAME}`
- Download date (UTC): {download_date}
- Source bytes: {source_size}
- Source SHA-256: `{source_sha256}`
- Horizontal datum/CRS: source WGS84 (`EPSG:4326`), processed in WGS84 / UTM zone 49N (`EPSG:32649`)
- Vertical datum: EGM2008
- Product and licence information: {LICENSE_URL}
- AWS Open Data registry: {AWS_REGISTRY_URL}

Copernicus DEM is a digital surface model. Vegetation and buildings can affect local elevations; this is not 2 m surveyed bare-earth data. Google Maps was used only to identify the requested center coordinate and is not an elevation-data source.
"""
    ATTRIBUTION.write_text(text, encoding="utf-8")


def main() -> None:
    SCRIPT_DIR.mkdir(parents=True, exist_ok=True)
    UNITY_DIR.mkdir(parents=True, exist_ok=True)
    projected_x, projected_y = validate_constants()
    elevations, source_raster = resample_dem()
    min_elevation = float(elevations.min())
    max_elevation = float(elevations.max())
    base_elevation = 10.0 * math.floor((min_elevation - 5.0) / 10.0)
    terrain_height_range = 10.0 * math.ceil((max_elevation - base_elevation + 5.0) / 10.0)
    normalized = np.clip((elevations.astype(np.float64) - base_elevation) / terrain_height_range, 0.0, 1.0)
    gray16 = np.rint(normalized * 65535.0).astype(np.uint16)

    write_float_tif(elevations)
    write_rgb48(gray16)
    decoded_gray16, png_info = reread_rgb48()
    if not np.array_equal(decoded_gray16, gray16):
        raise RuntimeError("Re-read PNG samples differ from encoded gray16")
    reconstructed = base_elevation + decoded_gray16.astype(np.float64) / 65535.0 * terrain_height_range
    max_reconstruction_error = float(np.max(np.abs(reconstructed - elevations.astype(np.float64))))
    reconstruction_tolerance = terrain_height_range / 65535.0 + 0.001
    if max_reconstruction_error > reconstruction_tolerance:
        raise RuntimeError(
            f"Reconstruction error {max_reconstruction_error} exceeds {reconstruction_tolerance}"
        )
    write_preview(elevations)

    source_size = SOURCE_PATH.stat().st_size
    source_sha256 = sha256_file(SOURCE_PATH)
    download_date = datetime.fromtimestamp(SOURCE_PATH.stat().st_mtime, timezone.utc).date().isoformat()
    metadata = {
        "schemaVersion": 1,
        "mapName": MAP_NAME,
        "centerWgs84": {"latitude": CENTER_LAT, "longitude": CENTER_LON},
        "utm": {
            "crs": TARGET_CRS,
            "centerEasting": CENTER_EASTING,
            "centerNorthing": CENTER_NORTHING,
            "computedCenterEasting": projected_x,
            "computedCenterNorthing": projected_y,
            "west": WEST,
            "east": EAST,
            "south": SOUTH,
            "north": NORTH,
        },
        "grid": {
            "width": GRID_SIZE,
            "height": GRID_SIZE,
            "spacingMeters": SPACING_METERS,
            "orientation": "first row north, last row south, first column west, last column east",
            "pixelCentersAreTerrainVertices": True,
        },
        "source": {
            "dataset": DATASET,
            "version": DATASET_VERSION,
            "fileName": SOURCE_FILENAME,
            "url": SOURCE_URL,
            "officialProductAndLicenseUrl": LICENSE_URL,
            "awsRegistryUrl": AWS_REGISTRY_URL,
            "downloadDateUtc": download_date,
            "byteSize": source_size,
            "sha256": source_sha256,
            "horizontalDatum": "WGS84",
            "sourceCrs": SOURCE_CRS,
            "verticalDatum": VERTICAL_DATUM,
            "surfaceModel": "DSM",
            "nominalSourceSamplingMeters": 30,
            "raster": source_raster,
        },
        "processing": {
            "resampling": "bilinear",
            "artificialDetail": False,
            "invalidValueCount": 0,
            "pythonVersion": sys.version.split()[0],
            "rasterioVersion": rasterio.__version__,
            "gdalVersion": rasterio.__gdal_version__,
            "numpyVersion": np.__version__,
            "pypngVersion": png.__version__,
            "pillowVersion": Image.__version__,
        },
        "elevationEncoding": {
            "minElevation": min_elevation,
            "maxElevation": max_elevation,
            "baseElevation": base_elevation,
            "terrainHeightRange": terrain_height_range,
            "formula": "gray16=round(clamp((elevation-baseElevation)/terrainHeightRange,0,1)*65535)",
            "decodedFormula": "baseElevation + gray16/65535*terrainHeightRange",
            "maxReconstructionErrorMeters": max_reconstruction_error,
            "reconstructionToleranceMeters": reconstruction_tolerance,
        },
        "png": {
            "fileName": RGB48_PNG.name,
            "width": GRID_SIZE,
            "height": GRID_SIZE,
            "channels": 3,
            "bitsPerChannel": png_info["bitdepth"],
            "format": "RGB48",
            "channelValuesIdentical": True,
            "firstRowAtImageTop": True,
        },
        "unity": {
            "rows": 2,
            "columns": 2,
            "tileWorldSize": 1024.0,
            "heightmapResolution": 513,
            "maxTerrainHeight": terrain_height_range,
            "detailSeed": 0,
            "detailFrequency": 4.0,
            "detailAmplitude": 0.0,
            "detailOctaves": 3,
        },
        "controlPoints": make_control_points(elevations, gray16),
        "selfCheck": {
            "status": "passed",
            "sourceReadable": True,
            "sourceCoverageContainsCenter": True,
            "finiteGrid": True,
            "rgbChannelsIdentical": True,
            "rgbBitDepth": 16,
            "reconstructionWithinTolerance": True,
            "controlPointCount": 25,
        },
    }
    serialized = json.dumps(metadata, ensure_ascii=False, indent=2) + "\n"
    TOOLS_METADATA.write_text(serialized, encoding="utf-8")
    shutil.copyfile(TOOLS_METADATA, UNITY_METADATA)
    write_attribution(download_date, source_size, source_sha256)

    print(json.dumps({
        "status": "passed",
        "sourceBytes": source_size,
        "sourceSha256": source_sha256,
        "centerUtm": [projected_x, projected_y],
        "bounds": [WEST, SOUTH, EAST, NORTH],
        "shape": list(elevations.shape),
        "minElevation": min_elevation,
        "maxElevation": max_elevation,
        "baseElevation": base_elevation,
        "terrainHeightRange": terrain_height_range,
        "maxReconstructionErrorMeters": max_reconstruction_error,
        "reconstructionToleranceMeters": reconstruction_tolerance,
    }, indent=2))


if __name__ == "__main__":
    main()
