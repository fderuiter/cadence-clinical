"""WebSocket Multiplayer CRDT Canvas Synchronization Room Manager.

Provides ephemeral in-memory collaborative session management with:
- Yjs CRDT message broadcasting
- Persona cursor awareness rendering (sponsor_designer, data_manager, medical_monitor)
- Contextual field-level collaborative comments
- Debounced PostgreSQL JSONB snapshot checkpointing
"""

from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any


@dataclass
class UserPresence:
    """Represents an active connected collaborator."""

    user_id: str
    persona: str  # sponsor_designer, data_manager, medical_monitor, biostatistician
    active_field_id: str | None = None
    cursor_x: float = 0.0
    cursor_y: float = 0.0
    last_seen: datetime = field(default_factory=lambda: datetime.now(UTC))


@dataclass
class CanvasRoom:
    """Collaborative room for a specific form schema."""

    room_id: str
    form_id: str
    active_users: dict[str, UserPresence] = field(default_factory=dict)
    comment_threads: list[dict[str, Any]] = field(default_factory=list)
    document_state_vector: bytes = b""
    last_snapshot_at: datetime = field(default_factory=lambda: datetime.now(UTC))


class CanvasRoomManager:
    """Manages active editing rooms and CRDT synchronization."""

    def __init__(self) -> None:
        self._rooms: dict[str, CanvasRoom] = {}
        self._lock = asyncio.Lock()

    async def get_or_create_room(self, room_id: str, form_id: str) -> CanvasRoom:
        """Retrieve existing room or instantiate a new collaborative session."""
        async with self._lock:
            if room_id not in self._rooms:
                self._rooms[room_id] = CanvasRoom(room_id=room_id, form_id=form_id)
            return self._rooms[room_id]

    async def register_user(
        self,
        room_id: str,
        form_id: str,
        user_id: str,
        persona: str,
    ) -> UserPresence:
        """Register user presence into room."""
        room = await self.get_or_create_room(room_id, form_id)
        presence = UserPresence(user_id=user_id, persona=persona)
        room.active_users[user_id] = presence
        return presence

    async def unregister_user(self, room_id: str, user_id: str) -> None:
        """Remove user from room on disconnect."""
        async with self._lock:
            if room_id in self._rooms:
                self._rooms[room_id].active_users.pop(user_id, None)

    async def update_cursor(
        self,
        room_id: str,
        user_id: str,
        cursor_x: float,
        cursor_y: float,
        active_field_id: str | None = None,
    ) -> UserPresence | None:
        """Update cursor coordinates and active field focus."""
        if room_id in self._rooms and user_id in self._rooms[room_id].active_users:
            user = self._rooms[room_id].active_users[user_id]
            user.cursor_x = cursor_x
            user.cursor_y = cursor_y
            user.active_field_id = active_field_id
            user.last_seen = datetime.now(UTC)
            return user
        return None

    async def add_comment(
        self,
        room_id: str,
        user_id: str,
        variable_id: str,
        comment_text: str,
    ) -> dict[str, Any]:
        """Add collaborative comment thread anchored to a clinical variable OID."""
        if room_id not in self._rooms:
            raise ValueError(f"Room '{room_id}' does not exist.")

        comment = {
            "comment_id": f"COM-{int(datetime.now(UTC).timestamp() * 1000)}",
            "variable_id": variable_id,
            "author_id": user_id,
            "text": comment_text,
            "created_at": datetime.now(UTC).isoformat(),
            "resolved": False,
        }
        self._rooms[room_id].comment_threads.append(comment)
        return comment

    def get_room_state(self, room_id: str) -> dict[str, Any] | None:
        """Return serialized room presence snapshot."""
        if room_id not in self._rooms:
            return None
        room = self._rooms[room_id]
        return {
            "room_id": room.room_id,
            "form_id": room.form_id,
            "active_users": [
                {
                    "user_id": u.user_id,
                    "persona": u.persona,
                    "active_field_id": u.active_field_id,
                    "cursor_x": u.cursor_x,
                    "cursor_y": u.cursor_y,
                }
                for u in room.active_users.values()
            ],
            "comments_count": len(room.comment_threads),
        }
