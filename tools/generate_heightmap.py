# tools/generate_heightmap.py
"""
Hormuz Strait Heightmap Generator

Downloads Copernicus DEM 90m tiles (publicly accessible, no auth required),
merges them, and converts to Unity Terrain 16-bit RAW heightmap.

Usage:
    python generate_heightmap.py            # 자동 다운로드 (Copernicus DEM)
    python generate_heightmap.py input.tif  # 기존 GeoTIFF 사용
"""

import math
import os
import sys
import shutil
import requests
import numpy as np
import rasterio
from rasterio.merge import merge as rasterio_merge
from PIL import Image

# ── 설정 ─────────────────────────────────────────────────────
# S(스타트: 페르시아만) → E(엔드: 오만만) 전체 구간
WEST, SOUTH, EAST, NORTH = 50.0, 22.0, 58.5, 27.5
HEIGHTMAP_SIZE = (1025, 1025)  # Unity heightmapResolution = 1025

SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
OUTPUT_RAW   = os.path.join(PROJECT_ROOT, "UnityProject", "Assets", "Terrain", "heightmap_hormuz.raw")
OUTPUT_META  = os.path.join(PROJECT_ROOT, "UnityProject", "Assets", "Terrain", "heightmap_meta.txt")
TMP_DIR      = os.path.join(SCRIPT_DIR, "tmp_tiles")

TILE_LIST_URL = "https://copernicus-dem-90m.s3.amazonaws.com/tileList.txt"
TILE_BASE_URL = "https://copernicus-dem-90m.s3.amazonaws.com"
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

    normalized = ((data - min_val) / span).astype(np.float32)

    img = Image.fromarray(normalized, mode='F')
    img = img.resize((target_size[1], target_size[0]), Image.LANCZOS)
    resampled = np.array(img, dtype=np.float32)

    return (np.clip(resampled, 0.0, 1.0) * 65535).astype(np.uint16)


def fetch_tile_list() -> set:
    """Copernicus DEM tileList.txt 다운로드 후 파싱."""
    print("타일 목록 가져오는 중...")
    resp = requests.get(TILE_LIST_URL, timeout=30)
    resp.raise_for_status()
    tiles = set(resp.text.splitlines())
    print(f"총 {len(tiles)}개 타일 확인됨")
    return tiles


def tile_name(lat: int, lon: int) -> str:
    """위경도 정수 좌표로 Copernicus DEM 타일 이름 생성."""
    lat_str = f"N{lat:02d}" if lat >= 0 else f"S{abs(lat):02d}"
    lon_str = f"E{lon:03d}" if lon >= 0 else f"W{abs(lon):03d}"
    return f"Copernicus_DSM_COG_30_{lat_str}_00_{lon_str}_00_DEM"


def download_tile(name: str, output_path: str) -> None:
    """단일 타일 다운로드."""
    url = f"{TILE_BASE_URL}/{name}/{name}.tif"
    resp = requests.get(url, stream=True, timeout=120)
    resp.raise_for_status()
    with open(output_path, "wb") as f:
        for chunk in resp.iter_content(chunk_size=65536):
            f.write(chunk)


def download_tiles(available_tiles: set) -> list:
    """bbox 내 존재하는 육지 타일을 모두 다운로드하고 경로 목록 반환."""
    os.makedirs(TMP_DIR, exist_ok=True)

    lat_range = range(math.floor(SOUTH), math.ceil(NORTH))
    lon_range = range(math.floor(WEST),  math.ceil(EAST))
    total_cells = len(lat_range) * len(lon_range)

    print(f"bbox 내 {total_cells}개 셀 확인 중 (위도 {len(lat_range)} × 경도 {len(lon_range)})...")

    downloaded, skipped = [], 0

    for lat in lat_range:
        for lon in lon_range:
            name = tile_name(lat, lon)
            if name not in available_tiles:
                skipped += 1  # 해양 타일 — 병합 시 0으로 처리
                continue

            out_path = os.path.join(TMP_DIR, f"{name}.tif")
            if os.path.exists(out_path):
                downloaded.append(out_path)
                continue

            try:
                print(f"  다운로드: {name}", end="\r")
                download_tile(name, out_path)
                downloaded.append(out_path)
            except Exception as e:
                print(f"  경고: {name} 실패 ({e})")

    print(f"\n육지 타일: {len(downloaded)}개 다운로드 / 해양 타일: {skipped}개 (0으로 처리)")
    return downloaded


