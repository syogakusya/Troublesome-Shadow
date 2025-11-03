"""Transport implementations for sending skeleton data."""
from __future__ import annotations

import asyncio
import json
import logging
import socket
from dataclasses import dataclass
from typing import Optional

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
    """Send skeleton data to a WebSocket endpoint."""

    uri: str
    _connection: Optional["websockets.WebSocketClientProtocol"] = None

    async def connect(self) -> None:
        import websockets  # type: ignore

        LOGGER.info("Connecting to WebSocket %s", self.uri)
        self._connection = await websockets.connect(self.uri)

    async def send(self, skeleton: SkeletonData) -> None:
        if self._connection is None:
            raise RuntimeError("Transport is not connected")
        payload = json.dumps(skeleton.to_dict())
        await self._connection.send(payload)
        LOGGER.debug("Sent skeleton frame (%d joints) via WebSocket", len(skeleton.joints))

    async def close(self) -> None:
        if self._connection:
            await self._connection.close()
            LOGGER.info("Closed WebSocket connection")
        self._connection = None


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
