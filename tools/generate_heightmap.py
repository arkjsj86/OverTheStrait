# tools/generate_heightmap.py
"""
Hormuz Strait Heightmap Generator

Usage:
    python generate_heightmap.py            # GEBCO 자동 다운로드
    python generate_heightmap.py input.tif  # 기존 GeoTIFF 사용
"""

import os
import sys
import zipfile
import requests
import numpy as np
import rasterio
from PIL import Image

# ── 설정 ─────────────────────────────────────────────────────
WEST, SOUTH, EAST, NORTH = 56.05, 26.35, 56.50, 26.75
HEIGHTMAP_SIZE = (1025, 1025)  # Unity heightmapResolution = 1025 → 정사각형

SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
OUTPUT_RAW   = os.path.join(PROJECT_ROOT, "UnityProject", "Assets", "Terrain", "heightmap_hormuz.raw")
OUTPUT_META  = os.path.join(PROJECT_ROOT, "UnityProject", "Assets", "Terrain", "heightmap_meta.txt")
TMP_ZIP      = os.path.join(SCRIPT_DIR, "tmp_gebco.zip")
TMP_TIF      = os.path.join(SCRIPT_DIR, "tmp_hormuz.tif")

GEBCO_URL = (
    "https://download.gebco.net/api/download"
    f"?sw_lat={SOUTH}&sw_lng={WEST}&ne_lat={NORTH}&ne_lng={EAST}"
    "&format=geotiff&layer=2023"
)
# ─────────────────────────────────────────────────────────────


def normalize_and_resize(data: np.ndarray, target_size: tuple) -> np.ndarray:
    """
    float32 고도 배열을 정규화하고 target_size 로 리샘플링해 uint16 반환.

    Args:
        data: 2D float32 배열 (임의 고도 범위)
        target_size: (height, width) 출력 픽셀 수

    Returns:
        2D uint16 배열, 값 범위 0~65535
    """
    min_val = float(data.min())
    max_val = float(data.max())
    span = (max_val - min_val) or 1.0

    normalized = ((data - min_val) / span).astype(np.float32)  # 0.0 ~ 1.0

    # PIL mode='F' (32-bit float) 로 LANCZOS 리샘플링
    img = Image.fromarray(normalized, mode='F')
    img = img.resize((target_size[1], target_size[0]), Image.LANCZOS)
    resampled = np.array(img, dtype=np.float32)

    return (np.clip(resampled, 0.0, 1.0) * 65535).astype(np.uint16)


def download_gebco(output_zip: str) -> None:
    print(f"GEBCO 데이터 다운로드 중...")
    resp = requests.get(GEBCO_URL, stream=True, timeout=120)
    resp.raise_for_status()
    with open(output_zip, "wb") as f:
        for chunk in resp.iter_content(chunk_size=8192):
            f.write(chunk)
    print(f"다운로드 완료: {output_zip}")


def extract_tif(zip_path: str, output_tif: str) -> None:
    with zipfile.ZipFile(zip_path, "r") as z:
        tif_files = [n for n in z.namelist() if n.lower().endswith(".tif")]
        if not tif_files:
            raise FileNotFoundError("zip 안에 .tif 파일이 없습니다.")
        z.extract(tif_files[0], path=os.path.dirname(output_tif))
        extracted = os.path.join(os.path.dirname(output_tif), tif_files[0])
        if os.path.abspath(extracted) != os.path.abspath(output_tif):
            os.replace(extracted, output_tif)
    print(f"추출 완료: {output_tif}")


def convert_to_raw(input_tif: str, output_raw: str, output_meta: str) -> None:
    with rasterio.open(input_tif) as ds:
        data = ds.read(1).astype(np.float32)

    min_val, max_val = float(data.min()), float(data.max())
    print(f"고도 범위: {min_val:.1f}m ~ {max_val:.1f}m")

    heightmap = normalize_and_resize(data, HEIGHTMAP_SIZE)

    os.makedirs(os.path.dirname(output_raw), exist_ok=True)

    # Unity Import Raw: 16-bit big-endian (Mac byte order)
    heightmap.byteswap().tofile(output_raw)
    print(f"RAW 저장: {output_raw}")

    # 해수면 Y 계산 (Unity Terrain Height = 500)
    span = (max_val - min_val) or 1.0
    sea_level_y = (-min_val / span) * 500.0

    meta = (
        f"Width: {HEIGHTMAP_SIZE[1]}\n"
        f"Height: {HEIGHTMAP_SIZE[0]}\n"
        f"Bit Depth: 16\n"
        f"Byte Order: Mac (big-endian)\n"
        f"Terrain Height (Unity): 500\n"
        f"Sea Level Y (Unity): {sea_level_y:.2f}\n"
    )
    with open(output_meta, "w", encoding="utf-8") as f:
        f.write(meta)
    print(f"메타데이터 저장: {output_meta}")
    print(f"\nUnity Import Raw 설정:")
    print(f"  Depth: 16 bit | Width: {HEIGHTMAP_SIZE[1]} | Height: {HEIGHTMAP_SIZE[0]}")
    print(f"  Byte Order: Mac | Terrain Size: 40000 x 500 x 20000")
    print(f"  해수면 Y: {sea_level_y:.2f}m")


def main() -> None:
    input_tif = sys.argv[1] if len(sys.argv) > 1 else None

    if input_tif is None:
        download_gebco(TMP_ZIP)
        extract_tif(TMP_ZIP, TMP_TIF)
        input_tif = TMP_TIF

    convert_to_raw(input_tif, OUTPUT_RAW, OUTPUT_META)

    for f in [TMP_ZIP, TMP_TIF]:
        if os.path.exists(f):
            os.remove(f)

    print("\n완료! Unity에서 Hormuz > Build Scene 메뉴를 실행하세요.")


if __name__ == "__main__":
    main()
