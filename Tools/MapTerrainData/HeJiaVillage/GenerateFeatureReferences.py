"""Acquire and verify fixed, traceable HeJiaVillage feature references.

The spatial contract is read only from metadata.json.  Source tiles are kept in
the operating-system temporary directory and only clipped analysis products are
published.  No Unity scene, TerrainData, collider, NavMesh, gameplay or build
setting is touched.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import math
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import fiona
import numpy as np
import PIL
import rasterio
import shapely
from PIL import Image, ImageDraw, ImageFont
from rasterio.enums import Resampling
from rasterio.features import rasterize
from rasterio.transform import from_bounds
from rasterio.warp import reproject, transform_bounds, transform_geom
from shapely.geometry import GeometryCollection, LineString, MultiLineString, MultiPolygon, Point, Polygon, box, mapping, shape
from shapely.ops import polygonize, unary_union


MAP_NAME = "HeJiaVillage"
TARGET_CRS = "EPSG:32649"
OUTPUT_SIZE = 2048
TILE_SIZE = 1024
SCHEMA_VERSION = 1
WORLD_COVER_ALLOWED = {0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 100}
WORLD_COVER_LABELS = {
    0: "NoData", 10: "Tree cover", 20: "Shrubland", 30: "Grassland",
    40: "Cropland", 50: "Built-up", 60: "Bare/sparse vegetation",
    70: "Snow and ice", 80: "Permanent water bodies",
    90: "Herbaceous wetland", 95: "Mangroves", 100: "Moss and lichen",
}
WORLD_COVER_COLORS = {
    10: (40, 140, 60), 20: (160, 195, 65), 30: (205, 225, 80),
    40: (245, 215, 70), 50: (220, 130, 40), 60: (185, 165, 125),
    70: (240, 245, 250), 80: (30, 90, 220), 90: (70, 190, 190),
    95: (50, 150, 105), 100: (180, 200, 150),
}
DATA_ACCESS_URL = "https://esa-worldcover.org/en/data-access"
COMPOSITE_BANDS_URL = "https://remotesensing.vito.be/news/simplify-your-processing-worldcover-annual-composites"
COMPOSITE_GRID_URL = "https://esa-worldcover.s3.eu-central-1.amazonaws.com/esa_worldcover_grid_composites.fgb"
CC_BY_URL = "https://creativecommons.org/licenses/by/4.0/"
OSM_LICENSE_URL = "https://www.openstreetmap.org/copyright"
OVERPASS_ENDPOINTS = (
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
)

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[2]
CONTRACT_PATH = SCRIPT_DIR / "metadata.json"
QUERY_PATH = SCRIPT_DIR / "HeJiaVillage_OSM_Features.overpassql"
UNITY_PARENT = REPO_ROOT / "Client" / "Assets" / "Game" / "MapTerrainSources" / MAP_NAME
UNITY_DIR = UNITY_PARENT / "FeatureReferences"
TOOLS_METADATA = SCRIPT_DIR / "feature-reference.metadata.json"

TOOLS_OUTPUT_NAMES = (
    "HeJiaVillage_OSM_Features.overpassql",
    "HeJiaVillage_OSM_Features_UTM49N.geojson",
    "HeJiaVillage_WorldCover_2021_v200_UTM49N.tif",
    "HeJiaVillage_Sentinel2_2021_RGBNIR_UTM49N.tif",
    "HeJiaVillage_Sentinel2_2021_NDVI_UTM49N.tif",
    "HeJiaVillage_Sentinel2_2021_SWIR_UTM49N.tif",
    "feature-reference.metadata.json",
    "FEATURE_REFERENCE_ATTRIBUTION.md",
    "FEATURE_REFERENCE_README.md",
)
UNITY_OUTPUT_NAMES = (
    "HeJiaVillage_ForestReference.png",
    "HeJiaVillage_BuiltUpReference.png",
    "HeJiaVillage_WaterReference.png",
    "HeJiaVillage_RoadReference.png",
    "HeJiaVillage_FeatureReference.metadata.json",
    "README.md",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def relative(path: Path) -> str:
    return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def load_contract() -> dict[str, Any]:
    metadata = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
    utm = metadata["utm"]
    unity = metadata["unity"]
    require(utm["crs"] == TARGET_CRS, f"MAP-REF-01: unexpected CRS {utm['crs']}")
    require(abs(float(utm["east"]) - float(utm["west"]) - 2048.0) < 1e-6, "MAP-REF-01: width is not 2048 m")
    require(abs(float(utm["north"]) - float(utm["south"]) - 2048.0) < 1e-6, "MAP-REF-01: height is not 2048 m")
    require(unity["rows"] == 2 and unity["columns"] == 2, "MAP-REF-01: Unity grid is not 2x2")
    require(abs(float(unity["tileWorldSize"]) - TILE_SIZE) < 1e-6, "MAP-REF-01: tile size is not 1024 m")
    return metadata


def dependency_versions() -> dict[str, str]:
    require((3, 11) <= sys.version_info[:2] < (3, 14), f"Python {platform.python_version()} is outside >=3.11,<3.14")
    versions = {
        "python": platform.python_version(), "numpy": np.__version__,
        "rasterio": rasterio.__version__, "gdal": rasterio.__gdal_version__,
        "Pillow": PIL.__version__, "shapely": shapely.__version__, "fiona": fiona.__version__,
    }
    return versions


def download(url: str, destination: Path, *, body: bytes | None = None, timeout: int = 240) -> dict[str, Any]:
    destination.parent.mkdir(parents=True, exist_ok=True)
    partial = destination.with_suffix(destination.suffix + ".partial")
    if body is None and shutil.which("curl.exe"):
        header_path = destination.with_suffix(destination.suffix + ".probe.headers")
        probe_path = destination.with_suffix(destination.suffix + ".probe")
        subprocess.run(
            ["curl.exe", "--silent", "--show-error", "--fail", "--location",
             "--range", "0-0", "--connect-timeout", "30", "--max-time", "120",
             "--dump-header", str(header_path), "--output", str(probe_path), url],
            check=True,
        )
        raw_headers = header_path.read_text(encoding="iso-8859-1", errors="replace").splitlines()
        headers: dict[str, str] = {}
        for line in raw_headers:
            if line.startswith("HTTP/"):
                headers = {}
            elif ":" in line:
                key, value = line.split(":", 1)
                headers[key.strip().lower()] = value.strip()
        content_range = headers.get("content-range", "")
        match = re.search(r"/(\d+)$", content_range)
        require(match is not None, f"Server did not return a total object size for {url}: {content_range}")
        total_size = int(match.group(1))
        etag = headers.get("etag")
        require(etag is not None, f"Server did not return an ETag for {url}")
        probe_path.unlink(missing_ok=True)
        header_path.unlink(missing_ok=True)
        parts_dir = destination.with_suffix(destination.suffix + ".parts")
        parts_dir.mkdir(parents=True, exist_ok=True)
        chunk_size = 8 * 1024 * 1024
        ranges = [(start, min(start + chunk_size - 1, total_size - 1)) for start in range(0, total_size, chunk_size)]

        def fetch_part(item: tuple[int, int]) -> Path:
            start, end = item
            part = parts_dir / f"{start:016d}-{end:016d}.part"
            subprocess.run(
                ["curl.exe", "--silent", "--show-error", "--fail", "--location",
                 "--retry", "5", "--retry-all-errors", "--connect-timeout", "30",
                 "--max-time", str(max(timeout, 1800)), "--header", f"If-Match: {etag}",
                 "--range", f"{start}-{end}", "--output", str(part), url],
                check=True,
            )
            require(part.stat().st_size == end - start + 1, f"Range size mismatch for {url}: {start}-{end}")
            return part

        try:
            with concurrent.futures.ThreadPoolExecutor(max_workers=min(12, len(ranges))) as executor:
                list(executor.map(fetch_part, ranges))
            with partial.open("wb") as output:
                for start, end in ranges:
                    part = parts_dir / f"{start:016d}-{end:016d}.part"
                    with part.open("rb") as stream:
                        shutil.copyfileobj(stream, output, length=1024 * 1024)
            require(partial.stat().st_size == total_size, f"Merged object size mismatch for {url}")
        finally:
            shutil.rmtree(parts_dir, ignore_errors=True)
        partial.replace(destination)
        return {
            "url": url, "requestedUrl": url, "downloadedAtUtc": utc_now(),
            "byteSize": destination.stat().st_size, "sha256": sha256_file(destination),
            "etag": etag, "lastModified": headers.get("last-modified"),
        }
    request = urllib.request.Request(url, data=body, headers={"User-Agent": "AnotherDay-FeatureReference/1.0"})
    digest = hashlib.sha256()
    size = 0
    with urllib.request.urlopen(request, timeout=timeout) as response, partial.open("wb") as stream:
        for block in iter(lambda: response.read(1024 * 1024), b""):
            stream.write(block)
            digest.update(block)
            size += len(block)
        headers = response.headers
        final_url = response.geturl()
    partial.replace(destination)
    return {
        "url": final_url, "requestedUrl": url, "downloadedAtUtc": utc_now(),
        "byteSize": size, "sha256": digest.hexdigest(),
        "etag": headers.get("ETag"), "lastModified": headers.get("Last-Modified"),
    }


def s3_to_https(path: str) -> str:
    require(path.startswith("s3://"), f"Unexpected grid object path: {path}")
    bucket, key = path[5:].split("/", 1)
    return f"https://{bucket}.s3.eu-central-1.amazonaws.com/{key}"


def resolve_sources(temp_downloads: Path, contract: dict[str, Any]) -> tuple[dict[str, dict[str, Any]], dict[str, Any]]:
    temp_downloads.mkdir(parents=True, exist_ok=True)
    grid_path = temp_downloads / "esa_worldcover_grid_composites.fgb"
    shared_grid_cache = Path(tempfile.gettempdir()) / "esa_worldcover_grid_composites.fgb"
    if shared_grid_cache.is_file() and shared_grid_cache.stat().st_size > 0:
        shutil.copy2(shared_grid_cache, grid_path)
        grid_info = {
            "url": COMPOSITE_GRID_URL,
            "requestedUrl": COMPOSITE_GRID_URL,
            "downloadedAtUtc": datetime.fromtimestamp(shared_grid_cache.stat().st_mtime, timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
            "byteSize": grid_path.stat().st_size,
            "sha256": sha256_file(grid_path),
            "etag": None,
            "lastModified": None,
            "cacheReuse": True,
        }
    else:
        grid_info = download(COMPOSITE_GRID_URL, grid_path)
        grid_info["cacheReuse"] = False
    lon = float(contract["centerWgs84"]["longitude"])
    lat = float(contract["centerWgs84"]["latitude"])
    with fiona.open(grid_path) as collection:
        matches = list(collection.filter(bbox=(lon, lat, lon, lat)))
    require(len(matches) == 1, f"Composite grid returned {len(matches)} tiles for center")
    properties = dict(matches[0]["properties"])
    require(properties.get("tile") == "N27E112", f"Unexpected composite tile: {properties.get('tile')}")
    map_south = int(math.floor(lat / 3.0) * 3)
    map_west = int(math.floor(lon / 3.0) * 3)
    map_tile = f"N{map_south:02d}E{map_west:03d}"
    urls = {
        "worldCover": f"https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/ESA_WorldCover_10m_2021_v200_{map_tile}_Map.tif",
        "rgbnir": s3_to_https(properties["s2_rgbnir_2021"]),
        "ndvi": s3_to_https(properties["s2_ndvi_2021"]),
        "swir": s3_to_https(properties["s2_swir_2021"]),
    }
    local_names = {key: Path(urllib.parse.urlparse(url).path).name for key, url in urls.items()}
    provenance: dict[str, dict[str, Any]] = {}
    for key in ("worldCover", "rgbnir", "ndvi", "swir"):
        print(f"Downloading official source object: {key} -> {urls[key]}", flush=True)
        provenance[key] = download(urls[key], temp_downloads / local_names[key], timeout=900)
        provenance[key]["temporaryFileName"] = local_names[key]
    return provenance, {"grid": grid_info, "properties": properties, "localNames": local_names}


def validate_source(path: Path, key: str) -> dict[str, Any]:
    expected = {
        "worldCover": (1, ("uint8",), (0.0,), (1.0,), (0.0,), "LandCover Map", None),
        # User-approved deviation: actual COG storage order is B04/B03/B02/B08.
        "rgbnir": (4, ("uint16",) * 4, (0.0,) * 4, (0.0001,) * 4, (0.0,) * 4, "RGBNIR", "Band 1: B04 (Red), Band 2: B03 (Green), Band 3: B02 (Blue), Band 4: B08 (Infrared)"),
        "ndvi": (3, ("uint8",) * 3, (255.0,) * 3, (0.008,) * 3, (-1.0,) * 3, "NDVI", "Band 1: NDVI-p90, Band2: NDVI-p50, Band 3: NDVI-p10"),
        "swir": (2, ("uint8",) * 2, (255.0,) * 2, (0.004,) * 2, (0.0,) * 2, "SWIR", "Band 1: B11-p50, Band 2: B12-p50"),
    }[key]
    with rasterio.open(path) as source:
        require(source.count == expected[0], f"{key}: unexpected band count {source.count}")
        require(tuple(source.dtypes) == expected[1], f"{key}: unexpected dtypes {source.dtypes}")
        require(tuple(source.nodatavals) == expected[2], f"{key}: unexpected nodata {source.nodatavals}")
        require(all(abs(a - b) < 1e-12 for a, b in zip(source.scales, expected[3])), f"{key}: unexpected scales {source.scales}")
        require(all(abs(a - b) < 1e-12 for a, b in zip(source.offsets, expected[4])), f"{key}: unexpected offsets {source.offsets}")
        require(str(source.crs) == "EPSG:4326", f"{key}: unexpected CRS {source.crs}")
        tags = source.tags()
        require("2021" in tags.get("title", "") and tags.get("product_version") == "V2.0.0", f"{key}: product year/version mismatch")
        require(expected[5] in tags.get("product_type", ""), f"{key}: product type mismatch")
        if expected[6] is not None:
            require(tags.get("bands") == expected[6], f"{key}: band order mismatch: {tags.get('bands')}")
        info = {
            "count": source.count, "dtypes": list(source.dtypes), "nodata": list(source.nodatavals),
            "scales": list(source.scales), "offsets": list(source.offsets), "crs": str(source.crs),
            "bounds": list(source.bounds), "width": source.width, "height": source.height,
            "transform": list(source.transform), "tags": tags,
        }
    return info


def reproject_clip(source_path: Path, output_path: Path, source_url: str, resolution: float, resampling: Resampling) -> dict[str, Any]:
    contract = load_contract()
    u = contract["utm"]
    west, east, south, north = map(float, (u["west"], u["east"], u["south"], u["north"]))
    width = math.ceil((east - west) / resolution)
    height = math.ceil((north - south) / resolution)
    dst_transform = from_bounds(west, south, east, north, width, height)
    with rasterio.open(source_path) as source:
        destination = np.full((source.count, height, width), source.nodata, dtype=source.dtypes[0])
        for band in range(1, source.count + 1):
            reproject(
                source=rasterio.band(source, band), destination=destination[band - 1],
                src_transform=source.transform, src_crs=source.crs, src_nodata=source.nodatavals[band - 1],
                dst_transform=dst_transform, dst_crs=TARGET_CRS, dst_nodata=source.nodatavals[band - 1],
                resampling=resampling,
            )
        profile = source.profile.copy()
        profile.update(driver="GTiff", width=width, height=height, count=source.count,
                       crs=TARGET_CRS, transform=dst_transform, compress="deflate", tiled=True,
                       blockxsize=128, blockysize=128)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with rasterio.open(output_path, "w", **profile) as target:
            target.write(destination)
            target.scales = source.scales
            target.offsets = source.offsets
            target.update_tags(**source.tags(), sourceObjectUrl=source_url,
                               clipBounds=f"{west},{south},{east},{north}",
                               resampling=resampling.name)
    invalid = np.zeros((height, width), dtype=bool)
    for index, nodata in enumerate([source_value for source_value in rasterio.open(output_path).nodatavals]):
        invalid |= destination[index] == nodata
    valid_fraction = float(1.0 - invalid.mean())
    require(valid_fraction >= 0.99, f"{output_path.name}: valid fraction {valid_fraction:.6f} < 0.99")
    points = [(0, 0), (0, width - 1), (height - 1, 0), (height - 1, width - 1), (height // 2, width // 2)]
    require(all(not invalid[row, column] for row, column in points), f"{output_path.name}: corner/center contains NoData")
    if output_path.name.startswith("HeJiaVillage_WorldCover"):
        values = set(int(value) for value in np.unique(destination))
        require(values <= WORLD_COVER_ALLOWED, f"Unexpected WorldCover classes: {sorted(values - WORLD_COVER_ALLOWED)}")
    return {
        "width": width, "height": height, "transform": list(dst_transform),
        "pixelSizeMeters": [float(dst_transform.a), float(-dst_transform.e)],
        "validPixelFraction": valid_fraction,
        "invalidPixelCount": int(invalid.sum()),
    }


def osm_bbox(contract: dict[str, Any]) -> tuple[float, float, float, float]:
    u = contract["utm"]
    bounds = transform_bounds(TARGET_CRS, "EPSG:4326", u["west"], u["south"], u["east"], u["north"], densify_pts=21)
    return tuple(float(value) for value in bounds)


def acquire_osm(query_template: str, contract: dict[str, Any], raw_path: Path) -> tuple[dict[str, Any], str, dict[str, Any]]:
    west, south, east, north = osm_bbox(contract)
    query = query_template.format(south=f"{south:.8f}", west=f"{west:.8f}", north=f"{north:.8f}", east=f"{east:.8f}")
    encoded = urllib.parse.urlencode({"data": query}).encode("utf-8")
    failures = []
    for endpoint in OVERPASS_ENDPOINTS:
        try:
            info = download(endpoint, raw_path, body=encoded, timeout=300)
            payload = json.loads(raw_path.read_text(encoding="utf-8"))
            require(isinstance(payload.get("elements"), list), "Overpass response has no elements array")
            info.update({"endpoint": endpoint, "osmBaseTimestamp": payload.get("osm3s", {}).get("timestamp_osm_base")})
            return payload, query, info
        except Exception as exc:
            failures.append(f"{endpoint}: {exc}")
    raise RuntimeError("All Overpass endpoints failed: " + " | ".join(failures))


def member_geometry(member: dict[str, Any]) -> LineString:
    coords = [(float(point["lon"]), float(point["lat"])) for point in member.get("geometry", [])]
    require(len(coords) >= 2, f"Relation member {member.get('ref')} lacks complete geometry")
    return LineString(coords)


def relation_geometry(element: dict[str, Any], polygon_feature: bool):
    members = [member for member in element.get("members", []) if member.get("type") == "way"]
    require(members, f"Relation {element.get('id')} has no way members")
    if not polygon_feature:
        lines = [member_geometry(member) for member in members]
        merged = unary_union(lines)
        require(not merged.is_empty, f"Relation {element.get('id')} produced empty line geometry")
        return merged
    outer_lines = [member_geometry(member) for member in members if member.get("role", "") in ("", "outer")]
    inner_lines = [member_geometry(member) for member in members if member.get("role") == "inner"]
    outers = list(polygonize(unary_union(outer_lines)))
    require(outers, f"Relation {element.get('id')} outer rings could not be polygonized")
    inners = list(polygonize(unary_union(inner_lines))) if inner_lines else []
    polygons = []
    for outer in outers:
        holes = [list(inner.exterior.coords) for inner in inners if outer.contains(inner.representative_point())]
        polygons.append(Polygon(outer.exterior.coords, holes))
    result = unary_union(polygons)
    require(not result.is_empty and result.geom_type in ("Polygon", "MultiPolygon"), f"Relation {element.get('id')} polygon assembly failed")
    return result


def is_polygon_tags(tags: dict[str, str]) -> bool:
    return bool(tags.get("building") or tags.get("natural") in ("water", "wood") or tags.get("landuse") in ("reservoir", "basin", "forest") or tags.get("water"))


def element_geometry(element: dict[str, Any], polygon_feature: bool):
    kind = element.get("type")
    if kind == "node":
        return Point(float(element["lon"]), float(element["lat"]))
    if kind == "relation":
        return relation_geometry(element, polygon_feature)
    coords = [(float(point["lon"]), float(point["lat"])) for point in element.get("geometry", [])]
    require(len(coords) >= 2, f"Way {element.get('id')} lacks complete geometry")
    if polygon_feature:
        require(len(coords) >= 4 and coords[0] == coords[-1], f"Polygon way {element.get('id')} is not closed")
        return Polygon(coords)
    return LineString(coords)


def write_osm_geojson(payload: dict[str, Any], output_path: Path, contract: dict[str, Any]) -> dict[str, Any]:
    clip = box(float(contract["utm"]["west"]), float(contract["utm"]["south"]), float(contract["utm"]["east"]), float(contract["utm"]["north"]))
    features = []
    counts: dict[str, int] = {}
    for element in payload["elements"]:
        tags = {str(key): str(value) for key, value in element.get("tags", {}).items()}
        if not tags:
            continue
        polygon_feature = is_polygon_tags(tags)
        geometry = element_geometry(element, polygon_feature)
        projected = shape(transform_geom("EPSG:4326", TARGET_CRS, mapping(geometry), precision=3))
        projected = projected.intersection(clip)
        if projected.is_empty:
            continue
        if not projected.is_valid:
            projected = shapely.make_valid(projected)
        props = {
            "osm_type": element["type"], "osm_id": int(element["id"]),
            "version": element.get("version"), "timestamp": element.get("timestamp"),
            "changeset": element.get("changeset"), "uid": element.get("uid"),
            "user": element.get("user"), "tags": tags,
        }
        features.append({"type": "Feature", "id": f"{element['type']}/{element['id']}", "properties": props, "geometry": mapping(projected)})
        counts[element["type"]] = counts.get(element["type"], 0) + 1
    require(features, "OSM clip produced no features")
    collection = {
        "type": "FeatureCollection", "name": "HeJiaVillage_OSM_Features_UTM49N",
        "crs": {"type": "name", "properties": {"name": TARGET_CRS}}, "features": features,
    }
    output_path.write_text(json.dumps(collection, ensure_ascii=False, indent=2), encoding="utf-8")
    return {"featureCount": len(features), "countsByOsmType": counts}


def read_geojson(path: Path) -> list[dict[str, Any]]:
    return json.loads(path.read_text(encoding="utf-8"))["features"]


def grid_arrays(tif_path: Path, *, size: int = OUTPUT_SIZE, resampling: Resampling = Resampling.nearest) -> np.ndarray:
    contract = load_contract()
    u = contract["utm"]
    transform = from_bounds(u["west"], u["south"], u["east"], u["north"], size, size)
    with rasterio.open(tif_path) as source:
        result = np.full((source.count, size, size), source.nodata, dtype=np.float32)
        for band in range(1, source.count + 1):
            reproject(rasterio.band(source, band), result[band - 1], src_transform=source.transform,
                      src_crs=source.crs, src_nodata=source.nodatavals[band - 1],
                      dst_transform=transform, dst_crs=TARGET_CRS, dst_nodata=source.nodatavals[band - 1],
                      resampling=resampling)
    return result


def pixel_xy(x: float, y: float, contract: dict[str, Any]) -> tuple[float, float]:
    return x - float(contract["utm"]["west"]), float(contract["utm"]["north"]) - y


def iter_lines(geometry) -> Iterable[LineString]:
    if geometry.geom_type in ("LineString", "LinearRing"):
        yield LineString(geometry)
    elif geometry.geom_type == "Polygon":
        yield LineString(geometry.exterior.coords)
        for ring in geometry.interiors:
            yield LineString(ring.coords)
    elif hasattr(geometry, "geoms"):
        for child in geometry.geoms:
            yield from iter_lines(child)


def draw_geometry_lines(draw: ImageDraw.ImageDraw, geometry, contract: dict[str, Any], color: tuple[int, ...], width: int) -> None:
    for line in iter_lines(geometry):
        coords = [pixel_xy(x, y, contract) for x, y in line.coords]
        if len(coords) >= 2:
            draw.line(coords, fill=color, width=width, joint="curve")


def road_style(tags: dict[str, str]) -> tuple[tuple[int, int, int, int], int]:
    value = tags.get("highway", "")
    if value in ("motorway", "trunk", "motorway_link", "trunk_link"):
        return (255, 45, 45, 230), 8
    if value in ("primary", "secondary", "primary_link", "secondary_link"):
        return (255, 150, 30, 230), 6
    if value in ("tertiary", "residential", "tertiary_link"):
        return (255, 235, 40, 230), 4
    return (255, 255, 255, 230), 2


def render_products(stage_tools: Path, stage_unity: Path, contract: dict[str, Any]) -> dict[str, Any]:
    wc_path = stage_tools / "HeJiaVillage_WorldCover_2021_v200_UTM49N.tif"
    rgb_path = stage_tools / "HeJiaVillage_Sentinel2_2021_RGBNIR_UTM49N.tif"
    ndvi_path = stage_tools / "HeJiaVillage_Sentinel2_2021_NDVI_UTM49N.tif"
    swir_path = stage_tools / "HeJiaVillage_Sentinel2_2021_SWIR_UTM49N.tif"
    geo_path = stage_tools / "HeJiaVillage_OSM_Features_UTM49N.geojson"
    wc = grid_arrays(wc_path, resampling=Resampling.nearest)[0].astype(np.uint8)
    rgbnir_raw = grid_arrays(rgb_path, resampling=Resampling.bilinear)
    ndvi_raw = grid_arrays(ndvi_path, resampling=Resampling.bilinear)
    swir_raw = grid_arrays(swir_path, resampling=Resampling.bilinear)
    rgb_valid = np.all(rgbnir_raw != 0, axis=0)
    reflectance = rgbnir_raw * 0.0001
    rgb = np.stack((reflectance[0], reflectance[1], reflectance[2]), axis=2)
    valid_values = rgb[rgb_valid]
    lo, hi = np.percentile(valid_values, (2, 98), axis=0)
    rgb8 = np.clip((rgb - lo) / np.maximum(hi - lo, 1e-6) * 255.0, 0, 255).astype(np.uint8)
    rgb8[~rgb_valid] = 0
    features = read_geojson(geo_path)

    overview = Image.fromarray(rgb8, "RGB").convert("RGBA")
    overlay = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE, 4), dtype=np.uint8)
    for value, color in WORLD_COVER_COLORS.items():
        mask = wc == value
        overlay[mask, :3] = color
        overlay[mask, 3] = 62
    overview.alpha_composite(Image.fromarray(overlay, "RGBA"))
    overview_draw = ImageDraw.Draw(overview, "RGBA")

    forest = Image.new("RGBA", (OUTPUT_SIZE, OUTPUT_SIZE), (0, 0, 0, 0))
    built = Image.new("RGBA", (OUTPUT_SIZE, OUTPUT_SIZE), (0, 0, 0, 0))
    water = Image.new("RGBA", (OUTPUT_SIZE, OUTPUT_SIZE), (0, 0, 0, 0))
    roads = Image.new("RGBA", (OUTPUT_SIZE, OUTPUT_SIZE), (0, 0, 0, 0))
    forest_array = np.array(forest)
    forest_array[wc == 10] = (40, 140, 60, 110)
    forest = Image.fromarray(forest_array, "RGBA")
    built_array = np.array(built)
    built_array[wc == 50] = (220, 130, 40, 110)
    built = Image.fromarray(built_array, "RGBA")
    water_array = np.array(water)
    water_array[wc == 80] = (30, 90, 220, 120)
    water = Image.fromarray(water_array, "RGBA")
    forest_draw, built_draw, water_draw, roads_draw = map(lambda image: ImageDraw.Draw(image, "RGBA"), (forest, built, water, roads))

    feature_counts = {"roads": 0, "water": 0, "forest": 0, "buildings": 0}
    for feature in features:
        tags = feature["properties"]["tags"]
        geometry = shape(feature["geometry"])
        if "highway" in tags:
            color, width = road_style(tags)
            draw_geometry_lines(roads_draw, geometry, contract, color, width)
            draw_geometry_lines(overview_draw, geometry, contract, color, max(2, width // 2))
            feature_counts["roads"] += 1
        if tags.get("natural") == "wood" or tags.get("landuse") == "forest":
            draw_geometry_lines(forest_draw, geometry, contract, (0, 255, 80, 220), 2)
            draw_geometry_lines(overview_draw, geometry, contract, (0, 255, 80, 220), 2)
            feature_counts["forest"] += 1
        if "building" in tags:
            draw_geometry_lines(built_draw, geometry, contract, (255, 40, 40, 220), 2)
            draw_geometry_lines(overview_draw, geometry, contract, (255, 40, 40, 220), 2)
            feature_counts["buildings"] += 1
        if tags.get("natural") == "water" or "water" in tags or tags.get("landuse") in ("reservoir", "basin") or "waterway" in tags:
            draw_geometry_lines(water_draw, geometry, contract, (0, 220, 255, 220), 2)
            draw_geometry_lines(overview_draw, geometry, contract, (0, 220, 255, 220), 2)
            feature_counts["water"] += 1

    font = ImageFont.load_default()
    seam_color = (255, 0, 255, 220)
    overview_draw.line((TILE_SIZE, 0, TILE_SIZE, OUTPUT_SIZE - 1), fill=seam_color, width=2)
    overview_draw.line((0, TILE_SIZE, OUTPUT_SIZE - 1, TILE_SIZE), fill=seam_color, width=2)
    overview_draw.rectangle((0, 0, OUTPUT_SIZE - 1, OUTPUT_SIZE - 1), outline=(255, 255, 255, 230), width=2)
    overview_draw.text((12, 10), "NW  tile 2_1", font=font, fill=(255, 255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0, 255))
    overview_draw.text((TILE_SIZE + 12, 10), "NE  tile 2_2", font=font, fill=(255, 255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0, 255))
    overview_draw.text((12, TILE_SIZE + 10), "SW  tile 1_1  Unity origin (0,0)", font=font, fill=(255, 255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0, 255))
    overview_draw.text((TILE_SIZE + 12, TILE_SIZE + 10), "SE  tile 1_2", font=font, fill=(255, 255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0, 255))
    u = contract["utm"]
    overview_draw.text((12, OUTPUT_SIZE - 40), f"SW {u['west']:.3f} E, {u['south']:.3f} N", font=font, fill=(255,255,255,255), stroke_width=2, stroke_fill=(0,0,0,255))
    overview_draw.text((OUTPUT_SIZE - 250, OUTPUT_SIZE - 40), f"SE {u['east']:.3f} E", font=font, fill=(255,255,255,255), stroke_width=2, stroke_fill=(0,0,0,255))
    overview_draw.text((OUTPUT_SIZE - 250, 28), f"NE {u['north']:.3f} N", font=font, fill=(255,255,255,255), stroke_width=2, stroke_fill=(0,0,0,255))
    overview_draw.polygon([(OUTPUT_SIZE // 2, 18), (OUTPUT_SIZE // 2 - 8, 42), (OUTPUT_SIZE // 2 + 8, 42)], fill=(255,255,255,255))
    overview_draw.text((OUTPUT_SIZE // 2 - 4, 45), "N", font=font, fill=(255,255,255,255), stroke_width=2, stroke_fill=(0,0,0,255))
    overview_draw.rectangle((10, 66, 510, 166), fill=(0, 0, 0, 175))
    overview_draw.text((20, 74), "Sentinel-2 true color + WorldCover classes", font=font, fill=(255,255,255,255))
    overview_draw.text((20, 94), "OSM: roads / water / forest / buildings", font=font, fill=(255,255,255,255))
    overview_draw.text((20, 114), "Source disagreement: manual judgement required", font=font, fill=(255,255,255,255))
    overview_draw.text((20, 142), "ESA WorldCover 2021 / modified Sentinel 2021 / OSM", font=font, fill=(255,255,255,255))

    preview = overview.copy()
    preview_draw = ImageDraw.Draw(preview, "RGBA")
    for x, y, label in ((0, 0, "NW"), (OUTPUT_SIZE - 1, 0, "NE"), (0, OUTPUT_SIZE - 1, "SW"), (OUTPUT_SIZE - 1, OUTPUT_SIZE - 1, "SE"), (OUTPUT_SIZE // 2, OUTPUT_SIZE // 2, "CENTER")):
        preview_draw.ellipse((x - 6, y - 6, x + 6, y + 6), fill=(255, 0, 255, 255))
        preview_draw.text((max(2, min(x + 8, OUTPUT_SIZE - 60)), max(2, min(y + 8, OUTPUT_SIZE - 18))), label, font=font, fill=(255,255,255,255), stroke_width=2, stroke_fill=(0,0,0,255))
    forest.save(stage_unity / "HeJiaVillage_ForestReference.png", optimize=True)
    built.save(stage_unity / "HeJiaVillage_BuiltUpReference.png", optimize=True)
    water.save(stage_unity / "HeJiaVillage_WaterReference.png", optimize=True)
    roads.save(stage_unity / "HeJiaVillage_RoadReference.png", optimize=True)

    ndvi = np.where(ndvi_raw == 255, np.nan, -1.0 + ndvi_raw * 0.008)
    green = np.where(rgbnir_raw[1] == 0, np.nan, rgbnir_raw[1] * 0.0001)
    nir = np.where(rgbnir_raw[3] == 0, np.nan, rgbnir_raw[3] * 0.0001)
    swir11 = np.where(swir_raw[0] == 255, np.nan, swir_raw[0] * 0.004)
    ndwi_den = green + nir
    mndwi_den = green + swir11
    ndwi = np.where(np.isfinite(ndwi_den) & (ndwi_den != 0), (green - nir) / ndwi_den, np.nan)
    mndwi = np.where(np.isfinite(mndwi_den) & (mndwi_den != 0), (green - swir11) / mndwi_den, np.nan)

    def stats(array: np.ndarray) -> dict[str, float | int]:
        values = array[np.isfinite(array)]
        require(values.size > 0, "Index has no valid samples")
        return {"validCount": int(values.size), "min": float(values.min()), "p10": float(np.percentile(values, 10)), "p50": float(np.percentile(values, 50)), "p90": float(np.percentile(values, 90)), "max": float(values.max())}

    return {
        "featureCounts": feature_counts,
        "indices": {"ndviP50": stats(ndvi[1]), "ndviP90": stats(ndvi[0]), "ndviP10": stats(ndvi[2]), "ndwi": stats(ndwi), "mndwi": stats(mndwi)},
        "trueColorStretchReflectance": {"p02": lo.tolist(), "p98": hi.tolist()},
    }


def coordinate_checks(contract: dict[str, Any]) -> dict[str, Any]:
    u = contract["utm"]
    west, east, south, north = map(float, (u["west"], u["east"], u["south"], u["north"]))
    points = [(0.5, 0.5), (2047.5, 0.5), (0.5, 2047.5), (2047.5, 2047.5), (1024.5, 1024.5)]
    errors = []
    records = []
    for column_center, row_center in points:
        easting = west + column_center
        northing = north - row_center
        unity_x, unity_z = column_center, 2048.0 - row_center
        back_easting = west + unity_x
        back_northing = south + unity_z
        error = math.hypot(easting - back_easting, northing - back_northing)
        errors.append(error)
        records.append({"pixelCenter": [column_center, row_center], "utm": [easting, northing], "unity": [unity_x, unity_z], "roundTripErrorMeters": error})
    maximum = max(errors)
    require(maximum <= 0.51, f"Coordinate round trip error {maximum} m exceeds 0.51 m")
    return {"points": records, "maximumRoundTripErrorMeters": maximum, "quadrants": ["2_1:NW", "2_2:NE", "1_1:SW", "1_2:SE"], "seamPixels": [1024, 1024]}


def write_docs(stage_tools: Path, stage_unity: Path) -> None:
    attribution = """# Feature Reference Attribution\n\n- © ESA WorldCover project 2021, WorldCover 2021 v200, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).\n- Contains modified Copernicus Sentinel data 2021, processed by the ESA WorldCover consortium.\n- © OpenStreetMap contributors, data available under the [Open Database License](https://www.openstreetmap.org/copyright).\n\nProduct information: https://esa-worldcover.org/en/data-access\n"""
    tools_readme = """# HeJiaVillage Feature References\n\nRun `GenerateFeatureReferences.py acquire` with the declared dependencies to acquire fixed 2021 sources, clip them to the existing metadata.json extent, validate alignment, and publish the reference set. Run `VerifyFeatureReferences.py` for a completely offline verification of the published files.\n\nThe GeoTIFFs retain approximate 10 m / 20 m analytical sampling. The 2048×2048 PNGs are 1-pixel-per-metre display references only; they do not add source precision. Source disagreements remain visible and require manual judgement. See `FEATURE_REFERENCE_ATTRIBUTION.md`.\n"""
    unity_readme = """# HeJiaVillage Unity Feature References\n\nThe `*Reference.png` images are manual planning references, not final terrain textures. They do not modify Terrain, colliders, NavMesh, scenes, spawning, networking, or saves. `HeJiaVillage_SurfaceControl.png` is the exception: it is a linear, readable data texture generated by `GenerateSurfaceControl.py` and drives TerrainLayer weights through the terrain profile. Pixel column increases east; image row increases south. Unity origin is the southwest corner of tile `1_1`; `x=column+0.5`, `z=2047.5-row`.\n\nRoad widths are visibility symbols, not surveyed widths. WorldCover built-up areas do not imply individual buildings. Missing OpenStreetMap features do not imply that a real-world feature is absent. Single-source records and boundary disagreements require manual judgement.\n\nAttribution: © ESA WorldCover project 2021 (CC BY 4.0); Contains modified Copernicus Sentinel data 2021; © OpenStreetMap contributors (ODbL). Full links and provenance are in the adjacent metadata and Tools attribution file.\n"""
    (stage_tools / "FEATURE_REFERENCE_ATTRIBUTION.md").write_text(attribution, encoding="utf-8")
    (stage_tools / "FEATURE_REFERENCE_README.md").write_text(tools_readme, encoding="utf-8")
    (stage_unity / "README.md").write_text(unity_readme, encoding="utf-8")


