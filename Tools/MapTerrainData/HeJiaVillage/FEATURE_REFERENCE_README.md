# HeJiaVillage Feature References

Run `GenerateFeatureReferences.py acquire` with the declared dependencies to acquire fixed 2021 sources, clip them to the existing metadata.json extent, validate alignment, and publish the reference set. Run `VerifyFeatureReferences.py` for a completely offline verification of the published files.

The GeoTIFFs retain approximate 10 m / 20 m analytical sampling. The 2048×2048 PNGs are 1-pixel-per-metre display references only; they do not add source precision. Source disagreements remain visible and require manual judgement. See `FEATURE_REFERENCE_ATTRIBUTION.md`.

`GenerateSatelliteRoadMask.py` extracts bright linear roads from the already clipped Sentinel-2 RGBNIR GeoTIFF, merges them with the OSM alpha channel, and then replaces the full 2048 m map extent with the manually verified centerlines in `HeJiaVillage_VerifiedRoads.json`. The correction records only visible road topology and approximate centerlines from the 2026-09-22 Gaode standard-map view; provider raster/vector tiles are not stored. The script writes the north-up Unity minimap road mask at `Client/Assets/Game/MapTerrainSources/HeJiaVillage/HeJiaVillage_SatelliteRoadMask.png`. Road widths are visibility symbols rather than surveyed widths.

`GenerateSurfaceControl.py` combines WorldCover, Sentinel-2 NDVI and the verified road mask. It also applies `HeJiaVillage_VerifiedSurface.json`, which contains only conservative interiors of ponds clearly visible on the 2026-09-22 Gaode satellite layer. Ambiguous waterways and land-cover boundaries are deliberately left unchanged, and no provider imagery is stored.
