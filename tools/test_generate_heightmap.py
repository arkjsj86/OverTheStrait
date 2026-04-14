# tools/test_generate_heightmap.py
import numpy as np
import os
import sys
sys.path.insert(0, os.path.dirname(__file__))

from generate_heightmap import normalize_and_resize


def test_output_dtype_is_uint16():
    """결과 배열은 uint16 이어야 한다."""
    data = np.array([[-100.0, 0.0], [50.0, 300.0]], dtype=np.float32)
    result = normalize_and_resize(data, (2, 2))
    assert result.dtype == np.uint16


def test_output_range_within_uint16():
    """결과값은 0~65535 범위 내이어야 한다."""
    data = np.array([[-100.0, 0.0], [50.0, 300.0]], dtype=np.float32)
    result = normalize_and_resize(data, (2, 2))
    assert result.min() >= 0
    assert result.max() <= 65535


def test_min_max_maps_to_full_range():
    """최솟값 → 0, 최댓값 → 65535 으로 정규화되어야 한다."""
    data = np.array([[0.0, 100.0]], dtype=np.float32)
    result = normalize_and_resize(data, (1, 2))
    assert result[0, 0] == 0
    assert result[0, 1] == 65535


def test_output_shape_matches_target():
    """출력 shape 이 target_size 와 일치해야 한다."""
    data = np.ones((10, 20), dtype=np.float32)
    result = normalize_and_resize(data, (5, 7))
    assert result.shape == (5, 7)
