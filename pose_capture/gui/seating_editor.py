"""Interactive tool for authoring seating layout JSON files."""
from __future__ import annotations

import base64
import json
import logging
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional

try:  # pragma: no cover - optional dependency for camera capture
    import cv2  # type: ignore
except Exception:  # pragma: no cover - allow running without OpenCV
    cv2 = None  # type: ignore

import tkinter as tk
from tkinter import filedialog, messagebox, simpledialog

from ..seating import SeatRegion, SeatingLayout

LOGGER = logging.getLogger(__name__)


@dataclass
class SeatDraft:
    """Represents a seat bounding box while editing."""

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

    def to_payload(self) -> Dict[str, object]:
        return {
            "id": self.seat_id,
            "bounds": {
                "xMin": self.x_min,
                "xMax": self.x_max,
                "yMin": self.y_min,
                "yMax": self.y_max,
            },
        }


class SeatingEditorApp:
    """Tkinter-based editor for configuring seating layouts."""

    def __init__(self, master: tk.Tk) -> None:
        self.master = master
        self.master.title("Seating Layout Editor")
        self.master.protocol("WM_DELETE_WINDOW", self._on_close)

        self._background_image: Optional[tk.PhotoImage] = None
        self._image_width = 1
        self._image_height = 1

        self._seats: List[SeatDraft] = []
        self._seat_rectangles: Dict[str, int] = {}
        self._seat_labels: Dict[str, int] = {}
        self._current_action: Optional[str] = None
        self._start_x = 0
        self._start_y = 0
        self._draft_rectangle: Optional[int] = None

        self._build_ui()

    # ------------------------------------------------------------------
    # UI construction
    # ------------------------------------------------------------------
    def _build_ui(self) -> None:
        toolbar = tk.Frame(self.master)
        toolbar.pack(side=tk.TOP, fill=tk.X)

        tk.Button(toolbar, text="背景画像を読み込み", command=self._load_background).pack(side=tk.LEFT)
        tk.Button(toolbar, text="カメラから取得", command=self._capture_frame, state=tk.NORMAL if cv2 else tk.DISABLED).pack(side=tk.LEFT)
        tk.Button(toolbar, text="座席レイアウトを読み込み", command=self._load_layout).pack(side=tk.LEFT)
        tk.Button(toolbar, text="座席レイアウトを書き出し", command=self._save_layout).pack(side=tk.LEFT)

        editor_frame = tk.Frame(self.master)
        editor_frame.pack(fill=tk.BOTH, expand=True)

        self.canvas = tk.Canvas(editor_frame, background="#111111")
        self.canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        side_panel = tk.Frame(editor_frame)
        side_panel.pack(side=tk.RIGHT, fill=tk.Y)

        tk.Label(side_panel, text="座席一覧").pack(anchor=tk.W, padx=8, pady=(8, 0))
        self.seat_list = tk.Listbox(side_panel, height=12)
        self.seat_list.pack(fill=tk.Y, padx=8, pady=4)
        self.seat_list.bind("<<ListboxSelect>>", lambda event: self._highlight_selected())

        tk.Button(side_panel, text="座席を追加", command=self._begin_add_seat).pack(fill=tk.X, padx=8, pady=(8, 2))
        tk.Button(side_panel, text="座席名を変更", command=self._rename_seat).pack(fill=tk.X, padx=8, pady=2)
        tk.Button(side_panel, text="座席を削除", command=self._delete_seat).pack(fill=tk.X, padx=8, pady=2)

        self.status = tk.StringVar(value="背景画像を読み込んでください")
        tk.Label(self.master, textvariable=self.status, anchor=tk.W).pack(fill=tk.X, side=tk.BOTTOM)

        self.canvas.bind("<ButtonPress-1>", self._on_canvas_press)
        self.canvas.bind("<B1-Motion>", self._on_canvas_drag)
        self.canvas.bind("<ButtonRelease-1>", self._on_canvas_release)

    # ------------------------------------------------------------------
    # Background management
    # ------------------------------------------------------------------
    def _load_background(self) -> None:
        path_str = filedialog.askopenfilename(
            title="背景画像を選択", filetypes=[("Image Files", "*.png *.jpg *.jpeg *.bmp *.gif"), ("All Files", "*.*")]
        )
        if not path_str:
            return
        path = Path(path_str)
        try:
            image = tk.PhotoImage(file=str(path))
        except Exception as exc:  # pragma: no cover - Tk image errors
            messagebox.showerror("読み込みエラー", f"画像を読み込めませんでした: {exc}")
            return
        self._set_background_image(image)
        self.status.set(f"背景画像: {path.name}")

    def _capture_frame(self) -> None:
        if cv2 is None:
            messagebox.showwarning("カメラ未対応", "OpenCV が見つからなかったため、カメラ取得は利用できません。")
            return
        capture = cv2.VideoCapture(0)
        if not capture.isOpened():
            messagebox.showerror("カメラエラー", "カメラを初期化できませんでした")
            return

        def _worker() -> None:
            try:
                ret, frame = capture.read()
                if not ret:
                    raise RuntimeError("フレームを取得できませんでした")
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                success, encoded = cv2.imencode(".png", rgb)
                if not success:
                    raise RuntimeError("フレームのエンコードに失敗しました")
                data = base64.b64encode(encoded.tobytes()).decode("ascii")
                image = tk.PhotoImage(data=data, format="png")
            except Exception as exc:
                LOGGER.exception("Failed to capture frame: %s", exc)
                self.master.after(0, lambda: messagebox.showerror("カメラエラー", str(exc)))
            else:
                self.master.after(0, lambda: self._set_background_image(image))
                self.master.after(0, lambda: self.status.set("カメラからフレームを取得しました"))
            finally:
                capture.release()

        threading.Thread(target=_worker, daemon=True).start()

    def _set_background_image(self, image: tk.PhotoImage) -> None:
        self._background_image = image
        self._image_width = max(1, image.width())
        self._image_height = max(1, image.height())
        self.canvas.delete("all")
        self.canvas.config(width=self._image_width, height=self._image_height)
        self.canvas.create_image(0, 0, anchor=tk.NW, image=self._background_image, tags="background")
        self._redraw_all_seats()

    # ------------------------------------------------------------------
    # Seat management
    # ------------------------------------------------------------------
    def _begin_add_seat(self) -> None:
        if self._background_image is None:
            messagebox.showinfo("操作不可", "先に背景画像を読み込んでください")
            return
        self._current_action = "draw"
        self.status.set("座席をドラッグして配置してください")

    def _rename_seat(self) -> None:
        index = self._get_selected_index()
        if index is None:
            return
        seat = self._seats[index]
        new_id = simpledialog.askstring("座席名", "新しい座席IDを入力", initialvalue=seat.seat_id)
        if not new_id:
            return
        if any(existing.seat_id == new_id for existing in self._seats if existing is not seat):
            messagebox.showerror("重複", "同じIDの座席が既に存在します")
            return
        old_id = seat.seat_id
        seat.seat_id = new_id
        self._seat_rectangles.pop(old_id, None)
        label_id = self._seat_labels.pop(old_id, None)
        if label_id:
            self.canvas.delete(label_id)
        self._draw_and_track(seat)
        self._refresh_list()

    def _delete_seat(self) -> None:
        index = self._get_selected_index()
        if index is None:
            return
        seat = self._seats.pop(index)
        rect_id = self._seat_rectangles.pop(seat.seat_id, None)
        if rect_id:
            self.canvas.delete(rect_id)
        label_id = self._seat_labels.pop(seat.seat_id, None)
        if label_id:
            self.canvas.delete(label_id)
        self._refresh_list()
        self.status.set(f"座席 {seat.seat_id} を削除しました")

    def _get_selected_index(self) -> Optional[int]:
        selection = self.seat_list.curselection()
        if not selection:
            messagebox.showinfo("選択なし", "座席を選択してください")
            return None
        return selection[0]

    def _highlight_selected(self) -> None:
        for rect_id in self._seat_rectangles.values():
            self.canvas.itemconfig(rect_id, width=2)
        index = self.seat_list.curselection()
        if not index:
            return
        seat = self._seats[index[0]]
        rect_id = self._seat_rectangles.get(seat.seat_id)
        if rect_id:
            self.canvas.itemconfig(rect_id, width=4)

    def _draw_and_track(self, seat: SeatDraft) -> None:
        rect_id, label_id = self._draw_seat(seat)
        self._seat_rectangles[seat.seat_id] = rect_id
        self._seat_labels[seat.seat_id] = label_id

    def _draw_seat(self, seat: SeatDraft) -> tuple[int, int]:
        coords = self._seat_to_canvas(seat)
        rect_id = self.canvas.create_rectangle(*coords, outline="#00ff88", width=2, tags="seat")
        label_x = (coords[0] + coords[2]) / 2
        label_y = (coords[1] + coords[3]) / 2
        label_id = self.canvas.create_text(label_x, label_y, text=seat.seat_id, fill="#00ff88")
        return rect_id, label_id

    # ------------------------------------------------------------------
    # Canvas interactions
    # ------------------------------------------------------------------
    def _on_canvas_press(self, event: tk.Event) -> None:
        if self._current_action != "draw":
            return
        self._start_x, self._start_y = event.x, event.y
        if self._draft_rectangle:
            self.canvas.delete(self._draft_rectangle)
            self._draft_rectangle = None

    def _on_canvas_drag(self, event: tk.Event) -> None:
        if self._current_action != "draw":
            return
        if self._draft_rectangle is None:
            self._draft_rectangle = self.canvas.create_rectangle(
                self._start_x,
                self._start_y,
                event.x,
                event.y,
                outline="#ffaa00",
                dash=(4, 2),
            )
        else:
            self.canvas.coords(self._draft_rectangle, self._start_x, self._start_y, event.x, event.y)

    def _on_canvas_release(self, event: tk.Event) -> None:
        if self._current_action != "draw":
            return
        if self._draft_rectangle is None:
            return
        x0, y0, x1, y1 = self.canvas.coords(self._draft_rectangle)
        self.canvas.delete(self._draft_rectangle)
        self._draft_rectangle = None

        if abs(x1 - x0) < 5 or abs(y1 - y0) < 5:
            self.status.set("矩形が小さすぎます。もう一度お試しください")
            return

        seat_id = simpledialog.askstring("座席ID", "座席IDを入力してください")
        if not seat_id:
            self.status.set("座席IDが入力されませんでした")
            return
        if any(existing.seat_id == seat_id for existing in self._seats):
            messagebox.showerror("重複", "同じIDの座席が既に存在します")
            return

        seat = SeatDraft(
            seat_id=seat_id,
            x_min=self._clamp(min(x0, x1) / self._image_width),
            y_min=self._clamp(min(y0, y1) / self._image_height),
            x_max=self._clamp(max(x0, x1) / self._image_width),
            y_max=self._clamp(max(y0, y1) / self._image_height),
        )
        self._seats.append(seat)
        self._draw_and_track(seat)
        self._refresh_list()
        self.status.set(f"座席 {seat_id} を追加しました")
        self._current_action = None

    # ------------------------------------------------------------------
    # Layout persistence
    # ------------------------------------------------------------------
    def _load_layout(self) -> None:
        path_str = filedialog.askopenfilename(
            title="座席レイアウトを選択", filetypes=[("JSON Files", "*.json"), ("All Files", "*.*")]
        )
        if not path_str:
            return
        path = Path(path_str)
        try:
            layout = SeatingLayout.from_json(path)
        except Exception as exc:  # pragma: no cover - parsing guard
            messagebox.showerror("読み込みエラー", f"レイアウトを読み込めませんでした: {exc}")
            return
        self._seats = [
            SeatDraft(
                seat_id=seat.seat_id,
                x_min=seat.x_min,
                y_min=seat.y_min,
                x_max=seat.x_max,
                y_max=seat.y_max,
            )
            for seat in layout.seats
        ]
        self._refresh_list()
        self._redraw_all_seats()
        self.status.set(f"{path.name} を読み込みました")

    def _save_layout(self) -> None:
        if not self._seats:
            messagebox.showinfo("保存不可", "座席が存在しません")
            return
        path_str = filedialog.asksaveasfilename(
            title="座席レイアウトを書き出し",
            defaultextension=".json",
            filetypes=[("JSON Files", "*.json"), ("All Files", "*.*")],
        )
        if not path_str:
            return
        path = Path(path_str)
        payload = {"seats": [seat.to_payload() for seat in self._seats]}
        try:
            path.write_text(json.dumps(payload, indent=2))
        except Exception as exc:  # pragma: no cover - IO errors
            messagebox.showerror("保存エラー", f"レイアウトを書き出せませんでした: {exc}")
            return
        self.status.set(f"{path.name} に保存しました")

    # ------------------------------------------------------------------
    # Utility helpers
    # ------------------------------------------------------------------
    def _seat_to_canvas(self, seat: SeatDraft) -> List[float]:
        return [
            seat.x_min * self._image_width,
            seat.y_min * self._image_height,
            seat.x_max * self._image_width,
            seat.y_max * self._image_height,
        ]

    def _redraw_all_seats(self) -> None:
        self.canvas.delete("seat")
        for label_id in self._seat_labels.values():
            self.canvas.delete(label_id)
        self._seat_rectangles.clear()
        self._seat_labels.clear()
        for seat in self._seats:
            self._draw_and_track(seat)

    def _refresh_list(self) -> None:
        self.seat_list.delete(0, tk.END)
        for seat in self._seats:
            bounds = f"({seat.x_min:.2f}, {seat.y_min:.2f})-({seat.x_max:.2f}, {seat.y_max:.2f})"
            self.seat_list.insert(tk.END, f"{seat.seat_id} {bounds}")

    @staticmethod
    def _clamp(value: float) -> float:
        return max(0.0, min(1.0, value))

    def _on_close(self) -> None:
        self.master.destroy()


def launch() -> None:
    """Launch the seating editor GUI."""

    root = tk.Tk()
    SeatingEditorApp(root)
    root.mainloop()


if __name__ == "__main__":  # pragma: no cover - manual launch
    launch()
