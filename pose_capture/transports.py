"""Transport implementations for sending skeleton data."""
from __future__ import annotations

import asyncio
import json
import logging
import socket
from dataclasses import dataclass, field
from inspect import isawaitable
from typing import Optional
from urllib.parse import urlparse

from .providers import SkeletonData

LOGGER = logging.getLogger(__name__)


class SkeletonTransport:
    """Interface for sending skeleton data to a consumer."""

    async def connect(self) -> None:
        """Connect the transport to its endpoint."""

    async def send(self, skeleton: SkeletonData) -> None:
        """Send a skeleton sample."""
        raise NotImplementedError

    async def close(self) -> None:
        """Close the transport."""


@dataclass
class WebSocketSkeletonTransport(SkeletonTransport):
    """Expose a WebSocket server that Unity clients can subscribe to."""

    uri: str
    _server: Optional["websockets.server.Serve"] = field(default=None, init=False, repr=False)
    _connection: Optional["websockets.WebSocketServerProtocol"] = field(default=None, init=False, repr=False)
    _connection_event: asyncio.Event = field(default_factory=asyncio.Event, init=False, repr=False)
    _path: Optional[str] = field(default=None, init=False, repr=False)

    @staticmethod
    def _normalize_path(path: Optional[str]) -> Optional[str]:
        """Return a canonical path (leading slash, no query, no trailing slash)."""
        if not path:
            return None
        if isinstance(path, bytes):
            try:
                path = path.decode("utf-8")
            except UnicodeDecodeError:
                LOGGER.debug("Failed to decode raw path bytes; defaulting to root")
                return None
        parsed_path = urlparse(str(path)).path or "/"
        if not parsed_path.startswith("/"):
            parsed_path = f"/{parsed_path}"
        parsed_path = parsed_path.rstrip("/")
        if not parsed_path:
            parsed_path = "/"
        return parsed_path if parsed_path != "/" else None

    async def connect(self) -> None:
        import websockets  # type: ignore

        parsed = urlparse(self.uri)
        host = parsed.hostname or "0.0.0.0"
        port = parsed.port
        if port is None:
            raise ValueError(f"WebSocket URI {self.uri!r} must include a port")
        self._path = self._normalize_path(parsed.path)

        if self._server:
            LOGGER.debug("WebSocket server already running")
            return

        async def handler(
            websocket: "websockets.WebSocketServerProtocol",
            request_path: Optional[str] = None,
        ) -> None:
            raw_path: Optional[str] = request_path
            if not raw_path:
                raw_path = getattr(websocket, "path", None)
            if not raw_path:
                request = getattr(websocket, "request", None)
                if request is not None:
                    raw_path = getattr(request, "path", None)
                    if not raw_path:
                        raw_path = getattr(request, "raw_path", None)
                    if isinstance(raw_path, (bytes, bytearray)):
                        try:
                            raw_path = raw_path.decode("utf-8")
                        except UnicodeDecodeError:
                            raw_path = None
                    if not raw_path:
                        uri = getattr(request, "uri", None)
                        if uri:
                            raw_path = urlparse(str(uri)).path

            normalized_path = self._normalize_path(raw_path)
            expected_path = self._path
            actual_for_log = normalized_path or (raw_path if raw_path else "/")
            expected_for_log = expected_path or "/"

            if expected_path and normalized_path != expected_path:
                LOGGER.warning(
                    "Rejected WebSocket client on unexpected path %s (expected %s)",
                    actual_for_log,
                    expected_for_log,
                )
                await websocket.close(code=1008, reason="Unexpected path")
                return

            LOGGER.info("WebSocket client connected from %s", websocket.remote_address)
            self._connection = websocket
            self._connection_event.set()
            try:
                await websocket.wait_closed()
            finally:
                LOGGER.info("WebSocket client disconnected")
                self._connection = None
                self._connection_event.clear()

        LOGGER.info("Starting WebSocket server on %s:%d%s", host, port, self._path or "")
        self._server = await websockets.serve(handler, host, port)

    async def send(self, skeleton: SkeletonData) -> None:
        if self._connection is None:
            LOGGER.debug("No WebSocket client connected; dropping frame")
            return

        payload = json.dumps(skeleton.to_dict())
        try:
            await self._connection.send(payload)
            LOGGER.debug("Sent skeleton frame (%d joints) via WebSocket", len(skeleton.joints))
        except AttributeError as exc:
            LOGGER.warning("WebSocket connection missing send() method: %s", exc)
            self._connection = None
            self._connection_event.clear()
        except Exception as exc:  # websockets.ConnectionClosed and similar
            LOGGER.warning("Failed to send skeleton frame via WebSocket: %s", exc)
            self._connection = None
            self._connection_event.clear()

    async def close(self) -> None:
        if self._connection:
            try:
                close_fn = getattr(self._connection, "close", None)
                if close_fn:
                    result = close_fn(code=1001, reason="Server shutdown")
                    if isawaitable(result):
                        await result
            except TypeError:
                try:
                    result = self._connection.close()
                    if isawaitable(result):
                        await result
                except Exception as exc:
                    LOGGER.debug("Error closing WebSocket client connection: %s", exc)
            except Exception as exc:
                LOGGER.debug("Error closing WebSocket client connection: %s", exc)
        self._connection = None
        self._connection_event.clear()

        if self._server:
            self._server.close()
            await self._server.wait_closed()
            LOGGER.info("Stopped WebSocket server")
        self._server = None


@dataclass
class UDPSkeletonTransport(SkeletonTransport):
    """Send skeleton data to a UDP socket."""

    host: str
    port: int
    loop: Optional[asyncio.AbstractEventLoop] = None
    _socket: Optional[socket.socket] = None

    async def connect(self) -> None:
        LOGGER.info("Preparing UDP socket to %s:%d", self.host, self.port)
        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._socket.setblocking(False)
        self.loop = self.loop or asyncio.get_running_loop()

    async def send(self, skeleton: SkeletonData) -> None:
        if self._socket is None:
            raise RuntimeError("Transport is not connected")
        payload = json.dumps(skeleton.to_dict()).encode("utf-8")
        assert self.loop is not None
        await self.loop.sock_sendto(self._socket, payload, (self.host, self.port))
        LOGGER.debug("Sent skeleton frame (%d joints) via UDP", len(skeleton.joints))

    async def close(self) -> None:
        if self._socket:
            self._socket.close()
            LOGGER.info("Closed UDP socket")
        self._socket = None
