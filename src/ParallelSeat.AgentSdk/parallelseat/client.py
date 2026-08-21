from __future__ import annotations

import asyncio
import json
import struct
import uuid
from dataclasses import dataclass
from typing import Any


PROTOCOL_VERSION = 1
MAX_MESSAGE_BYTES = 1_048_576


class ParallelSeatError(Exception):
    def __init__(self, message: str, code: int | None = None) -> None:
        super().__init__(message)
        self.code = code


@dataclass
class ElementHandle:
    seat: "Seat"
    element_id: str

    async def set_value(self, value: str) -> dict[str, Any]:
        return await self.seat._client.request(
            "keyboard.set_text",
            {"seatId": self.seat.seat_id, "value": value},
        )

    async def invoke(self) -> dict[str, Any]:
        return await self.seat._client.request(
            "pointer.click",
            {"seatId": self.seat.seat_id},
        )

    async def get_capabilities(self) -> dict[str, Any]:
        return await self.seat._client.request(
            "element.get_capabilities",
            {"seatId": self.seat.seat_id},
        )


class Seat:
    def __init__(self, client: "ParallelSeatClient", seat_id: str) -> None:
        self._client = client
        self.seat_id = seat_id
        self._heartbeat_task: asyncio.Task[None] | None = None

    async def __aenter__(self) -> "Seat":
        self._heartbeat_task = asyncio.create_task(self._heartbeat_loop())
        return self

    async def __aexit__(self, exc_type, exc, tb) -> None:
        if self._heartbeat_task is not None:
            self._heartbeat_task.cancel()
            try:
                await self._heartbeat_task
            except asyncio.CancelledError:
                pass
        await self._client.request("seat.destroy", {"seatId": self.seat_id})

    async def attach_to_application(self, process_id: int) -> dict[str, Any]:
        return await self._client.request(
            "seat.attach",
            {"seatId": self.seat_id, "processId": process_id},
        )

    async def find_element(
        self,
        automation_id: str | None = None,
        name: str | None = None,
        top_level_hwnd: int | None = None,
    ) -> ElementHandle:
        params: dict[str, Any] = {"seatId": self.seat_id}
        if automation_id is not None:
            params["automationId"] = automation_id
        if name is not None:
            params["name"] = name
        if top_level_hwnd is not None:
            params["topLevelHwnd"] = top_level_hwnd
        result = await self._client.request("element.find", params)
        return ElementHandle(self, result["elementId"])

    async def pause(self) -> dict[str, Any]:
        return await self._client.request("seat.pause", {"seatId": self.seat_id})

    async def resume(self) -> dict[str, Any]:
        return await self._client.request("seat.resume", {"seatId": self.seat_id})

    async def get_state(self) -> dict[str, Any]:
        return await self._client.request("seat.get_state", {"seatId": self.seat_id})

    async def _heartbeat_loop(self) -> None:
        while True:
            await asyncio.sleep(5)
            await self._client.request("seat.get_state", {"seatId": self.seat_id})


class ParallelSeatClient:
    """Async client. Transport uses a worker thread around Windows named-pipe file I/O."""

    def __init__(self, token: str, pipe_name: str) -> None:
        self._token = token
        self._pipe_name = pipe_name if pipe_name.startswith(r"\\.\pipe\") else rf"\\.\pipe\{pipe_name}"
        self._fp = None
        self._lock = asyncio.Lock()

    async def __aenter__(self) -> "ParallelSeatClient":
        await self.connect()
        return self

    async def __aexit__(self, exc_type, exc, tb) -> None:
        await self.close()

    async def connect(self) -> None:
        self._fp = await asyncio.to_thread(open, self._pipe_name, "r+b", buffering=0)
        await self._handshake()

    async def close(self) -> None:
        if self._fp is not None:
            await asyncio.to_thread(self._fp.close)
            self._fp = None

    def create_seat(self, seat_id: str = "ai-primary", strict_no_focus_steal: bool = True) -> "SeatContext":
        return SeatContext(self, seat_id, strict_no_focus_steal)

    async def request(self, method: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        if self._fp is None:
            raise ParallelSeatError("Client is not connected.")
        payload = {
            "jsonrpc": "2.0",
            "id": uuid.uuid4().hex,
            "method": method,
            "params": params or {},
            "correlationId": uuid.uuid4().hex,
        }
        data = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        async with self._lock:
            await asyncio.to_thread(write_frame_sync, self._fp, data)
            response_bytes = await asyncio.to_thread(read_frame_sync, self._fp)
        response = json.loads(response_bytes.decode("utf-8"))
        if response.get("error"):
            err = response["error"]
            raise ParallelSeatError(err.get("message", "RPC error"), err.get("code"))
        return response.get("result") or {}

    async def emergency_stop(self) -> dict[str, Any]:
        return await self.request("safety.emergency_stop", {})

    async def _handshake(self) -> None:
        assert self._fp is not None
        payload = {
            "protocolVersion": PROTOCOL_VERSION,
            "token": self._token,
            "clientName": "parallelseat-python",
        }
        await asyncio.to_thread(write_frame_sync, self._fp, json.dumps(payload).encode("utf-8"))
        raw = await asyncio.to_thread(read_frame_sync, self._fp)
        result = json.loads(raw.decode("utf-8"))
        if not result.get("ok"):
            raise ParallelSeatError(result.get("message") or "Handshake failed")


class SeatContext:
    def __init__(self, client: ParallelSeatClient, seat_id: str, strict: bool) -> None:
        self._client = client
        self._seat_id = seat_id
        self._strict = strict
        self._seat: Seat | None = None

    async def __aenter__(self) -> Seat:
        await self._client.request(
            "seat.create",
            {"seatId": self._seat_id, "strictNoFocusSteal": self._strict},
        )
        self._seat = Seat(self._client, self._seat_id)
        return await self._seat.__aenter__()

    async def __aexit__(self, exc_type, exc, tb) -> None:
        if self._seat is not None:
            await self._seat.__aexit__(exc_type, exc, tb)


def write_frame_sync(fp, payload: bytes) -> None:
    if len(payload) > MAX_MESSAGE_BYTES:
        raise ParallelSeatError("Message too large")
    fp.write(struct.pack("<i", len(payload)))
    fp.write(payload)
    fp.flush()


def read_frame_sync(fp) -> bytes:
    header = fp.read(4)
    if len(header) != 4:
        raise ParallelSeatError("Unexpected EOF reading frame header")
    (length,) = struct.unpack("<i", header)
    if length <= 0 or length > MAX_MESSAGE_BYTES:
        raise ParallelSeatError(f"Invalid frame length {length}")
    data = fp.read(length)
    if len(data) != length:
        raise ParallelSeatError("Unexpected EOF reading frame payload")
    return data