def build_mosaic(tile_files: list) -> np.ndarray:
    """타일 병합 → bbox 크롭 → float32 배열 반환. 해양(빈 영역)은 0."""
    datasets = [rasterio.open(f) for f in tile_files]

    mosaic, _ = rasterio_merge(
        datasets,
        bounds=(WEST, SOUTH, EAST, NORTH),
        nodata=0.0,
    )

    for ds in datasets:
        ds.close()

    data = mosaic[0].astype(np.float32)
    data = np.where(data < -100, 0.0, data)   # nodata(-9999 등) → 0
    data = np.where(np.isnan(data), 0.0, data)
    # rasterio는 북→남 순서로 저장, Unity는 row 0 = Z=0 (남쪽) 순서로 읽음
    # flipud로 남→북 순서로 변환 → Unity 씬에서 북쪽(이란/페르시아만)이 상단에 위치
    data = np.flipud(data)
    return data


def from_geotiff(input_tif: str) -> np.ndarray:
    """기존 GeoTIFF에서 bbox 크롭 후 float32 배열 반환."""
    from rasterio.windows import from_bounds
    with rasterio.open(input_tif) as ds:
        window = from_bounds(WEST, SOUTH, EAST, NORTH, ds.transform)
        data = ds.read(1, window=window).astype(np.float32)
    return data


def convert_to_raw(data: np.ndarray, output_raw: str, output_meta: str) -> None:
    """2D float32 고도 배열을 Unity용 16-bit big-endian RAW + 메타데이터로 저장."""
    # ── 해안선 이진화 전처리 ────────────────────────────────────────────────
    # 해수면 ±5m 애매 영역을 제거해 배 콜라이더(반경 15m)와 터레인의 간헐적
    # 관통 문제를 해소한다. Stage 2 수심 gradient는 상수 offset으로 보존.
    # spec: docs/superpowers/specs/2026-04-16-heightmap-coast-binarization-design.md
    COAST_THRESHOLD = 5.0   # 이하 = 바다로 취급 (DEM 수직 오차 + 리샘플링 여유)
    LAND_LIFT       = 25.0  # 육지 +25m lift (배 콜라이더 15m + 안전 마진 10m)
    before_below = int(np.sum(data < COAST_THRESHOLD))
    before_above = int(np.sum(data >= COAST_THRESHOLD))
    data = np.where(data < COAST_THRESHOLD, 0.0, data + LAND_LIFT)
    print(f"해안선 이진화: 바다 {before_below}px / 육지 {before_above}px (+{LAND_LIFT:.0f}m lift)")
    # ──────────────────────────────────────────────────────────────────

    min_val, max_val = float(data.min()), float(data.max())
    print(f"고도 범위: {min_val:.1f}m ~ {max_val:.1f}m  |  입력 크기: {data.shape}")

    heightmap = normalize_and_resize(data, HEIGHTMAP_SIZE)

    os.makedirs(os.path.dirname(output_raw), exist_ok=True)

    # Unity Import Raw: 16-bit big-endian (Mac byte order)
    heightmap.byteswap().tofile(output_raw)
    print(f"RAW 저장: {output_raw}")

    UNITY_TERRAIN_H = 2000.0  # 시각적 과장 ×4 (실제 최대 ~2981m → Unity 2000m)
    span = (max_val - min_val) or 1.0
    sea_level_y = (-min_val / span) * UNITY_TERRAIN_H

    meta = (
        f"Width: {HEIGHTMAP_SIZE[1]}\n"
        f"Height: {HEIGHTMAP_SIZE[0]}\n"
        f"Bit Depth: 16\n"
        f"Byte Order: Mac (big-endian)\n"
        f"Terrain Height (Unity): {int(UNITY_TERRAIN_H)}\n"
        f"Sea Level Y (Unity): {sea_level_y:.2f}\n"
    )
    with open(output_meta, "w", encoding="utf-8") as f:
        f.write(meta)
    print(f"메타데이터 저장: {output_meta}")
    print(f"\nUnity Import Raw 설정:")
    print(f"  Depth: 16 bit | Width: {HEIGHTMAP_SIZE[1]} | Height: {HEIGHTMAP_SIZE[0]}")
    print(f"  Byte Order: Mac | Terrain Size: 56000 x {int(UNITY_TERRAIN_H)} x 40000")
    print(f"  해수면 Y: {sea_level_y:.2f}m")


def main() -> None:
    if len(sys.argv) > 1:
        print(f"GeoTIFF 파일 사용: {sys.argv[1]}")
        data = from_geotiff(sys.argv[1])
    else:
        available = fetch_tile_list()
        tile_files = download_tiles(available)

        if not tile_files:
            raise RuntimeError("다운로드된 타일이 없습니다. 네트워크 연결을 확인하세요.")

        print("타일 병합 중...")
        data = build_mosaic(tile_files)
        shutil.rmtree(TMP_DIR, ignore_errors=True)

    convert_to_raw(data, OUTPUT_RAW, OUTPUT_META)
    print("\n완료! Unity에서 Hormuz > Build Scene 메뉴를 실행하세요.")


if __name__ == "__main__":
    main()
