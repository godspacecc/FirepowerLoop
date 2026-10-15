"""Build and verify the Unity surface-control texture from synchronized offline sources.

R stores ESA WorldCover classes plus conservative satellite-verified pond corrections,
G stores Sentinel-2 NDVI P50 raw values, B is reserved, and A stores OSM roads plus
the reviewed full-map road correction.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from VerifiedRoadCorrections import DATA_PATH as VERIFIED_ROADS_PATH
from VerifiedRoadCorrections import apply_verified_roads, load_verified_roads
from VerifiedSurfaceCorrections import DATA_PATH as VERIFIED_SURFACE_PATH
from VerifiedSurfaceCorrections import apply_verified_surface_classes, load_verified_surface


MAP_NAME = "HeJiaVillage"
OUTPUT_SIZE = 2048
SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[2]
UNITY_DIR = REPO_ROOT / "Client" / "Assets" / "Game" / "MapTerrainSources" / MAP_NAME / "FeatureReferences"


OUTPUT_NAME = f"{MAP_NAME}_SurfaceControl.png"
OUTPUT_PATH = UNITY_DIR / OUTPUT_NAME
METADATA_PATH = UNITY_DIR / f"{MAP_NAME}_SurfaceControl.metadata.json"
WORLD_COVER_PATH = SCRIPT_DIR / f"{MAP_NAME}_WorldCover_2021_v200_UTM49N.tif"
NDVI_PATH = SCRIPT_DIR / f"{MAP_NAME}_Sentinel2_2021_NDVI_UTM49N.tif"
OSM_PATH = SCRIPT_DIR / f"{MAP_NAME}_OSM_Features_UTM49N.geojson"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def stable_guid(name: str) -> str:
    return hashlib.md5(f"AnotherDay:{MAP_NAME}:FeatureReferences:{name}".encode("utf-8"), usedforsecurity=False).hexdigest()


def load_contract() -> dict:
    return json.loads((SCRIPT_DIR / "metadata.json").read_text(encoding="utf-8"))


def road_width(tags: dict[str, str]) -> int:
    value = tags.get("highway", "")
    if value in ("motorway", "trunk", "motorway_link", "trunk_link"):
        return 8
    if value in ("primary", "secondary", "primary_link", "secondary_link"):
        return 6
    if value in ("tertiary", "residential", "tertiary_link"):
        return 4
    return 2


def geometry_lines(geometry: dict) -> list[list[list[float]]]:
    kind = geometry["type"]
    coordinates = geometry["coordinates"]
    if kind == "LineString":
        return [coordinates]
    if kind == "MultiLineString":
        return coordinates
    if kind == "Polygon":
        return coordinates
    if kind == "MultiPolygon":
        return [ring for polygon in coordinates for ring in polygon]
    return []


def pixel_xy(x: float, y: float, contract: dict) -> tuple[float, float]:
    utm = contract["utm"]
    column = (x - float(utm["west"])) / (float(utm["east"]) - float(utm["west"])) * OUTPUT_SIZE
    row = (float(utm["north"]) - y) / (float(utm["north"]) - float(utm["south"])) * OUTPUT_SIZE
    return column, row


def texture_meta() -> str:
    return f"""fileFormatVersion: 2
