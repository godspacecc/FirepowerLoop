"""Offline verification entry point for HeJiaVillage feature references."""

import sys

sys.dont_write_bytecode = True

from GenerateFeatureReferences import main_verify
from GenerateSurfaceControl import verify as verify_surface_control


if __name__ == "__main__":
    main_verify()
    verify_surface_control()
