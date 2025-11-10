"""Graphical launcher for the pose capture app."""
from __future__ import annotations

import asyncio
import logging
import threading
from dataclasses import dataclass
from pathlib import Path
from tkinter import filedialog, messagebox
import tkinter as tk
from tkinter import ttk
from typing import Optional

from ..pose_capture_app import CaptureConfig, PoseCaptureApp, build_config_from_args, create_argument_parser

LOGGER = logging.getLogger(__name__)


@dataclass
class _LauncherArgs:
    """Argument container matching CLI options."""

    provider: str = "mediapipe"
    transport: str = "ws"
    endpoint: str = "0.0.0.0:9000/pose"
    frame_interval: float = 1 / 60
    calibration: Optional[Path] = None
    metadata: Optional[Path] = None
    seating_config: Optional[Path] = None
    camera: int = 0
    model_complexity: int = 1
    detection_confidence: float = 0.5
    tracking_confidence: float = 0.5
    image_width: Optional[int] = None
    image_height: Optional[int] = None
    preview: bool = False
    preview_window: str = "MediaPipe Pose"


class _CaptureWorker(threading.Thread):
    """Background thread that runs the async capture loop."""

    def __init__(self, config: CaptureConfig) -> None:
        super().__init__(daemon=True)
        self.config = config
        self.loop: Optional[asyncio.AbstractEventLoop] = None
        self._app: Optional[PoseCaptureApp] = None
        self.error: Optional[BaseException] = None

    def run(self) -> None:  # pragma: no cover - threading coordination
        try:
            self.loop = asyncio.new_event_loop()
            asyncio.set_event_loop(self.loop)
            self.loop.run_until_complete(self._run_app())
        except BaseException as exc:  # pragma: no cover - propagate to GUI thread
            self.error = exc
            LOGGER.exception("Capture worker terminated unexpectedly: %s", exc)
        finally:
            if self.loop:
                self.loop.close()
            self.loop = None

    async def _run_app(self) -> None:
        async with PoseCaptureApp(self.config) as app:
            self._app = app
            await app.run()

    def stop(self) -> None:  # pragma: no cover - threading coordination
        if not self.loop:
            return
        if self._app:
            future = asyncio.run_coroutine_threadsafe(self._app.stop(), self.loop)
            try:
                future.result(timeout=5)
            except Exception as exc:  # pragma: no cover - defensive guard
                LOGGER.exception("Failed to stop capture cleanly: %s", exc)


