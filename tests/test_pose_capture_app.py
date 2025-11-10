import asyncio


class DummyProvider:
    def start(self):
        pass

    def stop(self):
        pass

    def get_latest(self):
        return None


class DummyTransport:
    async def connect(self):
        pass

    async def close(self):
        pass

    async def send(self, skeleton):
        pass


class StubLayout:
    def __init__(self, payload):
        self._payload = payload

    def evaluate(self, skeleton):
        return self._payload


def test_apply_metadata_overwrites_previous_seating():
    from pose_capture.pose_capture_app import CaptureConfig, PoseCaptureApp
    from pose_capture.providers import SkeletonData

    skeleton = SkeletonData()
    skeleton.metadata = {"seating": {"activeSeatId": "seat-a"}}

    layout = StubLayout({"activeSeatId": "seat-b"})

    config = CaptureConfig(
        provider=DummyProvider(),
        transport=DummyTransport(),
        frame_interval=1 / 30,
        calibration_file=None,
        metadata={},
        seating_layout=layout,
    )

    app = PoseCaptureApp(config)
    updated = app._apply_metadata(skeleton)

    assert updated.metadata["seating"]["activeSeatId"] == "seat-b"


def test_apply_metadata_runs_without_additional_metadata():
    from pose_capture.pose_capture_app import CaptureConfig, PoseCaptureApp
    from pose_capture.providers import SkeletonData

    skeleton = SkeletonData()
    skeleton.metadata = {}

    layout = StubLayout({"activeSeatId": "seat-x"})

    config = CaptureConfig(
        provider=DummyProvider(),
        transport=DummyTransport(),
        frame_interval=1 / 30,
        calibration_file=None,
        metadata={},
        seating_layout=layout,
    )

    app = PoseCaptureApp(config)
    updated = app._apply_metadata(skeleton)

    assert updated.metadata["seating"]["activeSeatId"] == "seat-x"


def test_apply_metadata_includes_mode_metadata():
    from pose_capture.pose_capture_app import CaptureConfig, PoseCaptureApp
    from pose_capture.providers import SkeletonData

    skeleton = SkeletonData()
    skeleton.metadata = {}

    config = CaptureConfig(
        provider=DummyProvider(),
        transport=DummyTransport(),
        frame_interval=1 / 30,
        calibration_file=None,
        metadata={},
        seating_layout=None,
        mode="avatar",
    )

    app = PoseCaptureApp(config)
    updated = app._apply_metadata(skeleton)

    assert updated.metadata["mode"] == "avatar"


def test_update_seating_layout_replaces_metadata():
    from pose_capture.pose_capture_app import CaptureConfig, PoseCaptureApp
    from pose_capture.providers import SkeletonData

    skeleton = SkeletonData()
    skeleton.metadata = {}

    layout_a = StubLayout({"activeSeatId": "seat-a"})
    layout_b = StubLayout({"activeSeatId": "seat-b"})

    config = CaptureConfig(
        provider=DummyProvider(),
        transport=DummyTransport(),
        frame_interval=1 / 30,
        calibration_file=None,
        metadata={},
        seating_layout=layout_a,
    )

    app = PoseCaptureApp(config)
    first = app._apply_metadata(skeleton)
    assert first.metadata["seating"]["activeSeatId"] == "seat-a"

    asyncio.run(app.update_seating_layout(layout_b))
    skeleton2 = SkeletonData()
    skeleton2.metadata = {}
    second = app._apply_metadata(skeleton2)

    assert second.metadata["seating"]["activeSeatId"] == "seat-b"


def test_update_seating_layout_can_disable_seating():
    from pose_capture.pose_capture_app import CaptureConfig, PoseCaptureApp
    from pose_capture.providers import SkeletonData

    skeleton = SkeletonData()
    skeleton.metadata = {"seating": {"activeSeatId": "seat-a"}}

    layout = StubLayout({"activeSeatId": "seat-a"})

    config = CaptureConfig(
        provider=DummyProvider(),
        transport=DummyTransport(),
        frame_interval=1 / 30,
        calibration_file=None,
        metadata={},
        seating_layout=layout,
    )

    app = PoseCaptureApp(config)
    asyncio.run(app.update_seating_layout(None))
    updated = app._apply_metadata(skeleton)

    assert "seating" not in updated.metadata
