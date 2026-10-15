"""Apply conservative, manually verified surface-class corrections."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


SCRIPT_DIR = Path(__file__).resolve().parent
DATA_PATH = SCRIPT_DIR / "HeJiaVillage_VerifiedSurface.json"
WATER_CLASS = 80


def load_verified_surface() -> dict:
    return json.loads(DATA_PATH.read_text(encoding="utf-8"))


def _pixel_xy(point: list[float], contract: dict, output_size: int) -> tuple[float, float]:
    utm = contract["utm"]
    easting = float(utm["centerEasting"]) + float(point[0])
    northing = float(utm["centerNorthing"]) + float(point[1])
    column = (easting - float(utm["west"])) / (float(utm["east"]) - float(utm["west"])) * output_size
    row = (float(utm["north"]) - northing) / (float(utm["north"]) - float(utm["south"])) * output_size
    return column, row


def apply_verified_surface_classes(world_cover: np.ndarray, contract: dict) -> np.ndarray:
    """Override only the conservative interiors of features confirmed in satellite imagery."""
    if world_cover.ndim != 2 or world_cover.shape[0] != world_cover.shape[1]:
        raise ValueError(f"Expected a square class raster, got {world_cover.shape}")

    output_size = world_cover.shape[0]
    image = Image.fromarray(world_cover.astype(np.uint8), "L")
    draw = ImageDraw.Draw(image)
    for water_body in load_verified_surface()["waterBodies"]:
        center_x, center_y = _pixel_xy(water_body["centerLocalMeters"], contract, output_size)
        radius_x, radius_y = water_body["radiusMeters"]
        draw.ellipse((center_x - radius_x, center_y - radius_y,
                      center_x + radius_x, center_y + radius_y), fill=WATER_CLASS)
    return np.asarray(image, dtype=np.uint8)
