"""Pose capture application entry point."""
from __future__ import annotations

import asyncio
import json
import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

from .providers import SkeletonProvider, SkeletonData
from .transports import SkeletonTransport

LOGGER = logging.getLogger(__name__)


@dataclass
class CaptureConfig:
    """Runtime configuration for the capture application."""

    provider: SkeletonProvider
    transport: SkeletonTransport
    frame_interval: float = 1 / 30
    calibration_file: Optional[Path] = None
    metadata: dict = field(default_factory=dict)


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
        if not self.config.metadata and not self._calibration_data:
            return skeleton
        skeleton.metadata = {
            **(self._calibration_data or {}),
            **self.config.metadata,
        }
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


async def main(config: CaptureConfig) -> None:
    """Helper to run the app until interrupted."""
    async with PoseCaptureApp(config) as app:
        try:
            await app.run()
        except asyncio.CancelledError:  # pragma: no cover - normal shutdown
            LOGGER.info("Pose capture cancelled")


if __name__ == "__main__":  # pragma: no cover - manual invocation
    import argparse

    parser = argparse.ArgumentParser(description="Stream skeleton data")
    parser.add_argument("transport", choices=["ws", "udp"], help="Transport type")
    parser.add_argument("endpoint", help="URI (for ws) or host:port (for udp)")
    parser.add_argument("--frame-interval", type=float, default=1 / 30)
    parser.add_argument("--calibration", type=Path)
    parser.add_argument("--metadata", type=Path, help="JSON file with metadata")
    args = parser.parse_args()

    raise SystemExit("Provider selection must be implemented by the integrator.")
