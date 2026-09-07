#!/usr/bin/env python3
"""Real HTTP + SignalR checks. Requires requests, websockets, a DISPOSABLE database.
CHAT_TEST_DISPOSABLE=1 python3 tests/chat/realtime_files_test.py
"""
import asyncio
import base64
import hashlib
import json
import os
import uuid
from urllib.parse import urlencode
import requests
import websockets

BASE = os.environ.get("CHAT_TEST_BASE_URL", "http://127.0.0.1:5100").rstrip("/")


def fixture():
    if os.environ.get("CHAT_TEST_DISPOSABLE") != "1":
        raise SystemExit("Use a disposable test database and set CHAT_TEST_DISPOSABLE=1.")
    admin = requests.Session()
    response = admin.post(BASE + "/api/auth/login", json={"username": os.environ.get("CHAT_TEST_USERNAME", "admin"), "password": os.environ.get("CHAT_TEST_PASSWORD", "admin")}, timeout=20)
    response.raise_for_status()
    admin.headers["Authorization"] = "Bearer " + response.json()["token"]
    suffix = uuid.uuid4().hex[:8]
    users, sessions, logins = [], [], []
    for kind, name in [("sender", "فرستنده"), ("recipient", "گیرنده"), ("outsider", "غیرعضو")]:
        response = admin.post(BASE + "/api/users", json={"username": f"live_{kind}_{suffix}", "password": "LocalTest123!", "firstName": name, "lastName": suffix, "role": "Operator", "isActive": True}, timeout=20)
        response.raise_for_status(); users.append(response.json())
        response = requests.post(BASE + "/api/auth/login", json={"username": users[-1]["username"], "password": "LocalTest123!"}, timeout=20)
        response.raise_for_status(); logins.append(response.json())
        session = requests.Session(); session.headers["Authorization"] = "Bearer " + logins[-1]["token"]; sessions.append(session)
    response = sessions[0].post(BASE + "/api/chat/conversations/direct", json={"targetUserId": users[1]["id"]}, timeout=20)
    response.raise_for_status()
    return users, sessions, logins, response.json()["id"]


class Hub:
    def __init__(self):
        self.events = asyncio.Queue(); self.pending = {}; self.sequence = 0; self.received = []

    @classmethod
    async def open(cls, session, login, forged_user=None):
        self = cls()
        response = session.post(BASE + "/hubs/chat/negotiate?negotiateVersion=1", timeout=20)
        response.raise_for_status()
        query = {"id": response.json()["connectionToken"], "access_token": login["token"]}
        if forged_user is not None: query["userId"] = forged_user
        self.ws = await websockets.connect(BASE.replace("http", "ws", 1) + "/hubs/chat?" + urlencode(query), max_size=2**20)
        await self.ws.send(json.dumps({"protocol": "json", "version": 1}) + "\x1e")
        greeting = await asyncio.wait_for(self.ws.recv(), 10)
        assert json.loads(greeting.split("\x1e")[0]) == {}
        self.reader = asyncio.create_task(self.read())
        return self

    async def read(self):
        try:
            async for frame in self.ws:
                for record in frame.split("\x1e"):
                    if not record: continue
                    data = json.loads(record)
                    if data.get("type") == 3:
                        future = self.pending.pop(data["invocationId"], None)
                        if future and not future.done(): future.set_result(data)
                    elif data.get("type") == 1:
                        self.received.append(data); await self.events.put(data)
        except websockets.ConnectionClosed:
            pass

    async def invoke(self, method, *args):
        self.sequence += 1; key = str(self.sequence)
        future = asyncio.get_running_loop().create_future(); self.pending[key] = future
        await self.ws.send(json.dumps({"type": 1, "invocationId": key, "target": method, "arguments": args}) + "\x1e")
        return await asyncio.wait_for(future, 10)

    async def event(self, name, predicate=lambda args: True):
        async def take():
            while True:
                data = await self.events.get()
                if data["target"] == name and predicate(data["arguments"]): return data["arguments"]
        return await asyncio.wait_for(take(), 10)

    async def close(self):
        await self.ws.close(); await self.reader