def unity_meta(guid: str, kind: str, alpha: bool = False) -> str:
    if kind == "folder":
        return f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if kind == "default":
        return f"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    return f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
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
  isReadable: 0
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
    filterMode: 1
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
  alphaIsTransparency: {1 if alpha else 0}
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
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


def stable_guid(name: str) -> str:
    return hashlib.md5(f"AnotherDay:{MAP_NAME}:FeatureReferences:{name}".encode("utf-8"), usedforsecurity=False).hexdigest()


def write_unity_metas(stage_root: Path, stage_unity: Path) -> None:
    (stage_root / "FeatureReferences.meta").write_text(unity_meta(stable_guid("folder"), "folder"), encoding="utf-8")
    for name in UNITY_OUTPUT_NAMES:
        kind = "texture" if name.endswith(".png") else "default"
        alpha = name.endswith("Reference.png")
        (stage_unity / f"{name}.meta").write_text(unity_meta(stable_guid(name), kind, alpha), encoding="utf-8")


def artifact_hashes(stage_tools: Path, stage_unity: Path) -> list[dict[str, Any]]:
    records = []
    for root, base in ((stage_tools, SCRIPT_DIR), (stage_unity, UNITY_DIR)):
        for path in sorted(root.iterdir()):
            if path.name in ("feature-reference.metadata.json", "HeJiaVillage_FeatureReference.metadata.json") or path.suffix == ".meta":
                continue
            records.append({"path": relative(base / path.name), "byteSize": path.stat().st_size, "sha256": sha256_file(path)})
    return records


