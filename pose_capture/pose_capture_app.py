"""Pose capture application entry point."""
from __future__ import annotations

import argparse
import asyncio
import json
import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

from .providers import SkeletonProvider, SkeletonData, MediaPipeSkeletonProvider
from .seating import SeatingLayout
from .transports import SkeletonTransport, WebSocketSkeletonTransport, UDPSkeletonTransport

LOGGER = logging.getLogger(__name__)


@dataclass
class CaptureConfig:
    """Runtime configuration for the capture application."""

    provider: SkeletonProvider
    transport: SkeletonTransport
    frame_interval: float = 1 / 30
    calibration_file: Optional[Path] = None
    metadata: dict = field(default_factory=dict)
    seating_layout: Optional[SeatingLayout] = None


class PoseCaptureApp:
    """Main loop that forwards skeleton data from providers to transports."""

    def __init__(self, config: CaptureConfig) -> None:
        self.config = config
        self._running = False
        self._calibration_data: Optional[dict] = None

    async def __aenter__(self) -> "PoseCaptureApp":
        await self.start()
        return self

    async def __aexit__(self, exc_type, exc, tb) -> None:
        await self.stop()

    async def start(self) -> None:
        LOGGER.info("Starting PoseCaptureApp")
        self._running = True
        self.config.provider.start()
        await self.config.transport.connect()
        if self.config.calibration_file:
            self._calibration_data = self._load_calibration(self.config.calibration_file)

    async def stop(self) -> None:
        LOGGER.info("Stopping PoseCaptureApp")
        self._running = False
        await self.config.transport.close()
        self.config.provider.stop()

    async def run(self) -> None:
        if not self._running:
            await self.start()
        while self._running:
            skeleton = self.config.provider.get_latest()
            if skeleton:
                enriched = self._apply_metadata(skeleton)
                await self.config.transport.send(enriched)
            await asyncio.sleep(self.config.frame_interval)

    def _apply_metadata(self, skeleton: SkeletonData) -> SkeletonData:
        merged = dict(skeleton.metadata or {})
        if self._calibration_data:
            merged.update(self._calibration_data)
        if self.config.metadata:
            merged.update(self.config.metadata)
        if self.config.seating_layout:
            seating_metadata = self.config.seating_layout.evaluate(skeleton)
            if seating_metadata:
                merged["seating"] = seating_metadata
        skeleton.metadata = merged
        LOGGER.debug("Enriched skeleton payload: %s", json.dumps(skeleton.to_dict()))
        return skeleton

    def _load_calibration(self, path: Path) -> dict:
        LOGGER.info("Loading calibration file from %s", path)
        if not path.exists():
            LOGGER.warning("Calibration file %s does not exist", path)
            return {}
        data = json.loads(path.read_text())
        LOGGER.debug("Calibration data: %s", data)
        return data


def create_argument_parser() -> argparse.ArgumentParser:
    """Construct an argument parser for the capture CLI and GUI."""

    parser = argparse.ArgumentParser(description="Stream skeleton data to Unity")
    parser.add_argument("--provider", choices=["mediapipe"], default="mediapipe")
    parser.add_argument("--transport", choices=["ws", "udp"], default="ws")
    parser.add_argument("--endpoint", default="0.0.0.0:9000/pose", help="WebSocket URI or UDP host:port")
    parser.add_argument("--frame-interval", type=float, default=1 / 60, help="Seconds between frames")
    parser.add_argument("--calibration", type=Path, help="Optional calibration JSON file")
    parser.add_argument("--metadata", type=Path, help="Optional metadata JSON file")
    parser.add_argument("--seating-config", type=Path, help="Optional seating configuration JSON file")
    parser.add_argument("--camera", type=int, default=0, help="Camera index for MediaPipe")
    parser.add_argument("--model-complexity", type=int, default=1, help="MediaPipe model complexity (0-2)")
    parser.add_argument("--detection-confidence", type=float, default=0.5)
    parser.add_argument("--tracking-confidence", type=float, default=0.5)
    parser.add_argument("--image-width", type=int, help="Requested camera width")
    parser.add_argument("--image-height", type=int, help="Requested camera height")
    parser.add_argument("--preview", action="store_true", help="Show a webcam preview with MediaPipe landmarks")
    parser.add_argument("--preview-window", default="MediaPipe Pose", help="Window title for the preview")
    parser.add_argument("--debug", action="store_true", help="Enable verbose debug logging")
    return parser