guid: {stable_guid(OUTPUT_NAME)}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 1
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 1
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def default_meta(name: str) -> str:
    return f"""fileFormatVersion: 2
guid: {stable_guid(name)}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def build() -> None:
    contract = load_contract()
    with Image.open(WORLD_COVER_PATH) as image:
        world_cover = np.asarray(image.resize((OUTPUT_SIZE, OUTPUT_SIZE), Image.Resampling.NEAREST),
                                 dtype=np.uint8)
    world_cover = apply_verified_surface_classes(world_cover, contract)
    with Image.open(NDVI_PATH) as image:
        ndvi = np.asarray(image.resize((OUTPUT_SIZE, OUTPUT_SIZE), Image.Resampling.BILINEAR),
                          dtype=np.uint8)[:, :, 1]
    control = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE, 4), dtype=np.uint8)
    control[:, :, 0] = world_cover
    control[:, :, 1] = np.where(ndvi == 255, 0, ndvi)

    road_image = Image.new("L", (OUTPUT_SIZE, OUTPUT_SIZE), 0)
    road_draw = ImageDraw.Draw(road_image)
    road_count = 0
    features = json.loads(OSM_PATH.read_text(encoding="utf-8"))["features"]
    for feature in features:
        tags = feature["properties"]["tags"]
        if "highway" not in tags:
            continue
        reference_width = road_width(tags)
        # 256px alphamap has an 8m texel footprint; a 6m minimum prevents narrow paths from disappearing.
        for line in geometry_lines(feature["geometry"]):
            points = [pixel_xy(float(point[0]), float(point[1]), contract) for point in line]
            if len(points) >= 2:
                road_draw.line(points, fill=255, width=max(6, reference_width), joint="curve")
        road_count += 1
    road_image = apply_verified_roads(road_image, contract, cartographic_border=False)
    control[:, :, 3] = np.asarray(road_image, dtype=np.uint8)

    UNITY_DIR.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(suffix=".png", delete=False, dir=UNITY_DIR) as stream:
        temp_path = Path(stream.name)
    try:
        Image.fromarray(control, "RGBA").save(temp_path, optimize=True)
        os.replace(temp_path, OUTPUT_PATH)
    finally:
        temp_path.unlink(missing_ok=True)

    (OUTPUT_PATH.with_suffix(OUTPUT_PATH.suffix + ".meta")).write_text(texture_meta(), encoding="utf-8")
    metadata = {
        "schemaVersion": 1,
        "mapName": MAP_NAME,
        "size": [OUTPUT_SIZE, OUTPUT_SIZE],
        "metersPerPixel": 1,
        "orientation": "top row north; left column west",
        "channels": {
            "r": "ESA WorldCover class code plus conservative satellite-verified pond corrections",
            "g": "Sentinel-2 NDVI P50 raw byte; NDVI=-1+raw*0.008; 0 also represents NoData",
            "b": "reserved",
            "a": "OSM road mask plus manually verified full-map road correction; minimum graybox width 6m",
        },
        "osmRoadFeatureCount": road_count,
        "verifiedRoadFeatureCount": len(load_verified_roads()["roads"]),
        "verifiedWaterBodyCount": len(load_verified_surface()["waterBodies"]),
        "sources": {
            WORLD_COVER_PATH.name: sha256_file(WORLD_COVER_PATH),
            NDVI_PATH.name: sha256_file(NDVI_PATH),
            OSM_PATH.name: sha256_file(OSM_PATH),
            VERIFIED_ROADS_PATH.name: sha256_file(VERIFIED_ROADS_PATH),
            VERIFIED_SURFACE_PATH.name: sha256_file(VERIFIED_SURFACE_PATH),
        },
        "outputSha256": sha256_file(OUTPUT_PATH),
    }
    METADATA_PATH.write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    (METADATA_PATH.with_suffix(METADATA_PATH.suffix + ".meta")).write_text(
        default_meta(METADATA_PATH.name), encoding="utf-8")


def verify() -> None:
    metadata = json.loads(METADATA_PATH.read_text(encoding="utf-8"))
    if metadata["outputSha256"] != sha256_file(OUTPUT_PATH):
        raise RuntimeError("Surface-control output hash mismatch")
    for name, expected in metadata["sources"].items():
        if sha256_file(SCRIPT_DIR / name) != expected:
            raise RuntimeError(f"Surface-control source hash mismatch: {name}")
    with Image.open(OUTPUT_PATH) as image:
        if image.mode != "RGBA" or image.size != (OUTPUT_SIZE, OUTPUT_SIZE):
            raise RuntimeError(f"Unexpected surface-control image: mode={image.mode}, size={image.size}")
        values = np.asarray(image)
        classes = set(np.unique(values[:, :, 0]).tolist())
        if not classes.issubset({0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 100}):
            raise RuntimeError(f"Unexpected WorldCover classes: {sorted(classes)}")
        if not np.any(values[:, :, 3] > 0):
            raise RuntimeError("Surface-control road mask is empty")
    print(json.dumps({
        "status": "passed",
        "output": str(OUTPUT_PATH),
        "osmRoadFeatureCount": metadata["osmRoadFeatureCount"],
        "verifiedRoadFeatureCount": metadata["verifiedRoadFeatureCount"],
        "verifiedWaterBodyCount": metadata["verifiedWaterBodyCount"],
    }, ensure_ascii=False))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("build", "verify"))
    args = parser.parse_args()
    if args.mode == "build":
        build()
    verify()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