async def main():
    users, sessions, logins, conversation = fixture()
    sender, recipient, outsider = sessions
    checks = []
    def passed(text): checks.append(text); print("PASS:", text, flush=True)
    assert requests.post(BASE + "/hubs/chat/negotiate?negotiateVersion=1&userId=" + str(users[0]["id"]), timeout=15).status_code == 401
    passed("A query-string userId cannot authenticate to the chat hub")
    hubs = [await Hub.open(session, login, users[1]["id"] if i == 0 else None) for i, (session, login) in enumerate(zip(sessions, logins))]
    try:
        assert "error" in await hubs[2].invoke("JoinConversation", conversation)
        assert "error" in await hubs[2].invoke("SendTyping", conversation)
        assert "error" not in await hubs[0].invoke("JoinConversation", conversation)
        assert "error" not in await hubs[1].invoke("JoinConversation", conversation)
        passed("Only actual members can join or send typing notifications")
        await hubs[0].invoke("SendTyping", conversation)
        typing = await hubs[1].event("UserTyping")
        assert typing[1] == users[0]["id"] and "فرستنده" in typing[2]
        await hubs[0].invoke("StopTyping", conversation)
        assert (await hubs[1].event("UserTyping"))[2] == ""
        passed("Typing uses authenticated identity and stops explicitly")
        ids = []
        for text in ["پیام زنده اول", "پیام زنده دوم", "پیام زنده سوم"]:
            response = sender.post(BASE + f"/api/chat/conversations/{conversation}/messages", json={"text": text}, timeout=20)
            response.raise_for_status(); message = response.json(); ids.append(message["id"])
            own = (await hubs[0].event("ReceiveMessage", lambda a: a[0]["id"] == message["id"]))[0]
            peer = (await hubs[1].event("ReceiveMessage", lambda a: a[0]["id"] == message["id"]))[0]
            assert own["isOutgoing"] is True and peer["isOutgoing"] is False
        await asyncio.sleep(.2)
        for hub in hubs[:2]:
            received = [e for e in hub.received if e["target"] == "ReceiveMessage" and e["arguments"][0]["id"] in ids]
            assert len(received) == 3
        assert not any(e["target"] == "ReceiveMessage" for e in hubs[2].received)
        passed("Each message arrives once per member, with correct sender/recipient alignment")
        assert recipient.get(BASE + "/api/chat/summary", timeout=20).json()["totalUnreadMessages"] == 3
        response = recipient.post(BASE + f"/api/chat/conversations/{conversation}/read", json={"lastReadMessageId": ids[1]}, timeout=20); response.raise_for_status()
        assert recipient.get(BASE + "/api/chat/summary", timeout=20).json()["totalUnreadMessages"] == 1
        assert (await hubs[0].event("MessagesRead"))[2] == ids[1]
        assert (await hubs[1].event("MessagesRead"))[2] == ids[1]
        passed("Unread count is exact; read acknowledgement only covers displayed messages and reaches both users")
        response = sender.put(BASE + f"/api/chat/messages/{ids[-1]}", json={"newText": "ویرایش زنده"}, timeout=20); response.raise_for_status()
        assert (await hubs[1].event("MessageUpdated"))[0]["text"] == "ویرایش زنده"
        passed("Message edit event has the same signature expected by the client")

        content = "فایل فارسی آزمایشی\n123".encode()
        response = sender.post(BASE + f"/api/chat/conversations/{conversation}/attachments", files={"file": ("پرونده فارسی.txt", content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")}, timeout=20)
        response.raise_for_status(); upload = response.json()
        assert upload["fileContentType"] == "text/plain"
        assert recipient.get(BASE + upload["fileUrl"], timeout=20).status_code == 403
        response = sender.post(BASE + f"/api/chat/conversations/{conversation}/messages", json={"fileUrl": upload["fileUrl"], "fileName": "wrong.exe", "fileSizeBytes": 7}, timeout=20)
        response.raise_for_status(); message = response.json(); path = f"/api/chat/messages/{message['id']}/download"
        response = recipient.get(BASE + path, timeout=20); response.raise_for_status()
        assert response.content == content and "filename*=" in response.headers["Content-Disposition"]
        assert message["fileName"] == "پرونده فارسی.txt" and message["fileSizeBytes"] == len(content)
        partial = recipient.get(BASE + path, headers={"Range": "bytes=0-5"}, timeout=20)
        assert partial.status_code == 206 and partial.content == content[:6]
        assert requests.get(BASE + path, timeout=20).status_code == 401
        assert outsider.get(BASE + path, timeout=20).status_code == 403
        assert requests.get(BASE + path, params={"access_token": logins[1]["token"]}, timeout=20).content == content
        passed("Persian filename, authoritative MIME/length, exact bytes, range download and authorization work")
        png = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=")
        response = sender.post(BASE + f"/api/chat/conversations/{conversation}/attachments", files={"file": ("تصویر.png", png, "application/octet-stream")}, timeout=20); response.raise_for_status()
        response = sender.post(BASE + f"/api/chat/conversations/{conversation}/messages", json={"fileUrl": response.json()["fileUrl"]}, timeout=20); response.raise_for_status()
        preview = recipient.get(BASE + f"/api/chat/messages/{response.json()['id']}/preview", timeout=20)
        assert preview.status_code == 200 and preview.content == png and preview.headers["Content-Type"] == "image/png"
        assert preview.headers["X-Content-Type-Options"] == "nosniff"
        passed("Image preview returns actual image bytes with a correct, safe MIME type")
        assert requests.get(BASE + "/uploads/chat/anything.txt", timeout=20).status_code == 404
        passed("Legacy public static URLs cannot bypass attachment authorization")

        large = b"0123456789ABCDEF" * (32 * 1024 * 1024 // 16)
        response = sender.post(BASE + f"/api/chat/conversations/{conversation}/attachments", files={"file": ("32MiB.bin", large, "application/octet-stream")}, timeout=90)
        response.raise_for_status()
        downloaded = sender.get(BASE + response.json()["fileUrl"], timeout=90)
        assert downloaded.status_code == 200 and hashlib.sha256(downloaded.content).digest() == hashlib.sha256(large).digest()
        passed("A 32 MiB file exceeds the old default HTTP cap and uploads/downloads byte-for-byte")
        empty = sender.post(BASE + f"/api/chat/conversations/{conversation}/attachments", files={"file": ("empty.txt", b"", "text/plain")}, timeout=20)
        assert empty.status_code == 400
        oversized = sender.post(BASE + f"/api/chat/conversations/{conversation}/attachments", files={"file": ("too-large.bin", b"x" * (50 * 1024 * 1024 + 1), "application/octet-stream")}, timeout=90)
        assert oversized.status_code in (400, 413)
        passed("Empty and over-limit files are rejected with an explicit failure")
    finally:
        for hub in hubs: await hub.close()
    print(f"All {len(checks)} HTTP/SignalR scenarios passed.")

if __name__ == "__main__": asyncio.run(main())
