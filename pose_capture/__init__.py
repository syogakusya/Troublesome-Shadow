"""Pose capture package."""

from .pose_capture_app import PoseCaptureApp, CaptureConfig
from .providers import SkeletonData, MediaPipeSkeletonProvider, OpenPoseSkeletonProvider
from .seating import SeatingLayout, SeatRegion
from .transports import SkeletonTransport, WebSocketSkeletonTransport, UDPSkeletonTransport

__all__ = [
    "PoseCaptureApp",
    "CaptureConfig",
    "SkeletonData",
    "MediaPipeSkeletonProvider",
    "OpenPoseSkeletonProvider",
    "SeatingLayout",
    "SeatRegion",
    "SkeletonTransport",
    "WebSocketSkeletonTransport",
    "UDPSkeletonTransport",
]
