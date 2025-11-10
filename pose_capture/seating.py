"""Seat layout utilities for pose-driven interactions."""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Mapping, Optional

from .providers import SkeletonData


@dataclass
class SeatRegion:
    """Describes the normalized bounds of a single seat in camera space."""

    seat_id: str
    x_min: float
    y_min: float
    x_max: float
    y_max: float

    def contains(self, x: float, y: float) -> bool:
        return self.x_min <= x <= self.x_max and self.y_min <= y <= self.y_max

    @property
    def width(self) -> float:
        return max(0.0, self.x_max - self.x_min)

    @property
    def height(self) -> float:
        return max(0.0, self.y_max - self.y_min)


class SeatingLayout:
    """Normalized description of seats for occupancy estimation."""

    def __init__(self, seats: Iterable[SeatRegion]) -> None:
        seats = list(seats)
        if not seats:
            raise ValueError("SeatingLayout requires at least one seat")
        seen: set[str] = set()
        ordered: List[SeatRegion] = []
        for seat in seats:
            if seat.seat_id in seen:
                raise ValueError(f"Duplicate seat id detected: {seat.seat_id}")
            seen.add(seat.seat_id)
            ordered.append(seat)
        self._seats = ordered

    @property
    def seats(self) -> List[SeatRegion]:
        return list(self._seats)

    def resolve(self, x: float, y: float) -> Optional[SeatRegion]:
        for seat in self._seats:
            if seat.contains(x, y):
                return seat
        return None

    def evaluate(self, skeleton: SkeletonData) -> Optional[Mapping[str, object]]:
        metadata = skeleton.metadata or {}
        normalized = _extract_normalized_root(metadata)
        if normalized is None:
            return None
        seat = self.resolve(normalized["x"], normalized["y"])
        occupancy: Dict[str, bool] = {seat_region.seat_id: False for seat_region in self._seats}
        confidence = 0.0
        active_seat_id: Optional[str] = None
        if seat:
            occupancy[seat.seat_id] = True
            active_seat_id = seat.seat_id
            confidence = _compute_confidence(seat, normalized["x"], normalized["y"])

        return {
            "activeSeatId": active_seat_id,
            "confidence": confidence,
            "seats": [
                {
                    "id": seat_region.seat_id,
                    "occupied": occupancy[seat_region.seat_id],
                    "bounds": {
                        "xMin": seat_region.x_min,
                        "xMax": seat_region.x_max,
                        "yMin": seat_region.y_min,
                        "yMax": seat_region.y_max,
                    },
                }
                for seat_region in self._seats
            ],
        }

    @classmethod
    def from_mapping(cls, payload: Mapping[str, object]) -> "SeatingLayout":
        seats_payload = payload.get("seats")
        if not isinstance(seats_payload, Iterable):
            raise ValueError("Seating layout payload must contain an iterable 'seats' field")
        seats: List[SeatRegion] = []
        for raw in seats_payload:
            if not isinstance(raw, Mapping):
                raise ValueError("Seat entry must be a mapping")
            seat_id = str(raw.get("id") or raw.get("seatId"))
            if not seat_id:
                raise ValueError("Seat entry missing 'id'")
            bounds = raw.get("bounds")
            if not isinstance(bounds, Mapping):
                raise ValueError(f"Seat '{seat_id}' missing 'bounds'")
            try:
                x_min = float(bounds.get("xMin"))
                x_max = float(bounds.get("xMax"))
                y_min = float(bounds.get("yMin"))
                y_max = float(bounds.get("yMax"))
            except (TypeError, ValueError) as exc:  # pragma: no cover - defensive guard
                raise ValueError(f"Invalid bounds for seat '{seat_id}': {exc}") from exc
            if x_min >= x_max or y_min >= y_max:
                raise ValueError(f"Seat '{seat_id}' has non-positive bounds")
            seats.append(SeatRegion(seat_id=seat_id, x_min=x_min, x_max=x_max, y_min=y_min, y_max=y_max))
        return cls(seats)

    @classmethod
    def from_json(cls, path: Path) -> "SeatingLayout":
        import json

        payload = json.loads(path.read_text())
        if not isinstance(payload, Mapping):
            raise ValueError("Seating config root must be a mapping")
        return cls.from_mapping(payload)


def _extract_normalized_root(metadata: Mapping[str, object]) -> Optional[Dict[str, float]]:
    root = metadata.get("root_center_normalized")
    if isinstance(root, Mapping):
        try:
            return {"x": float(root["x"]), "y": float(root["y"])}
        except (KeyError, TypeError, ValueError):
            pass
    pixel = metadata.get("root_center_pixel")
    frame = metadata.get("frame_dimensions")
    if isinstance(pixel, Mapping) and isinstance(frame, Mapping):
        try:
            x = float(pixel["x"]) / float(frame["width"])
            y = float(pixel["y"]) / float(frame["height"])
        except (KeyError, TypeError, ValueError, ZeroDivisionError):
            return None
        return {"x": x, "y": y}
    return None


def _compute_confidence(seat: SeatRegion, x: float, y: float) -> float:
    if seat.width <= 0 or seat.height <= 0:
        return 0.0
    margin_x = min(x - seat.x_min, seat.x_max - x)
    margin_y = min(y - seat.y_min, seat.y_max - y)
    if margin_x < 0 or margin_y < 0:
        return 0.0
    half_width = seat.width * 0.5
    half_height = seat.height * 0.5
    if half_width <= 0 or half_height <= 0:
        return 0.0
    normalized_margin = min(margin_x / half_width, margin_y / half_height)
    return max(0.0, min(1.0, normalized_margin))


__all__ = ["SeatRegion", "SeatingLayout"]
