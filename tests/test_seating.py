import pytest

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from pose_capture.providers import SkeletonData
from pose_capture.seating import SeatRegion, SeatingLayout


def build_skeleton(x: float, y: float) -> SkeletonData:
    skeleton = SkeletonData()
    skeleton.metadata = {
        "root_center_normalized": {"x": x, "y": y},
        "frame_dimensions": {"width": 1920, "height": 1080},
    }
    return skeleton


def test_layout_selects_correct_seat():
    layout = SeatingLayout(
        [
            SeatRegion("seat-a", 0.0, 0.0, 0.3, 0.8),
            SeatRegion("seat-b", 0.3, 0.0, 0.6, 0.8),
            SeatRegion("seat-c", 0.6, 0.0, 1.0, 0.8),
        ]
    )

    skeleton = build_skeleton(0.35, 0.5)
    metadata = layout.evaluate(skeleton)
    assert metadata is not None
    assert metadata["activeSeatId"] == "seat-b"
    seats = {entry["id"]: entry for entry in metadata["seats"]}
    assert seats["seat-b"]["occupied"]
    assert not seats["seat-a"]["occupied"]
    assert metadata["confidence"] == pytest.approx(1/3, rel=1e-6)


def test_layout_uses_pixel_coordinates_when_needed():
    layout = SeatingLayout(
        [
            SeatRegion("seat-a", 0.0, 0.0, 0.5, 0.5),
            SeatRegion("seat-b", 0.5, 0.0, 1.0, 0.5),
        ]
    )
    skeleton = SkeletonData()
    skeleton.metadata = {
        "root_center_pixel": {"x": 1200, "y": 200},
        "frame_dimensions": {"width": 1600, "height": 400},
    }
    metadata = layout.evaluate(skeleton)
    assert metadata is not None
    assert metadata["activeSeatId"] == "seat-b"


def test_layout_from_mapping_validation():
    payload = {
        "seats": [
            {"id": "s1", "bounds": {"xMin": 0.0, "xMax": 0.5, "yMin": 0.0, "yMax": 0.5}},
            {"id": "s2", "bounds": {"xMin": 0.5, "xMax": 1.0, "yMin": 0.0, "yMax": 0.5}},
        ]
    }
    layout = SeatingLayout.from_mapping(payload)
    assert len(layout.seats) == 2

    with pytest.raises(ValueError):
        SeatingLayout([])

    with pytest.raises(ValueError):
        SeatingLayout.from_mapping({"seats": [{"id": "s1"}]})