def _load_metadata(path: Optional[Path]) -> dict:
    if not path:
        return {}
    if not path.exists():
        LOGGER.warning("Metadata file %s was not found; ignoring", path)
        return {}
    try:
        return json.loads(path.read_text())
    except json.JSONDecodeError as exc:
        LOGGER.error("Failed to parse metadata JSON from %s: %s", path, exc)
        return {}


def _load_seating(path: Optional[Path]) -> Optional[SeatingLayout]:
    if not path:
        return None
    if not path.exists():
        LOGGER.warning("Seating config %s was not found; seating metadata disabled", path)
        return None
    try:
        return SeatingLayout.from_json(path)
    except Exception as exc:  # pragma: no cover - defensive parsing guard
        LOGGER.error("Failed to load seating config %s: %s", path, exc)
        return None


def _build_transport(transport: str, endpoint: str) -> SkeletonTransport:
    if transport == "ws":
        uri = endpoint if endpoint.startswith("ws://") or endpoint.startswith("wss://") else f"ws://{endpoint}"
        return WebSocketSkeletonTransport(uri=uri)
    if transport == "udp":
        if ":" not in endpoint:
            raise ValueError("UDP endpoint must be in host:port format")
        host, port_str = endpoint.rsplit(":", 1)
        return UDPSkeletonTransport(host=host or "127.0.0.1", port=int(port_str))
    raise ValueError(f"Unsupported transport type {transport!r}")


def _build_provider(args: "argparse.Namespace") -> SkeletonProvider:
    if args.provider == "mediapipe":
        image_size = None
        if args.image_width or args.image_height:
            image_size = (args.image_width or 0, args.image_height or 0)
        return MediaPipeSkeletonProvider(
            model_complexity=args.model_complexity,
            camera_index=args.camera,
            detection_confidence=args.detection_confidence,
            tracking_confidence=args.tracking_confidence,
            image_size=image_size,
            preview=getattr(args, "preview", False),
            preview_window=getattr(args, "preview_window", "MediaPipe Pose"),
        )
    raise ValueError(f"Unsupported provider {args.provider!r}")


def build_config_from_args(args: "argparse.Namespace") -> CaptureConfig:
    """Create a :class:`CaptureConfig` instance from parsed arguments."""

    provider = _build_provider(args)
    transport = _build_transport(args.transport, args.endpoint)
    metadata = _load_metadata(getattr(args, "metadata", None))
    seating_layout = _load_seating(getattr(args, "seating_config", None))

    return CaptureConfig(
        provider=provider,
        transport=transport,
        frame_interval=args.frame_interval,
        calibration_file=getattr(args, "calibration", None),
        metadata=metadata,
        seating_layout=seating_layout,
    )


def configure_logging(debug: bool = False) -> None:
    """Configure the root logger for CLI and GUI launchers."""

    level = logging.DEBUG if debug else logging.INFO
    logging.basicConfig(
        level=level,
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        force=True,
    )


async def main(args: Optional["argparse.Namespace"] = None) -> None:
    if args is None:
        parser = create_argument_parser()
        args = parser.parse_args()

    configure_logging(getattr(args, "debug", False))

    config = build_config_from_args(args)

    async with PoseCaptureApp(config) as app:
        await app.run()


if __name__ == "__main__":  # pragma: no cover - CLI entry point
    asyncio.run(main())