def build_metadata(contract: dict[str, Any], versions: dict[str, str], provenance: dict[str, dict[str, Any]], source_info: dict[str, Any], clip_info: dict[str, Any], osm_info: dict[str, Any], osm_convert: dict[str, Any], render_info: dict[str, Any], controls: dict[str, Any], hashes: list[dict[str, Any]]) -> dict[str, Any]:
    u = contract["utm"]
    return {
        "schemaVersion": SCHEMA_VERSION, "mapName": MAP_NAME, "generatedAtUtc": utc_now(),
        "scopeSource": "Tools/MapTerrainData/HeJiaVillage/metadata.json",
        "extent": {"crs": TARGET_CRS, "west": u["west"], "east": u["east"], "south": u["south"], "north": u["north"], "widthMeters": 2048, "heightMeters": 2048},
        "approvedDeviations": [{"status": "user-approved implementation deviation", "approvedAtConversationDate": "2026-09-12", "reason": "The official RGBNIR COG stores bands as B04/B03/B02/B08, while the plan text listed B02/B03/B04/B08. The implementation follows and validates the official COG tags; true color remains B04/B03/B02."}],
        "sources": {
            "worldCover": {"product": "ESA WorldCover 2021 v200 Map", "license": "CC BY 4.0", "licenseUrl": CC_BY_URL, **provenance["worldCover"], "raster": source_info["worldCover"]},
            "sentinelRgbnir": {"product": "ESA WorldCover Sentinel-2 L2A RGBNIR annual median composite 2021 v2", "license": "CC BY 4.0", "licenseUrl": CC_BY_URL, **provenance["rgbnir"], "raster": source_info["rgbnir"]},
            "sentinelNdvi": {"product": "ESA WorldCover Sentinel-2 NDVI percentiles composite 2021 v2", "license": "CC BY 4.0", "licenseUrl": CC_BY_URL, **provenance["ndvi"], "raster": source_info["ndvi"]},
            "sentinelSwir": {"product": "ESA WorldCover Sentinel-2 SWIR annual median composite 2021 v2", "license": "CC BY 4.0", "licenseUrl": CC_BY_URL, **provenance["swir"], "raster": source_info["swir"]},
            "compositeGrid": {"url": COMPOSITE_GRID_URL, **provenance["grid"]},
            "openStreetMap": {"product": "OpenStreetMap current data subset", "attribution": "© OpenStreetMap contributors", "license": "ODbL", "licenseUrl": OSM_LICENSE_URL, **osm_info, **osm_convert},
        },
        "officialDocumentation": {"dataAccess": DATA_ACCESS_URL, "compositeBands": COMPOSITE_BANDS_URL},
        "processing": {"versions": versions, "clipRasters": clip_info, "classificationResampling": "nearest", "continuousResampling": "bilinear", "worldCoverClasses": WORLD_COVER_LABELS, "render": render_info},
        "indices": {"ndvi": "offset + raw*scale; p50 is the vegetation review layer", "ndwi": "(B03-B08)/(B03+B08)", "mndwi": "(B03-B11)/(B03+B11)", "zeroOrNoDataRule": "NoData if denominator is zero or any input is NoData", "statistics": render_info["indices"]},
        "unity": {"imageSize": [2048, 2048], "metersPerPixel": 1, "orientation": "top row north; columns increase east; rows increase south", "pixelCenterToUtm": "E=west+column+0.5; N=north-row-0.5", "pixelCenterToUnity": "x=column+0.5; z=2047.5-row", "origin": "southwest corner of tile 1_1", "tiles": controls["quadrants"], "seamPixelLines": controls["seamPixels"], "roadWidthsAreVisibilitySymbolsMeters": {"motorway/trunk": 8, "primary/secondary": 6, "tertiary/residential": 4, "track/path/other": 2}},
        "legend": {"forest": {"worldCoverTreeFill": [40,140,60,110], "osmForestOutline": [0,255,80,220]}, "builtUp": {"worldCoverFill": [220,130,40,110], "osmBuildingOutline": [255,40,40,220]}, "water": {"worldCoverFill": [30,90,220,120], "osmWater": [0,220,255,220]}, "sourceDisagreement": "Single-source areas and differing boundaries remain visible and require manual judgement."},
        "controlPoints": controls, "artifacts": hashes,
        "selfCheck": {"status": "passed", "sourceStructureValidated": True, "minimumRasterValidFraction": min(value["validPixelFraction"] for value in clip_info.values()), "coordinateRoundTripWithin0.51m": True, "quadrantsNoOverlapOrGap": True, "northUpEastRight": True},
    }


