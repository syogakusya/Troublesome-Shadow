"""Skeleton providers for PoseCaptureApp."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, Iterable, List, Mapping, Optional, Tuple
import logging
import time

try:
    import cv2  # type: ignore
except ImportError:  # pragma: no cover - optional dependency
    cv2 = None  # type: ignore

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

        def _as_vec(values: Optional[List[float]], expected: int) -> Optional[Dict[str, float]]:
            if not values:
                return None
            padded = list(values[:expected]) + [0.0] * max(0, expected - len(values))
            if expected == 3:
                return {"x": padded[0], "y": padded[1], "z": padded[2]}
            if expected == 4:
                return {"x": padded[0], "y": padded[1], "z": padded[2], "w": padded[3]}
            raise ValueError(f"Unsupported vector size {expected}")

        payload = {
            "_timestamp": self.timestamp_ms,
            "_joints": [
                {
                    "_name": joint.name,
                    "_position": _as_vec(joint.position, 3),
                    "_rotation": _as_vec(joint.rotation, 4),
                    "_confidence": joint.confidence,
                }
                for joint in sorted(self.joints.values(), key=lambda j: j.name)
            ],
        }
        if self.metadata:
            payload["Meta"] = self.metadata
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
    """Skeleton provider backed by MediaPipe running on a webcam feed."""

    _IDENTITY_ROTATION: List[float] = [0.0, 0.0, 0.0, 1.0]

    def __init__(
        self,
        model_complexity: int = 1,
        camera_index: int = 0,
        detection_confidence: float = 0.5,
        tracking_confidence: float = 0.5,
        image_size: Optional[Tuple[int, int]] = None,
        preview: bool = False,
        preview_window: str = "MediaPipe Pose",
    ) -> None:
        if mp is None:  # pragma: no cover - optional dependency
            raise RuntimeError("mediapipe is not installed")
        if cv2 is None:  # pragma: no cover - optional dependency
            raise RuntimeError("opencv-python is not installed")

        self._pose = mp.solutions.pose.Pose(
            model_complexity=model_complexity,
            min_detection_confidence=detection_confidence,
            min_tracking_confidence=tracking_confidence,
        )
        self._camera_index = camera_index
        self._image_size = image_size
        self._capture: Optional["cv2.VideoCapture"] = None
        self._last_frame_fail = False
        self._preview_enabled = preview
        self._preview_window = preview_window
        self._drawing_utils = mp.solutions.drawing_utils
        self._drawing_styles = mp.solutions.drawing_styles
        self._pose_landmark_style = self._resolve_landmark_style()
        self._pose_connection_style = self._resolve_connection_style()
        self.joint_order = [
            j.name for j in mp.solutions.pose.PoseLandmark
        ]  # type: ignore[attr-defined]

    def _ensure_capture(self) -> None:
        if self._capture is not None and self._capture.isOpened():
            return
        self._capture = cv2.VideoCapture(self._camera_index)
        if not self._capture.isOpened():
            raise RuntimeError(f"Failed to open camera index {self._camera_index}")
        if self._image_size:
            width, height = self._image_size
            if width:
                self._capture.set(cv2.CAP_PROP_FRAME_WIDTH, width)
            if height:
                self._capture.set(cv2.CAP_PROP_FRAME_HEIGHT, height)

    def start(self) -> None:  # pragma: no cover - requires camera input
        LOGGER.info("Starting MediaPipe capture on camera %d", self._camera_index)
        self._ensure_capture()
        if self._preview_enabled:
            cv2.namedWindow(self._preview_window, cv2.WINDOW_NORMAL)

    def get_latest(self) -> Optional[SkeletonData]:  # pragma: no cover - requires camera input
        if self._capture is None:
            LOGGER.debug("MediaPipe provider has not been started yet")
            return None

        success, frame = self._capture.read()
        if not success:
            if not self._last_frame_fail:
                LOGGER.warning("Failed to read frame from camera %d", self._camera_index)
            self._last_frame_fail = True
            return None
        self._last_frame_fail = False

        image_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image_rgb.flags.writeable = False
        results = self._pose.process(image_rgb)

        if not results.pose_landmarks:
            LOGGER.debug("No pose landmarks detected in current frame")
            if self._preview_enabled:
                self._show_preview(frame, None)
            return None

        world_landmarks = getattr(results, "pose_world_landmarks", None)
        skeleton = SkeletonData(timestamp_ms=int(time.time() * 1000))

        for idx, landmark in enumerate(results.pose_landmarks.landmark):
            try:
                landmark_enum = mp.solutions.pose.PoseLandmark(idx)
                name = landmark_enum.name
            except ValueError:  # pragma: no cover - defensive guard
                LOGGER.debug("Skipping unknown landmark index %d", idx)
                continue

            if world_landmarks and idx < len(world_landmarks.landmark):
                world = world_landmarks.landmark[idx]
                position = [world.x, world.y, world.z]
            else:
                position = [landmark.x, landmark.y, getattr(landmark, "z", 0.0)]

            joint = Joint(
                name=name,
                position=position,
                rotation=self._IDENTITY_ROTATION,
                confidence=float(getattr(landmark, "visibility", 1.0)),
            )
            skeleton.joints[name] = joint

        skeleton.metadata = {
            "provider": "mediapipe",
            "camera_index": self._camera_index,
        }
        skeleton.metadata.update(
            self._build_root_metadata(
                landmarks=results.pose_landmarks.landmark,
                world_landmarks=getattr(world_landmarks, "landmark", None),
                frame_shape=frame.shape,
            )
        )

        if self._preview_enabled:
            self._show_preview(frame, results.pose_landmarks)
        return skeleton

    def stop(self) -> None:  # pragma: no cover - requires camera input
        LOGGER.info("Stopping MediaPipe capture")
        if self._capture is not None:
            self._capture.release()
        self._capture = None
        self._pose.close()
        if self._preview_enabled:
            try:
                cv2.destroyWindow(self._preview_window)
            except cv2.error:  # pragma: no cover - window already closed
                pass

    def _show_preview(self, bgr_frame, landmarks) -> None:
        annotated = bgr_frame.copy()
        if landmarks is not None:
            self._drawing_utils.draw_landmarks(
                annotated,
                landmarks,
                mp.solutions.pose.POSE_CONNECTIONS,
                landmark_drawing_spec=self._pose_landmark_style,
                connection_drawing_spec=self._pose_connection_style,
            )

        cv2.imshow(self._preview_window, annotated)
        key = cv2.waitKey(1) & 0xFF
        if key == 27:  # ESC pressed
            self._preview_enabled = False
            LOGGER.info("Disabling preview at user request (ESC pressed)")
            try:
                cv2.destroyWindow(self._preview_window)
            except cv2.error:
                pass
        else:
            try:
                visibility = cv2.getWindowProperty(self._preview_window, cv2.WND_PROP_VISIBLE)
                if visibility < 1:
                    self._preview_enabled = False
                    LOGGER.info("Preview window closed; disabling preview rendering")
            except cv2.error:
                self._preview_enabled = False

    def _resolve_landmark_style(self):
        getter = getattr(self._drawing_styles, "get_default_pose_landmarks_style", None)
        if callable(getter):
            try:
                return getter()
            except Exception as exc:  # pragma: no cover - defensive
                LOGGER.debug("Failed to obtain default pose landmark style: %s", exc)
        return self._drawing_utils.DrawingSpec(color=(0, 255, 0), thickness=2, circle_radius=3)

    def _resolve_connection_style(self):
        getter = getattr(self._drawing_styles, "get_default_pose_connections_style", None)
        if callable(getter):
            try:
                return getter()
            except Exception as exc:  # pragma: no cover - defensive
                LOGGER.debug("Failed to obtain default pose connection style: %s", exc)
        return self._drawing_utils.DrawingSpec(color=(0, 200, 255), thickness=2, circle_radius=2)

    def _build_root_metadata(self, landmarks, world_landmarks, frame_shape) -> Dict[str, object]:
        metadata: Dict[str, object] = {}
        try:
            left_idx = mp.solutions.pose.PoseLandmark.LEFT_HIP.value
            right_idx = mp.solutions.pose.PoseLandmark.RIGHT_HIP.value
            left = landmarks[left_idx]
            right = landmarks[right_idx]
        except (AttributeError, IndexError, TypeError):  # pragma: no cover - defensive guard
            return metadata

        root_x = float((left.x + right.x) * 0.5)
        root_y = float((left.y + right.y) * 0.5)
        root_z = float((getattr(left, "z", 0.0) + getattr(right, "z", 0.0)) * 0.5)
        metadata["root_center_normalized"] = {"x": root_x, "y": root_y, "z": root_z}

        if frame_shape is not None and len(frame_shape) >= 2:
            height = int(frame_shape[0])
            width = int(frame_shape[1])
            metadata["frame_dimensions"] = {"width": width, "height": height}
            metadata["root_center_pixel"] = {"x": root_x * width, "y": root_y * height}

        if world_landmarks:
            try:
                left_world = world_landmarks[left_idx]
                right_world = world_landmarks[right_idx]
            except (IndexError, TypeError):  # pragma: no cover - defensive guard
                pass
            else:
                world_x = float((left_world.x + right_world.x) * 0.5)
                world_y = float((left_world.y + right_world.y) * 0.5)
                world_z = float((left_world.z + right_world.z) * 0.5)
                metadata["root_center_world"] = {"x": world_x, "y": world_y, "z": world_z}

        return metadata


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