class PoseCaptureLauncherApp:
    """Tkinter UI for configuring and launching :mod:`pose_capture_app`."""

    def __init__(self, master: tk.Tk) -> None:
        self.master = master
        self.master.title("Pose Capture Launcher")
        self.master.protocol("WM_DELETE_WINDOW", self._on_close)

        self._parser = create_argument_parser()
        defaults = self._parser.parse_args([])
        self.args = _LauncherArgs(**vars(defaults))
        self._worker: Optional[_CaptureWorker] = None

        self._build_ui()
        self._sync_from_args()

    def _build_ui(self) -> None:
        frame = tk.Frame(self.master, padx=12, pady=12)
        frame.pack(fill=tk.BOTH, expand=True)

        row = 0

        def add_row(label: str, widget: tk.Widget) -> None:
            nonlocal row
            tk.Label(frame, text=label).grid(row=row, column=0, sticky=tk.W, pady=2)
            widget.grid(row=row, column=1, sticky=tk.EW, pady=2)
            row += 1

        self.provider_var = tk.StringVar(value=self.args.provider)
        provider_combo = ttk.Combobox(frame, textvariable=self.provider_var, values=["mediapipe"], state="readonly")
        add_row("プロバイダー", provider_combo)

        self.transport_var = tk.StringVar(value=self.args.transport)
        transport_combo = ttk.Combobox(frame, textvariable=self.transport_var, values=["ws", "udp"], state="readonly")
        add_row("トランスポート", transport_combo)

        self.endpoint_var = tk.StringVar(value=self.args.endpoint)
        endpoint_entry = tk.Entry(frame, textvariable=self.endpoint_var)
        add_row("エンドポイント", endpoint_entry)

        self.frame_interval_var = tk.DoubleVar(value=self.args.frame_interval)
        frame_interval_entry = tk.Entry(frame, textvariable=self.frame_interval_var)
        add_row("フレーム間隔 (秒)", frame_interval_entry)

        self.calibration_var = tk.StringVar(value=self._path_to_string(self.args.calibration))
        calibration_entry = tk.Entry(frame, textvariable=self.calibration_var)
        calibration_button = tk.Button(frame, text="参照", command=lambda: self._choose_file(self.calibration_var))
        add_row("キャリブレーション", calibration_entry)
        calibration_button.grid(row=row - 1, column=2, padx=4)

        self.metadata_var = tk.StringVar(value=self._path_to_string(self.args.metadata))
        metadata_entry = tk.Entry(frame, textvariable=self.metadata_var)
        metadata_button = tk.Button(frame, text="参照", command=lambda: self._choose_file(self.metadata_var))
        add_row("追加メタデータ", metadata_entry)
        metadata_button.grid(row=row - 1, column=2, padx=4)

        self.seating_var = tk.StringVar(value=self._path_to_string(self.args.seating_config))
        seating_entry = tk.Entry(frame, textvariable=self.seating_var)
        seating_button = tk.Button(frame, text="参照", command=lambda: self._choose_file(self.seating_var))
        add_row("座席レイアウト", seating_entry)
        seating_button.grid(row=row - 1, column=2, padx=4)

        self.camera_var = tk.IntVar(value=self.args.camera)
        camera_entry = tk.Entry(frame, textvariable=self.camera_var)
        add_row("カメラ番号", camera_entry)

        self.model_complexity_var = tk.IntVar(value=self.args.model_complexity)
        model_entry = tk.Entry(frame, textvariable=self.model_complexity_var)
        add_row("モデル複雑度", model_entry)

        self.detection_confidence_var = tk.DoubleVar(value=self.args.detection_confidence)
        detection_entry = tk.Entry(frame, textvariable=self.detection_confidence_var)
        add_row("検出信頼度", detection_entry)

        self.tracking_confidence_var = tk.DoubleVar(value=self.args.tracking_confidence)
        tracking_entry = tk.Entry(frame, textvariable=self.tracking_confidence_var)
        add_row("追跡信頼度", tracking_entry)

        self.image_width_var = tk.StringVar(value="" if self.args.image_width is None else str(self.args.image_width))
        width_entry = tk.Entry(frame, textvariable=self.image_width_var)
        add_row("画像幅", width_entry)

        self.image_height_var = tk.StringVar(value="" if self.args.image_height is None else str(self.args.image_height))
        height_entry = tk.Entry(frame, textvariable=self.image_height_var)
        add_row("画像高さ", height_entry)

        self.preview_var = tk.BooleanVar(value=self.args.preview)
        preview_check = tk.Checkbutton(frame, text="プレビューを表示", variable=self.preview_var)
        add_row("プレビュー", preview_check)

        self.preview_window_var = tk.StringVar(value=self.args.preview_window)
        preview_window_entry = tk.Entry(frame, textvariable=self.preview_window_var)
        add_row("プレビューウィンドウ名", preview_window_entry)

        frame.columnconfigure(1, weight=1)

        button_frame = tk.Frame(self.master, padx=12, pady=12)
        button_frame.pack(fill=tk.X)

        self.start_button = tk.Button(button_frame, text="開始", command=self._start_capture)
        self.start_button.pack(side=tk.LEFT)
        self.stop_button = tk.Button(button_frame, text="停止", command=self._stop_capture, state=tk.DISABLED)
        self.stop_button.pack(side=tk.LEFT, padx=8)

        self.status = tk.StringVar(value="待機中")
        tk.Label(self.master, textvariable=self.status, anchor=tk.W).pack(fill=tk.X, padx=12, pady=(0, 12))

    def _sync_from_args(self) -> None:
        self.provider_var.set(self.args.provider)
        self.transport_var.set(self.args.transport)
        self.endpoint_var.set(self.args.endpoint)
        self.frame_interval_var.set(self.args.frame_interval)
        self.calibration_var.set(self._path_to_string(self.args.calibration))
        self.metadata_var.set(self._path_to_string(self.args.metadata))
        self.seating_var.set(self._path_to_string(self.args.seating_config))
        self.camera_var.set(self.args.camera)
        self.model_complexity_var.set(self.args.model_complexity)
        self.detection_confidence_var.set(self.args.detection_confidence)
        self.tracking_confidence_var.set(self.args.tracking_confidence)
        self.image_width_var.set("" if self.args.image_width is None else str(self.args.image_width))
        self.image_height_var.set("" if self.args.image_height is None else str(self.args.image_height))
        self.preview_var.set(self.args.preview)
        self.preview_window_var.set(self.args.preview_window)

    def _choose_file(self, var: tk.StringVar) -> None:
        path = filedialog.askopenfilename()
        if path:
            var.set(path)

    def _collect_args(self) -> _LauncherArgs:
        args = _LauncherArgs()
        args.provider = self.provider_var.get()
        args.transport = self.transport_var.get()
        args.endpoint = self.endpoint_var.get()
        args.frame_interval = float(self.frame_interval_var.get())
        args.calibration = self._to_path(self.calibration_var.get())
        args.metadata = self._to_path(self.metadata_var.get())
        args.seating_config = self._to_path(self.seating_var.get())
        args.camera = int(self.camera_var.get())
        args.model_complexity = int(self.model_complexity_var.get())
        args.detection_confidence = float(self.detection_confidence_var.get())
        args.tracking_confidence = float(self.tracking_confidence_var.get())
        args.image_width = self._to_optional_int(self.image_width_var.get())
        args.image_height = self._to_optional_int(self.image_height_var.get())
        args.preview = self.preview_var.get()
        args.preview_window = self.preview_window_var.get()
        return args

    def _start_capture(self) -> None:
        if self._worker:
            messagebox.showinfo("実行中", "既にキャプチャが実行されています")
            return
        try:
            args = self._collect_args()
            config = build_config_from_args(args)
        except Exception as exc:
            LOGGER.exception("Failed to start capture: %s", exc)
            messagebox.showerror("起動エラー", str(exc))
            return
        self._worker = _CaptureWorker(config)
        self._worker.start()
        self.start_button.config(state=tk.DISABLED)
        self.stop_button.config(state=tk.NORMAL)
        self.status.set("キャプチャ実行中…")
        self.master.after(500, self._poll_worker)

    def _poll_worker(self) -> None:
        if not self._worker:
            return
        if self._worker.is_alive():
            self.master.after(500, self._poll_worker)
            return
        if self._worker.error:
            messagebox.showerror("エラー", f"キャプチャが異常終了しました: {self._worker.error}")
        self._worker = None
        self.start_button.config(state=tk.NORMAL)
        self.stop_button.config(state=tk.DISABLED)
        self.status.set("停止しました")

    def _stop_capture(self) -> None:
        if not self._worker:
            return
        self._worker.stop()
        self._worker.join(timeout=5)
        if self._worker.is_alive():  # pragma: no cover - defensive guard
            messagebox.showwarning("警告", "完全に停止できませんでした。アプリを再起動してください。")
        self._worker = None
        self.start_button.config(state=tk.NORMAL)
        self.stop_button.config(state=tk.DISABLED)
        self.status.set("停止しました")

    def _on_close(self) -> None:
        self._stop_capture()
        self.master.destroy()

    @staticmethod
    def _path_to_string(path: Optional[Path]) -> str:
        return str(path) if path else ""

    @staticmethod
    def _to_path(value: str) -> Optional[Path]:
        value = value.strip()
        return Path(value) if value else None

    @staticmethod
    def _to_optional_int(value: str) -> Optional[int]:
        value = value.strip()
        return int(value) if value else None


def launch() -> None:
    root = tk.Tk()
    PoseCaptureLauncherApp(root)
    root.mainloop()


if __name__ == "__main__":  # pragma: no cover - manual launch
    launch()