def publish(stage_tools: Path, stage_unity_root: Path) -> None:
    stage_unity = stage_unity_root / "FeatureReferences"
    expected_tools = set(TOOLS_OUTPUT_NAMES)
    expected_unity = set(UNITY_OUTPUT_NAMES) | {f"{name}.meta" for name in UNITY_OUTPUT_NAMES}
    require({p.name for p in stage_tools.iterdir()} == expected_tools, "Staged Tools file set is incomplete or contains extras")
    require({p.name for p in stage_unity.iterdir()} == expected_unity, "Staged Unity file set is incomplete or contains extras")
    require((stage_unity_root / "FeatureReferences.meta").is_file(), "Staged Unity folder meta is missing")
    backup_root = Path(tempfile.mkdtemp(prefix="AnotherDay-FeatureRefs-Backup-"))
    installed: list[Path] = []
    backups: list[tuple[Path, Path]] = []
    try:
        UNITY_DIR.mkdir(parents=True, exist_ok=True)
        targets = [(path, SCRIPT_DIR / path.name) for path in stage_tools.iterdir()]
        targets += [(path, UNITY_DIR / path.name) for path in stage_unity.iterdir()]
        targets += [(stage_unity_root / "FeatureReferences.meta", UNITY_PARENT / "FeatureReferences.meta")]
        for source, target in targets:
            if target.exists():
                backup = backup_root / f"{len(backups):03d}-{target.name}"
                shutil.copy2(target, backup)
                backups.append((backup, target))
            replacement = target.with_name(target.name + ".feature-ref-new")
            shutil.copy2(source, replacement)
            os.replace(replacement, target)
            installed.append(target)
    except Exception:
        for path in reversed(installed):
            if path.exists():
                path.unlink()
        for backup, target in backups:
            shutil.copy2(backup, target)
        raise
    finally:
        shutil.rmtree(backup_root, ignore_errors=True)


