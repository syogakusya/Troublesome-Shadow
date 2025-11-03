"""Skeleton providers for PoseCaptureApp."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, Iterable, List, Mapping, Optional
import logging

try:
    import mediapipe as mp  # type: ignore
except ImportError:  # pragma: no cover - optional dependency
    mp = None  # type: ignore

try:
    import pyopenpose as op  # type: ignore
except ImportError:  # pragma: no cover - optional dependency
    op = None  # type: ignore


LOGGER = logging.getLogger(__name__)


@dataclass
class Joint:
    """Represents a single joint."""

    name: str
    position: List[float]
    rotation: Optional[List[float]] = None
    confidence: float = 1.0


@dataclass
class SkeletonData:
    """Container for skeleton information."""

    joints: Dict[str, Joint] = field(default_factory=dict)
    timestamp_ms: int = 0
    metadata: Dict[str, object] = field(default_factory=dict)

    def to_dict(self) -> Mapping[str, object]:
        """Serialize to a JSON-compatible dict."""
        payload = {
            "timestamp": self.timestamp_ms,
            "joints": {
                name: {
                    "position": joint.position,
                    "rotation": joint.rotation,
                    "confidence": joint.confidence,
                }
                for name, joint in self.joints.items()
            },
        }
        if self.metadata:
            payload["meta"] = self.metadata
        return payload


class SkeletonProvider:
    """Abstract base class for skeleton providers."""

    joint_order: Iterable[str]

    def start(self) -> None:
        """Start the provider."""

    def get_latest(self) -> Optional[SkeletonData]:
        """Return the latest skeleton data, if any."""
        raise NotImplementedError

    def stop(self) -> None:
        """Stop the provider and release resources."""


class MediaPipeSkeletonProvider(SkeletonProvider):
    """Skeleton provider backed by MediaPipe."""

    def __init__(self, model_complexity: int = 1) -> None:
        if mp is None:  # pragma: no cover - optional dependency
            raise RuntimeError("mediapipe is not installed")
        self._pose = mp.solutions.pose.Pose(model_complexity=model_complexity)
        self.joint_order = [
            j.name for j in mp.solutions.pose.PoseLandmark
        ]  # type: ignore[attr-defined]

    def start(self) -> None:  # pragma: no cover - MediaPipe requires camera input
        LOGGER.info("Starting MediaPipe capture")

    def get_latest(self) -> Optional[SkeletonData]:  # pragma: no cover - MediaPipe requires camera input
        LOGGER.debug("Capturing frame via MediaPipe")
        # Implementation would capture a frame from the camera and process it.
        return None

    def stop(self) -> None:  # pragma: no cover - MediaPipe requires camera input
        LOGGER.info("Stopping MediaPipe capture")
        self._pose.close()


class OpenPoseSkeletonProvider(SkeletonProvider):
    """Skeleton provider backed by OpenPose."""

    def __init__(self, params: Optional[Mapping[str, str]] = None) -> None:
        if op is None:  # pragma: no cover - optional dependency
            raise RuntimeError("pyopenpose is not installed")
        params = dict(params or {})
        self._wrapper = op.WrapperPython()
        self._wrapper.configure(params)
        self._wrapper.start()
        self.joint_order = [f"joint_{i}" for i in range(25)]

    def start(self) -> None:  # pragma: no cover - OpenPose requires camera input
        LOGGER.info("Starting OpenPose capture")

    def get_latest(self) -> Optional[SkeletonData]:  # pragma: no cover - OpenPose requires camera input
        LOGGER.debug("Capturing frame via OpenPose")
        return None

    def stop(self) -> None:  # pragma: no cover - OpenPose requires camera input
        LOGGER.info("Stopping OpenPose capture")
        self._wrapper.stop()
