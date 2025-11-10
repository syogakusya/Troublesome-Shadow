"""Interactive overlay for editing seating layouts inside the preview window."""
from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Callable, List, Optional, Tuple

try:  # pragma: no cover - optional dependency guarded at runtime
    import cv2  # type: ignore
except Exception:  # pragma: no cover - allow import without OpenCV for tests
    cv2 = None  # type: ignore

from .seating import SeatRegion, SeatingLayout

LOGGER = logging.getLogger(__name__)


@dataclass
class _SeatDraft:
    """Editable representation of a single seat."""

    seat_id: str
    x_min: float
    y_min: float
    x_max: float
    y_max: float

    def to_region(self) -> SeatRegion:
        return SeatRegion(
            seat_id=self.seat_id,
            x_min=self.x_min,
            y_min=self.y_min,
            x_max=self.x_max,
            y_max=self.y_max,
        )


class LiveSeatingEditor:
    """Handles in-preview seat editing interactions."""

    _HANDLE_RADIUS = 12
    _MIN_EXTENT = 0.02

    def __init__(
        self,
        window_name: str,
        *,
        on_layout_changed: Optional[Callable[[Optional[SeatingLayout]], None]] = None,
    ) -> None:
        if cv2 is None:  # pragma: no cover - optional dependency
            raise RuntimeError("OpenCV is required for the live seating editor")
        self.window_name = window_name
        self._on_layout_changed = on_layout_changed
        self._seats: List[_SeatDraft] = []
        self._selected_index: Optional[int] = None
        self._frame_width: int = 1
        self._frame_height: int = 1
        self._editing_enabled = False
        self._pending_create = False
        self._drag_state: Optional[str] = None
        self._drag_anchor: Optional[str] = None
        self._drag_start_px: Tuple[int, int] = (0, 0)
        self._drag_start_norm: Tuple[float, float, float, float] = (0.0, 0.0, 0.0, 0.0)
        self._create_start_px: Optional[Tuple[int, int]] = None
        self._status_message = "'E'キーで編集モード"
        self._mouse_attached = False
        self._last_mouse_position: Tuple[int, int] = (0, 0)

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------
    def bind(self) -> None:
        if self._mouse_attached:
            return
        try:
            cv2.setMouseCallback(self.window_name, self._handle_mouse)
        except cv2.error as exc:  # pragma: no cover - window lifecycle guard
            LOGGER.debug("Failed to bind mouse callback: %s", exc)
            return
        self._mouse_attached = True

    def set_on_layout_changed(self, callback: Optional[Callable[[Optional[SeatingLayout]], None]]) -> None:
        self._on_layout_changed = callback

    def set_layout(self, layout: Optional[SeatingLayout]) -> None:
        self._seats.clear()
        if layout:
            for seat in layout.seats:
                self._seats.append(
                    _SeatDraft(
                        seat_id=seat.seat_id,
                        x_min=seat.x_min,
                        y_min=seat.y_min,
                        x_max=seat.x_max,
                        y_max=seat.y_max,
                    )
                )
        self._selected_index = 0 if self._seats else None

    def render(self, frame) -> None:
        self._frame_height, self._frame_width = frame.shape[:2]
        for index, seat in enumerate(self._seats):
            color = (0, 200, 255) if index == self._selected_index and self._editing_enabled else (0, 160, 64)
            x1, y1, x2, y2 = self._seat_pixels(seat)
            cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)
            label = f"{seat.seat_id}"
            cv2.putText(frame, label, (x1 + 4, y1 + 18), cv2.FONT_HERSHEY_SIMPLEX, 0.5, color, 1, cv2.LINE_AA)
            if self._editing_enabled and index == self._selected_index:
                self._draw_handles(frame, x1, y1, x2, y2)
        if self._editing_enabled:
            info_lines = [
                "編集モード: 座席をドラッグで移動 / 角をドラッグでリサイズ",
                "Tab: 次の座席 / Delete: 削除 / N: 追加 / C: すべて削除 / E: 終了",
            ]
        else:
            info_lines = [self._status_message]
        for idx, text in enumerate(info_lines):
            cv2.putText(
                frame,
                text,
                (10, 24 + idx * 20),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (255, 255, 255),
                1,
                cv2.LINE_AA,
            )
        if self._pending_create and self._create_start_px:
            x1, y1 = self._create_start_px
            x2, y2 = self._last_mouse_position
            cv2.rectangle(frame, (x1, y1), (x2, y2), (200, 200, 200), 1, cv2.LINE_AA)

    def handle_key(self, key: int) -> None:
        if key < 0:
            return
        if key in (ord("e"), ord("E")):
            self._toggle_editing()
            return
        if not self._editing_enabled:
            return
        if key == 9:  # Tab
            self._cycle_selection()
        elif key in (127, 8):  # Delete / Backspace
            self._delete_selected()
        elif key in (ord("n"), ord("N")):
            self._begin_create()
        elif key in (ord("c"), ord("C")):
            self._clear_all()

    # ------------------------------------------------------------------
    # Mouse handling
    # ------------------------------------------------------------------
    def _handle_mouse(self, event, x, y, flags, param) -> None:  # pragma: no cover - requires GUI interaction
        self._last_mouse_position = (x, y)
        if not self._editing_enabled:
            return
        if self._pending_create:
            self._handle_create_mouse(event, x, y)
            return
        if event == cv2.EVENT_LBUTTONDOWN:
            self._start_drag(x, y)
        elif event == cv2.EVENT_MOUSEMOVE and self._drag_state:
            self._update_drag(x, y)
        elif event == cv2.EVENT_LBUTTONUP and self._drag_state:
            self._finish_drag(x, y)

    def _handle_create_mouse(self, event: int, x: int, y: int) -> None:
        if event == cv2.EVENT_LBUTTONDOWN:
            self._create_start_px = (x, y)
            self._drag_state = "create"
        elif event == cv2.EVENT_MOUSEMOVE and self._drag_state == "create" and self._create_start_px:
            pass  # preview rectangle is drawn in render()
        elif event == cv2.EVENT_LBUTTONUP and self._drag_state == "create" and self._create_start_px:
            sx, sy = self._create_start_px
            ex, ey = x, y
            if abs(ex - sx) >= 8 and abs(ey - sy) >= 8:
                self._finalize_new_seat(sx, sy, ex, ey)
            self._create_start_px = None
            self._drag_state = None
            self._pending_create = False
            self._status_message = "座席を追加しました"

    def _start_drag(self, x: int, y: int) -> None:
        index, handle = self._pick_seat(x, y)
        if index is None:
            return
        self._selected_index = index
        self._drag_start_px = (x, y)
        seat = self._seats[index]
        self._drag_start_norm = (seat.x_min, seat.y_min, seat.x_max, seat.y_max)
        if handle:
            self._drag_state = "resize"
            self._drag_anchor = handle
        else:
            self._drag_state = "move"
            self._drag_anchor = None

    def _update_drag(self, x: int, y: int) -> None:
        if self._selected_index is None:
            return
        seat = self._seats[self._selected_index]
        if self._drag_state == "move":
            dx = (x - self._drag_start_px[0]) / max(1, self._frame_width)
            dy = (y - self._drag_start_px[1]) / max(1, self._frame_height)
            width = self._drag_start_norm[2] - self._drag_start_norm[0]
            height = self._drag_start_norm[3] - self._drag_start_norm[1]
            x_min = self._clamp(self._drag_start_norm[0] + dx, 0.0, 1.0 - width)
            y_min = self._clamp(self._drag_start_norm[1] + dy, 0.0, 1.0 - height)
            seat.x_min = x_min
            seat.y_min = y_min
            seat.x_max = x_min + width
            seat.y_max = y_min + height
        elif self._drag_state == "resize" and self._drag_anchor:
            nx = x / max(1, self._frame_width)
            ny = y / max(1, self._frame_height)
            if "l" in self._drag_anchor:
                seat.x_min = self._clamp(nx, 0.0, seat.x_max - self._MIN_EXTENT)
            if "r" in self._drag_anchor:
                seat.x_max = self._clamp(nx, seat.x_min + self._MIN_EXTENT, 1.0)
            if "t" in self._drag_anchor:
                seat.y_min = self._clamp(ny, 0.0, seat.y_max - self._MIN_EXTENT)
            if "b" in self._drag_anchor:
                seat.y_max = self._clamp(ny, seat.y_min + self._MIN_EXTENT, 1.0)

    def _finish_drag(self, x: int, y: int) -> None:
        self._update_drag(x, y)
        self._drag_state = None
        self._drag_anchor = None
        self._emit_layout()

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------
    def _toggle_editing(self) -> None:
        self._editing_enabled = not self._editing_enabled
        if self._editing_enabled:
            self._status_message = "編集モードを開始しました"
        else:
            self._pending_create = False
            self._drag_state = None
            self._status_message = "編集モードを終了しました"

    def _cycle_selection(self) -> None:
        if not self._seats:
            return
        if self._selected_index is None:
            self._selected_index = 0
        else:
            self._selected_index = (self._selected_index + 1) % len(self._seats)

    def _delete_selected(self) -> None:
        if self._selected_index is None:
            return
        deleted = self._seats.pop(self._selected_index)
        LOGGER.info("Removed seat '%s' from live layout", deleted.seat_id)
        if not self._seats:
            self._selected_index = None
        else:
            self._selected_index = min(self._selected_index, len(self._seats) - 1)
        self._emit_layout()

    def _begin_create(self) -> None:
        self._pending_create = True
        self._create_start_px = None
        self._status_message = "新しい座席: 左ドラッグで配置"

    def _clear_all(self) -> None:
        if not self._seats:
            return
        self._seats.clear()
        self._selected_index = None
        self._emit_layout()
        self._status_message = "座席を全て削除しました"

    def _finalize_new_seat(self, sx: int, sy: int, ex: int, ey: int) -> None:
        x1, x2 = sorted([sx, ex])
        y1, y2 = sorted([sy, ey])
        width = max(1, self._frame_width)
        height = max(1, self._frame_height)
        x_min = self._clamp(x1 / width, 0.0, 1.0 - self._MIN_EXTENT)
        x_max = self._clamp(x2 / width, x_min + self._MIN_EXTENT, 1.0)
        y_min = self._clamp(y1 / height, 0.0, 1.0 - self._MIN_EXTENT)
        y_max = self._clamp(y2 / height, y_min + self._MIN_EXTENT, 1.0)
        seat_id = self._generate_seat_id()
        draft = _SeatDraft(seat_id=seat_id, x_min=x_min, y_min=y_min, x_max=x_max, y_max=y_max)
        self._seats.append(draft)
        self._selected_index = len(self._seats) - 1
        LOGGER.info("Added seat '%s' via live editor", seat_id)
        self._emit_layout()

    def _emit_layout(self) -> None:
        if not self._on_layout_changed:
            return
        if not self._seats:
            self._on_layout_changed(None)
            return
        try:
            layout = SeatingLayout(seat.to_region() for seat in self._seats)
        except ValueError as exc:  # pragma: no cover - defensive guard
            LOGGER.error("Failed to build seating layout: %s", exc)
            return
        self._on_layout_changed(layout)

    def _generate_seat_id(self) -> str:
        base = "seat"
        existing = {seat.seat_id for seat in self._seats}
        index = 1
        while True:
            candidate = f"{base}-{index:02d}"
            if candidate not in existing:
                return candidate
            index += 1

    def _seat_pixels(self, seat: _SeatDraft) -> Tuple[int, int, int, int]:
        x1 = int(seat.x_min * self._frame_width)
        y1 = int(seat.y_min * self._frame_height)
        x2 = int(seat.x_max * self._frame_width)
        y2 = int(seat.y_max * self._frame_height)
        return x1, y1, x2, y2

    def _draw_handles(self, frame, x1: int, y1: int, x2: int, y2: int) -> None:
        for cx, cy in [
            (x1, y1),
            (x2, y1),
            (x1, y2),
            (x2, y2),
        ]:
            cv2.circle(frame, (cx, cy), self._HANDLE_RADIUS // 2, (255, 255, 0), 1, cv2.LINE_AA)

    def _pick_seat(self, x: int, y: int) -> Tuple[Optional[int], Optional[str]]:
        threshold = self._HANDLE_RADIUS
        for index, seat in enumerate(self._seats):
            x1, y1, x2, y2 = self._seat_pixels(seat)
            if x1 <= x <= x2 and y1 <= y <= y2:
                if abs(x - x1) <= threshold and abs(y - y1) <= threshold:
                    return index, "lt"
                if abs(x - x2) <= threshold and abs(y - y1) <= threshold:
                    return index, "rt"
                if abs(x - x1) <= threshold and abs(y - y2) <= threshold:
                    return index, "lb"
                if abs(x - x2) <= threshold and abs(y - y2) <= threshold:
                    return index, "rb"
                return index, None
        return None, None

    @staticmethod
    def _clamp(value: float, lower: float, upper: float) -> float:
        return max(lower, min(upper, value))


__all__ = ["LiveSeatingEditor"]