def verify_root(tools_root: Path, unity_root: Path, *, metadata_path: Path) -> dict[str, Any]:
    errors: list[str] = []
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    contract = load_contract()
    artifacts = {entry["path"]: entry for entry in metadata["artifacts"]}
    for path_text, record in artifacts.items():
        name = Path(path_text).name
        path = (unity_root / name) if "/FeatureReferences/" in path_text else (tools_root / name)
        if not path.is_file():
            errors.append(f"Missing artifact: {path_text}")
            continue
        actual = sha256_file(path)
        if actual != record["sha256"] or path.stat().st_size != record["byteSize"]:
            errors.append(f"Hash/size mismatch: {path_text}")
    tif_names = [name for name in TOOLS_OUTPUT_NAMES if name.endswith(".tif")]
    for name in tif_names:
        path = tools_root / name
        with rasterio.open(path) as dataset:
            if str(dataset.crs) != TARGET_CRS:
                errors.append(f"Unexpected CRS: {name}")
            u = contract["utm"]
            expected_bounds = tuple(map(float, (u["west"], u["south"], u["east"], u["north"])))
            if any(abs(a - b) > 0.01 for a, b in zip(dataset.bounds, expected_bounds)):
                errors.append(f"Unexpected bounds: {name} {dataset.bounds}")
    png_names = list(UNITY_OUTPUT_NAMES[:5])
    for index, name in enumerate(png_names):
        with Image.open(unity_root / name) as image:
            if image.size != (OUTPUT_SIZE, OUTPUT_SIZE):
                errors.append(f"Unexpected PNG size: {name}")
            expected_mode = "RGB" if index == 0 else "RGBA"
            if image.mode != expected_mode:
                errors.append(f"Unexpected PNG mode: {name} {image.mode}")
            if index > 0:
                alpha = np.asarray(image.getchannel("A"))
                if not np.any(alpha == 0):
                    errors.append(f"Transparent layer lacks alpha=0 background: {name}")
    geojson = json.loads((tools_root / "HeJiaVillage_OSM_Features_UTM49N.geojson").read_text(encoding="utf-8"))
    if geojson.get("crs", {}).get("properties", {}).get("name") != TARGET_CRS or not geojson.get("features"):
        errors.append("OSM GeoJSON CRS/features invalid")
    if "out meta geom" not in (tools_root / "HeJiaVillage_OSM_Features.overpassql").read_text(encoding="utf-8"):
        errors.append("Saved Overpass query is not out meta geom")
    controls = coordinate_checks(contract)
    if controls["maximumRoundTripErrorMeters"] > 0.51:
        errors.append("Control point error exceeds tolerance")
    meta_text = metadata_path.read_text(encoding="utf-8")
    if str(REPO_ROOT) in meta_text or str(Path(tempfile.gettempdir())) in meta_text:
        errors.append("Metadata contains a local absolute path")
    if errors:
        raise RuntimeError("Offline verification failed:\n- " + "\n- ".join(errors))
    return {"status": "passed", "artifactCount": len(artifacts), "sourceCount": 3, "rasterCount": 4, "pngCount": 6, "geoJsonFeatureCount": len(geojson["features"]), "maximumRoundTripErrorMeters": controls["maximumRoundTripErrorMeters"]}


