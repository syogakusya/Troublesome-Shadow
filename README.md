# Troublesome Shadow Pose Pipeline

This repository contains a reference implementation for streaming human pose data from Python-based pose estimators to a Unity avatar.

## Components

### Python PoseCaptureApp
* Located under `pose_capture/`.
* Provides a configurable application that requests skeleton samples from a `SkeletonProvider` (MediaPipe or OpenPose) and forwards them via WebSocket or UDP transports.
* Transports are defined in `pose_capture/transports.py` and support JSON payloads compatible with the Unity runtime.

### Unity Runtime
* Scripts live under `UnityProject/Assets/Scripts/` and are organised by concern (`Data`, `Networking`, `Processing`, `UI`).
* `PoseReceiver` manages network connectivity and buffers incoming skeleton frames.
* `SkeletonNormalizer`, `CalibrationManager`, and `BoneMapper` prepare incoming data for the avatar rig.
* `MotionSmoother` attenuates noise, while `AvatarController` drives the rig inside `LateUpdate`.
* `DiagnosticsPanel` surfaces live transport metrics and queue state for monitoring latency and connection health.

## Usage Overview

1. Instantiate a `SkeletonProvider` (e.g., `MediaPipeSkeletonProvider`) and a transport (`WebSocketSkeletonTransport` or `UDPSkeletonTransport`).
2. Construct a `CaptureConfig` and run the `PoseCaptureApp` event loop to stream skeleton frames to Unity.
3. In Unity, add the provided components to your scene:
   * Attach `PoseReceiver` to an object to receive frames from Python.
   * Configure `SkeletonNormalizer`, `CalibrationManager`, `BoneMapper`, and `MotionSmoother` to match your rig.
   * Assign these dependencies to `AvatarController` to drive the avatar.
   * Optionally add `DiagnosticsPanel` with a `Text` element for live status.
4. Implement reconnection and calibration flows as needed for your experience.

> **Note:** Real-time capture from MediaPipe/OpenPose requires installing their respective dependencies and providing camera frames, which is beyond the scope of this sample.
