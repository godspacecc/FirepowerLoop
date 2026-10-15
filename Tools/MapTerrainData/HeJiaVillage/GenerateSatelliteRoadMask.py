"""Extract a north-up cartographic road mask from the local Sentinel-2 source."""

import json
from pathlib import Path

import cv2
import numpy as np
import rasterio
from PIL import Image

from VerifiedRoadCorrections import apply_verified_roads


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[2]
SOURCE = SCRIPT_DIR / "HeJiaVillage_Sentinel2_2021_RGBNIR_UTM49N.tif"
OUTPUT = (
    REPO_ROOT
    / "Client"
    / "Assets"
    / "Game"
    / "MapTerrainSources"
    / "HeJiaVillage"
    / "HeJiaVillage_SatelliteRoadMask.png"
)
OSM_CONTROL = (
    REPO_ROOT
    / "Client"
    / "Assets"
    / "Game"
    / "MapTerrainSources"
    / "HeJiaVillage"
    / "FeatureReferences"
    / "HeJiaVillage_SurfaceControl.png"
)
WORK_SIZE = 1024
OUTPUT_SIZE = 2048


def stretched_true_color() -> np.ndarray:
    with rasterio.open(SOURCE) as dataset:
        bands = dataset.read((1, 2, 3)).astype(np.float32)
    valid = np.all(bands != 0, axis=0)
    rgb = np.moveaxis(bands * 0.0001, 0, 2)
    values = rgb[valid]
    if values.size == 0:
        raise RuntimeError("Sentinel-2 RGB source contains no valid pixels")
    low, high = np.percentile(values, (2, 98), axis=0)
    rgb8 = np.clip((rgb - low) / np.maximum(high - low, 1e-6) * 255.0, 0, 255).astype(np.uint8)
    rgb8[~valid] = 0
    return cv2.resize(rgb8, (WORK_SIZE, WORK_SIZE), interpolation=cv2.INTER_CUBIC)


def ridge_response(gray: np.ndarray) -> np.ndarray:
    source = gray.astype(np.float32) / 255.0
    response = np.zeros_like(source)
    for sigma in (1.2, 2.0, 3.2, 4.8):
        blurred = cv2.GaussianBlur(source, (0, 0), sigma)
        dxx = cv2.Sobel(blurred, cv2.CV_32F, 2, 0, ksize=3)
        dyy = cv2.Sobel(blurred, cv2.CV_32F, 0, 2, ksize=3)
        dxy = cv2.Sobel(blurred, cv2.CV_32F, 1, 1, ksize=3)
        trace = dxx + dyy
        delta = np.sqrt(np.maximum(0.0, (dxx - dyy) ** 2 + 4.0 * dxy ** 2))
        first = 0.5 * (trace + delta)
        second = 0.5 * (trace - delta)
        swap = np.abs(first) > np.abs(second)
        lambda1 = np.where(swap, second, first)
        lambda2 = np.where(swap, first, second)
        ratio = np.abs(lambda1) / (np.abs(lambda2) + 1e-6)
        strength = np.sqrt(lambda1 * lambda1 + lambda2 * lambda2)
        vessel = np.exp(-(ratio * ratio) / (2.0 * 0.55 * 0.55))
        vessel *= 1.0 - np.exp(-(strength * strength) / (2.0 * 0.08 * 0.08))
        vessel[lambda2 >= 0.0] = 0.0
        response = np.maximum(response, vessel)
    return response


def centerlines(binary: np.ndarray) -> np.ndarray:
    remaining = binary.copy()
    skeleton = np.zeros_like(binary)
    element = cv2.getStructuringElement(cv2.MORPH_CROSS, (3, 3))
    while cv2.countNonZero(remaining) > 0:
        eroded = cv2.erode(remaining, element)
        opened = cv2.dilate(eroded, element)
        skeleton = cv2.bitwise_or(skeleton, cv2.subtract(remaining, opened))
        remaining = eroded
    return skeleton


def main() -> None:
    rgb = stretched_true_color()
    gray = cv2.cvtColor(rgb, cv2.COLOR_RGB2GRAY)
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
    local = cv2.GaussianBlur(gray, (0, 0), 9.0)
    ridges = ridge_response(gray)

    candidate = (
        (ridges >= 0.16)
        & (gray.astype(np.int16) >= local.astype(np.int16) + 5)
        & (gray >= 72)
        & (hsv[:, :, 1] <= 145)
    ).astype(np.uint8) * 255
    candidate = cv2.morphologyEx(candidate, cv2.MORPH_CLOSE,
                                 cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (13, 13)))

    count, labels, stats, _ = cv2.connectedComponentsWithStats(candidate, 8)
    filtered = np.zeros_like(candidate)
    for label in range(1, count):
        x, y, width, height, area = stats[label]
        span = max(width, height)
        if area >= 180 and span >= 45:
            filtered[labels == label] = 255

    road_lines = centerlines(filtered)
    road_lines = cv2.dilate(
        road_lines, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)), iterations=1
    )
    mask = cv2.resize(road_lines, (OUTPUT_SIZE, OUTPUT_SIZE), interpolation=cv2.INTER_NEAREST)
    osm_alpha = np.array(Image.open(OSM_CONTROL).convert("RGBA"), dtype=np.uint8)[:, :, 3]
    mask = np.maximum(mask, osm_alpha)
    border = cv2.dilate(mask, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
    mask = np.where(mask > 0, 255, np.where(border > 0, 128, 0)).astype(np.uint8)
    contract = json.loads((SCRIPT_DIR / "metadata.json").read_text(encoding="utf-8"))
    corrected = apply_verified_roads(Image.fromarray(mask, "L"), contract, cartographic_border=True)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    corrected.save(OUTPUT, optimize=True)
    print(f"Generated {OUTPUT.relative_to(REPO_ROOT)}")


if __name__ == "__main__":
    main()