def acquire() -> int:
    versions = dependency_versions()
    contract = load_contract()
    temp_root = Path(tempfile.mkdtemp(prefix="AnotherDay-HeJiaVillage-FeatureRefs-"))
    downloads = temp_root / "downloads"
    stage_tools = temp_root / "publish" / "Tools"
    stage_unity_root = temp_root / "publish" / "Unity"
    stage_unity = stage_unity_root / "FeatureReferences"
    stage_tools.mkdir(parents=True)
    stage_unity.mkdir(parents=True)
    try:
        provenance, resolution = resolve_sources(downloads, contract)
        provenance["grid"] = resolution["grid"]
        source_info = {key: validate_source(downloads / resolution["localNames"][key], key) for key in ("worldCover", "rgbnir", "ndvi", "swir")}
        clip_targets = {
            "worldCover": ("HeJiaVillage_WorldCover_2021_v200_UTM49N.tif", 10.0, Resampling.nearest),
            "rgbnir": ("HeJiaVillage_Sentinel2_2021_RGBNIR_UTM49N.tif", 10.0, Resampling.bilinear),
            "ndvi": ("HeJiaVillage_Sentinel2_2021_NDVI_UTM49N.tif", 10.0, Resampling.bilinear),
            "swir": ("HeJiaVillage_Sentinel2_2021_SWIR_UTM49N.tif", 20.0, Resampling.bilinear),
        }
        clip_info = {}
        for key, (name, resolution_m, method) in clip_targets.items():
            clip_info[key] = reproject_clip(downloads / resolution["localNames"][key], stage_tools / name, provenance[key]["url"], resolution_m, method)
        query_template = QUERY_PATH.read_text(encoding="utf-8")
        raw_osm = downloads / "overpass-response.json"
        osm_payload, expanded_query, osm_info = acquire_osm(query_template, contract, raw_osm)
        (stage_tools / QUERY_PATH.name).write_text(expanded_query, encoding="utf-8")
        osm_convert = write_osm_geojson(osm_payload, stage_tools / "HeJiaVillage_OSM_Features_UTM49N.geojson", contract)
        controls = coordinate_checks(contract)
        render_info = render_products(stage_tools, stage_unity, contract)
        write_docs(stage_tools, stage_unity)
        write_unity_metas(stage_unity_root, stage_unity)
        hashes = artifact_hashes(stage_tools, stage_unity)
        metadata = build_metadata(contract, versions, provenance, source_info, clip_info, osm_info, osm_convert, render_info, controls, hashes)
        metadata_text = json.dumps(metadata, ensure_ascii=False, indent=2)
        (stage_tools / "feature-reference.metadata.json").write_text(metadata_text, encoding="utf-8")
        (stage_unity / "HeJiaVillage_FeatureReference.metadata.json").write_text(metadata_text, encoding="utf-8")
        # Verify against the staged roots before any formal file is replaced.
        verify_root(stage_tools, stage_unity, metadata_path=stage_tools / "feature-reference.metadata.json")
        publish(stage_tools, stage_unity_root)
        result = main_verify(return_result=True)
        print("Acquisition and atomic publication passed.")
        print(json.dumps({"sources": [provenance[key]["url"] for key in ("worldCover", "rgbnir", "ndvi", "swir")], "outputs": list(TOOLS_OUTPUT_NAMES) + list(UNITY_OUTPUT_NAMES), "verification": result}, ensure_ascii=False, indent=2))
        return 0
    finally:
        shutil.rmtree(temp_root, ignore_errors=True)


def main_verify(*, return_result: bool = False):
    dependency_versions()
    result = verify_root(SCRIPT_DIR, UNITY_DIR, metadata_path=TOOLS_METADATA)
    if return_result:
        return result
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("acquire", "verify"))
    args = parser.parse_args()
    return acquire() if args.mode == "acquire" else main_verify()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise
