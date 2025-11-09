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
