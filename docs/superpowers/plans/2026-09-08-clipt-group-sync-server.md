# Clipt Group Sync — Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the self-hosted FastAPI + SQLite sync API described in `docs/superpowers/specs/2026-09-08-group-sync-design.md`, testable entirely on this machine (WSL/Linux) with pytest — no VPS access needed to build or verify it. Deployment to the Ubuntu VPS is a manual step the user performs at the end (Task 6 hands them exact commands).

**Architecture:** A single FastAPI app (`app/main.py`) backed by one SQLite file, run under gunicorn+uvicorn workers behind the existing nginx on the VPS. Every group is an opaque ciphertext blob to this service — it only ever handles ids, bytes, and version numbers. A single monotonic version counter (`sync_meta.next_version`), allocated inside a `BEGIN IMMEDIATE` transaction on every write, is what makes `GET /groups?since=` correctly return "everything that changed after this point" across every row, not just one.

**Tech Stack:** Python 3.11+, FastAPI, Pydantic v2, gunicorn + uvicorn workers, sqlite3 (stdlib), pytest + httpx (for `TestClient`).

## Global Constraints

- Server never decrypts or inspects ciphertext — every request body's `ciphertext` field is opaque bytes in, opaque bytes out.
- Every endpoint requires `Authorization: Bearer <token>`, checked with a constant-time comparison (`hmac.compare_digest`), token read from the `CLIPT_SYNC_TOKEN` environment variable.
- Optimistic concurrency on every write: a `PUT`/`DELETE` must supply `based_on_version` matching the row's current version (0 for "doesn't exist yet"), or the request fails with `409`.
- Deletes are soft (tombstone: `deleted=1`), never row-removal — other devices only learn about a delete by seeing the tombstone in a future pull.
- Run gunicorn with a single worker (`--workers 1`). This is a personal, single-user, low-traffic service; one worker removes any cross-process SQLite lock contention entirely rather than needing to reason about it.
- All versions in this plan (FastAPI, Pydantic, gunicorn, uvicorn, pytest, httpx) are exact pins in `requirements.txt` — install these versions, don't take latest.

---

## File Structure

```
server/clipt-sync-server/
  requirements.txt
  app/
    __init__.py          (empty)
    db.py                 SQLite connection, schema, version counter
    auth.py                Bearer-token dependency
    models.py               Pydantic request/response models
    main.py                   FastAPI app + routes
  tests/
    conftest.py             pytest fixtures (temp db, authed TestClient)
    test_db.py               db.py unit tests (no FastAPI)
    test_kdf.py                GET /kdf
    test_auth.py                401 cases across all endpoints
    test_groups_upsert.py        PUT /groups/{id}
    test_groups_delete.py         DELETE /groups/{id}
    test_groups_pull.py            GET /groups?since=
  deploy/
    clipt-sync.service        systemd unit template
    nginx-clipt-sync.conf      nginx site block template
  README.md                    deployment steps (manual, run by the user on the VPS)
```

---

### Task 1: SQLite schema and version counter (`app/db.py`)

**Files:**
- Create: `server/clipt-sync-server/requirements.txt`
- Create: `server/clipt-sync-server/app/__init__.py`
- Create: `server/clipt-sync-server/app/db.py`
- Test: `server/clipt-sync-server/tests/test_db.py`

**Interfaces:**
- Produces: `get_connection(db_path: str | None = None) -> sqlite3.Connection`, `init_db(conn: sqlite3.Connection) -> None`, `allocate_version(conn: sqlite3.Connection) -> int` (must be called between `BEGIN IMMEDIATE` and `COMMIT`/`ROLLBACK` on `conn`).

- [ ] **Step 1: Create the project scaffold and pin dependencies**

`server/clipt-sync-server/requirements.txt`:
```
fastapi==0.115.0
uvicorn[standard]==0.30.6
gunicorn==23.0.0
pydantic==2.9.2
pytest==8.3.3
httpx==0.27.2
```

`server/clipt-sync-server/app/__init__.py`: empty file.

- [ ] **Step 2: Write the failing test for schema init and version allocation**

`server/clipt-sync-server/tests/test_db.py`:
```python
import sqlite3

from app import db


def test_init_db_creates_expected_tables(tmp_path):
    conn = db.get_connection(str(tmp_path / "test.db"))
    db.init_db(conn)

    tables = {
        row[0]
        for row in conn.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table'"
        ).fetchall()
    }
    assert {"groups", "kdf_params", "sync_meta"} <= tables
    conn.close()


def test_init_db_is_idempotent(tmp_path):
    conn = db.get_connection(str(tmp_path / "test.db"))
    db.init_db(conn)
    db.init_db(conn)  # must not raise or reset next_version

    row = conn.execute("SELECT next_version FROM sync_meta WHERE id = 1").fetchone()
    assert row[0] == 1
    conn.close()


def test_allocate_version_increments_monotonically(tmp_path):
    conn = db.get_connection(str(tmp_path / "test.db"))
    db.init_db(conn)

    conn.execute("BEGIN IMMEDIATE")
    first = db.allocate_version(conn)
    conn.execute("COMMIT")

    conn.execute("BEGIN IMMEDIATE")
    second = db.allocate_version(conn)
    conn.execute("COMMIT")

    assert first == 1
    assert second == 2
    conn.close()
```

- [ ] **Step 2b: Run the test to verify it fails**

Run (from `server/clipt-sync-server/`): `python -m pytest tests/test_db.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'app.db'` (or `app`).

- [ ] **Step 3: Implement `app/db.py`**

```python
import sqlite3

SCHEMA = """
CREATE TABLE IF NOT EXISTS groups (
    id TEXT PRIMARY KEY,
    ciphertext BLOB NOT NULL,
    version INTEGER NOT NULL,
    updated_at TEXT NOT NULL,
    deleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS kdf_params (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    salt BLOB NOT NULL,
    argon2_time_cost INTEGER NOT NULL,
    argon2_memory_cost_kib INTEGER NOT NULL,
    argon2_parallelism INTEGER NOT NULL,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sync_meta (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    next_version INTEGER NOT NULL
);
"""


def get_connection(db_path: str | None = None) -> sqlite3.Connection:
    conn = sqlite3.connect(db_path or "clipt_sync.db", timeout=5, isolation_level=None)
    conn.execute("PRAGMA journal_mode=WAL")
    return conn


def init_db(conn: sqlite3.Connection) -> None:
    conn.executescript(SCHEMA)
    row = conn.execute("SELECT next_version FROM sync_meta WHERE id = 1").fetchone()
    if row is None:
        conn.execute("INSERT INTO sync_meta (id, next_version) VALUES (1, 1)")


def allocate_version(conn: sqlite3.Connection) -> int:
    """Must run inside a BEGIN IMMEDIATE transaction on conn; caller commits/rolls back."""
    row = conn.execute("SELECT next_version FROM sync_meta WHERE id = 1").fetchone()
    next_version = row[0]
    conn.execute("UPDATE sync_meta SET next_version = ? WHERE id = 1", (next_version + 1,))
    return next_version
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python -m pytest tests/test_db.py -v`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
cd server/clipt-sync-server
git add requirements.txt app/__init__.py app/db.py tests/test_db.py
git commit -m "clipt-sync-server: SQLite schema and monotonic version counter"
```

---

### Task 2: Auth dependency and `GET /kdf`

**Files:**
- Create: `server/clipt-sync-server/app/auth.py`
- Create: `server/clipt-sync-server/app/models.py`
- Create: `server/clipt-sync-server/app/main.py`
- Create: `server/clipt-sync-server/tests/conftest.py`
- Test: `server/clipt-sync-server/tests/test_kdf.py`
- Test: `server/clipt-sync-server/tests/test_auth.py`

**Interfaces:**
- Consumes: `db.get_connection`, `db.init_db` (Task 1).
- Produces: `app.main.app` (the FastAPI instance), `app.main.get_db` (dependency, overridable in tests), `app.auth.require_auth` (dependency), `app.models.KdfParamsResponse`.

- [ ] **Step 1: Write the failing tests**

`server/clipt-sync-server/tests/conftest.py`:
```python
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

TEST_TOKEN = "test-token"


@pytest.fixture(autouse=True)
def sync_token_env(monkeypatch):
    monkeypatch.setenv("CLIPT_SYNC_TOKEN", TEST_TOKEN)


@pytest.fixture
def db_path(tmp_path: Path) -> str:
    return str(tmp_path / "test.db")


@pytest.fixture
def client(db_path: str, sync_token_env) -> TestClient:
    from app import db as db_module
    from app.main import app, get_db

    def override_get_db():
        conn = db_module.get_connection(db_path)
        db_module.init_db(conn)
        try:
            yield conn
        finally:
            conn.close()

    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as c:
        c.headers.update({"Authorization": f"Bearer {TEST_TOKEN}"})
        yield c
    app.dependency_overrides.clear()


@pytest.fixture
def unauthed_client(db_path: str, sync_token_env) -> TestClient:
    from app import db as db_module
    from app.main import app, get_db

    def override_get_db():
        conn = db_module.get_connection(db_path)
        db_module.init_db(conn)
        try:
            yield conn
        finally:
            conn.close()

    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as c:
        yield c
    app.dependency_overrides.clear()
```

`server/clipt-sync-server/tests/test_kdf.py`:
```python
import base64


def test_get_kdf_returns_params(client):
    response = client.get("/kdf")

    assert response.status_code == 200
    body = response.json()
    assert base64.b64decode(body["salt"])  # decodes without error, non-empty
    assert body["argon2_time_cost"] > 0
    assert body["argon2_memory_cost_kib"] > 0
    assert body["argon2_parallelism"] > 0


def test_get_kdf_returns_same_salt_on_second_call(client):
    first = client.get("/kdf").json()
    second = client.get("/kdf").json()

    assert first["salt"] == second["salt"]
```

`server/clipt-sync-server/tests/test_auth.py`:
```python
def test_kdf_without_token_is_401(unauthed_client):
    response = unauthed_client.get("/kdf")
    assert response.status_code == 401


def test_kdf_with_wrong_token_is_401(unauthed_client):
    unauthed_client.headers.update({"Authorization": "Bearer wrong-token"})
    response = unauthed_client.get("/kdf")
    assert response.status_code == 401
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `python -m pytest tests/test_kdf.py tests/test_auth.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'app.main'`.

- [ ] **Step 3: Implement `app/auth.py`**

```python
import hmac
import os

from fastapi import Header, HTTPException, status


def require_auth(authorization: str = Header(default="")) -> None:
    expected = os.environ.get("CLIPT_SYNC_TOKEN")
    if not expected:
        raise RuntimeError("CLIPT_SYNC_TOKEN environment variable is not set.")

    prefix = "Bearer "
    if not authorization.startswith(prefix):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Missing bearer token.")

    provided = authorization[len(prefix):]
    if not hmac.compare_digest(provided, expected):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid token.")
```

- [ ] **Step 4: Implement `app/models.py`**

```python
from pydantic import BaseModel


class KdfParamsResponse(BaseModel):
    salt: str  # base64
    argon2_time_cost: int
    argon2_memory_cost_kib: int
    argon2_parallelism: int


class GroupUpsertRequest(BaseModel):
    ciphertext: str  # base64
    based_on_version: int


class GroupDeleteRequest(BaseModel):
    based_on_version: int


class GroupUpsertResponse(BaseModel):
    id: str
    version: int


class GroupRecord(BaseModel):
    id: str
    ciphertext: str | None  # null for tombstoned (deleted) rows
    version: int
    updated_at: str
    deleted: bool


class GroupsPullResponse(BaseModel):
    groups: list[GroupRecord]
    latest_version: int
```

- [ ] **Step 5: Implement `app/main.py` with just `GET /kdf`**

```python
import base64
import secrets
import sqlite3
from datetime import datetime, timezone

from fastapi import Depends, FastAPI

from . import db as db_module
from .auth import require_auth
from .models import KdfParamsResponse

ARGON2_TIME_COST = 3
ARGON2_MEMORY_COST_KIB = 65536
ARGON2_PARALLELISM = 4

app = FastAPI(title="Clipt Sync")


def get_db():
    conn = db_module.get_connection()
    db_module.init_db(conn)
    try:
        yield conn
    finally:
        conn.close()


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


@app.get("/kdf", response_model=KdfParamsResponse, dependencies=[Depends(require_auth)])
def get_kdf(conn: sqlite3.Connection = Depends(get_db)) -> KdfParamsResponse:
    row = conn.execute(
        "SELECT salt, argon2_time_cost, argon2_memory_cost_kib, argon2_parallelism "
        "FROM kdf_params WHERE id = 1"
    ).fetchone()

    if row is None:
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            "SELECT salt, argon2_time_cost, argon2_memory_cost_kib, argon2_parallelism "
            "FROM kdf_params WHERE id = 1"
        ).fetchone()
        if row is None:
            salt = secrets.token_bytes(16)
            conn.execute(
                "INSERT INTO kdf_params "
                "(id, salt, argon2_time_cost, argon2_memory_cost_kib, argon2_parallelism, created_at) "
                "VALUES (1, ?, ?, ?, ?, ?)",
                (salt, ARGON2_TIME_COST, ARGON2_MEMORY_COST_KIB, ARGON2_PARALLELISM, _now_iso()),
            )
            row = (salt, ARGON2_TIME_COST, ARGON2_MEMORY_COST_KIB, ARGON2_PARALLELISM)
        conn.execute("COMMIT")

    return KdfParamsResponse(
        salt=base64.b64encode(row[0]).decode("ascii"),
        argon2_time_cost=row[1],
        argon2_memory_cost_kib=row[2],
        argon2_parallelism=row[3],
    )
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `python -m pytest tests/test_kdf.py tests/test_auth.py -v`
Expected: 4 passed.

- [ ] **Step 7: Commit**

```bash
git add app/auth.py app/models.py app/main.py tests/conftest.py tests/test_kdf.py tests/test_auth.py
git commit -m "clipt-sync-server: bearer auth and GET /kdf"
```

---

### Task 3: `PUT /groups/{id}` — upsert with optimistic concurrency

**Files:**
- Modify: `server/clipt-sync-server/app/main.py`
- Test: `server/clipt-sync-server/tests/test_groups_upsert.py`

**Interfaces:**
- Consumes: `db.allocate_version` (Task 1), `models.GroupUpsertRequest`/`GroupUpsertResponse` (Task 2).
- Produces: `PUT /groups/{group_id}` route.

- [ ] **Step 1: Write the failing tests**

`server/clipt-sync-server/tests/test_groups_upsert.py`:
```python
import base64


def _b64(s: str) -> str:
    return base64.b64encode(s.encode()).decode("ascii")


def test_create_new_group_with_based_on_version_zero(client):
    response = client.put(
        "/groups/g1",
        json={"ciphertext": _b64("hello"), "based_on_version": 0},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["id"] == "g1"
    assert body["version"] == 1


def test_update_existing_group_with_matching_version(client):
    first = client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0}).json()

    response = client.put(
        "/groups/g1",
        json={"ciphertext": _b64("v2"), "based_on_version": first["version"]},
    )

    assert response.status_code == 200
    assert response.json()["version"] == first["version"] + 1


def test_update_with_stale_version_is_409(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})

    response = client.put(
        "/groups/g1",
        json={"ciphertext": _b64("v2"), "based_on_version": 0},  # stale — already at version 1
    )

    assert response.status_code == 409


def test_create_with_nonzero_based_on_version_is_409(client):
    response = client.put(
        "/groups/g1",
        json={"ciphertext": _b64("v1"), "based_on_version": 5},
    )

    assert response.status_code == 409
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `python -m pytest tests/test_groups_upsert.py -v`
Expected: FAIL — 404 (route doesn't exist yet).

- [ ] **Step 3: Add the route to `app/main.py`**

Add these imports to the top of `app/main.py`:
```python
from fastapi import HTTPException, status
```

Add after `get_kdf`:
```python
from .models import GroupUpsertRequest, GroupUpsertResponse


@app.put("/groups/{group_id}", response_model=GroupUpsertResponse, dependencies=[Depends(require_auth)])
def upsert_group(
    group_id: str, body: GroupUpsertRequest, conn: sqlite3.Connection = Depends(get_db)
) -> GroupUpsertResponse:
    conn.execute("BEGIN IMMEDIATE")
    row = conn.execute("SELECT version FROM groups WHERE id = ?", (group_id,)).fetchone()
    current_version = row[0] if row is not None else 0

    if current_version != body.based_on_version:
        conn.execute("ROLLBACK")
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Version mismatch.")

    new_version = db_module.allocate_version(conn)
    ciphertext = base64.b64decode(body.ciphertext)
    conn.execute(
        "INSERT INTO groups (id, ciphertext, version, updated_at, deleted) VALUES (?, ?, ?, ?, 0) "
        "ON CONFLICT(id) DO UPDATE SET "
        "ciphertext = excluded.ciphertext, version = excluded.version, "
        "updated_at = excluded.updated_at, deleted = 0",
        (group_id, ciphertext, new_version, _now_iso()),
    )
    conn.execute("COMMIT")
    return GroupUpsertResponse(id=group_id, version=new_version)
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python -m pytest tests/test_groups_upsert.py -v`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add app/main.py tests/test_groups_upsert.py
git commit -m "clipt-sync-server: PUT /groups/{id} upsert with optimistic concurrency"
```

---

### Task 4: `DELETE /groups/{id}` — tombstone

**Files:**
- Modify: `server/clipt-sync-server/app/main.py`
- Test: `server/clipt-sync-server/tests/test_groups_delete.py`

**Interfaces:**
- Consumes: same as Task 3.
- Produces: `DELETE /groups/{group_id}` route.

- [ ] **Step 1: Write the failing tests**

`server/clipt-sync-server/tests/test_groups_delete.py`:
```python
import base64


def _b64(s: str) -> str:
    return base64.b64encode(s.encode()).decode("ascii")


def test_delete_existing_group(client):
    created = client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0}).json()

    response = client.delete("/groups/g1", json={"based_on_version": created["version"]})

    assert response.status_code == 200
    assert response.json()["version"] == created["version"] + 1


def test_delete_with_stale_version_is_409(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})

    response = client.delete("/groups/g1", json={"based_on_version": 0})

    assert response.status_code == 409


def test_delete_unknown_group_is_404(client):
    response = client.delete("/groups/does-not-exist", json={"based_on_version": 0})

    assert response.status_code == 404
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `python -m pytest tests/test_groups_delete.py -v`
Expected: FAIL — 405 Method Not Allowed (no DELETE route yet).

- [ ] **Step 3: Add the route to `app/main.py`**

```python
from .models import GroupDeleteRequest


@app.delete("/groups/{group_id}", response_model=GroupUpsertResponse, dependencies=[Depends(require_auth)])
def delete_group(
    group_id: str, body: GroupDeleteRequest, conn: sqlite3.Connection = Depends(get_db)
) -> GroupUpsertResponse:
    conn.execute("BEGIN IMMEDIATE")
    row = conn.execute("SELECT version FROM groups WHERE id = ?", (group_id,)).fetchone()

    if row is None:
        conn.execute("ROLLBACK")
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Group not found.")

    current_version = row[0]
    if current_version != body.based_on_version:
        conn.execute("ROLLBACK")
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Version mismatch.")

    new_version = db_module.allocate_version(conn)
    conn.execute(
        "UPDATE groups SET deleted = 1, version = ?, updated_at = ? WHERE id = ?",
        (new_version, _now_iso(), group_id),
    )
    conn.execute("COMMIT")
    return GroupUpsertResponse(id=group_id, version=new_version)
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python -m pytest tests/test_groups_delete.py -v`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add app/main.py tests/test_groups_delete.py
git commit -m "clipt-sync-server: DELETE /groups/{id} tombstone"
```

---

### Task 5: `GET /groups?since=` — incremental pull

**Files:**
- Modify: `server/clipt-sync-server/app/main.py`
- Test: `server/clipt-sync-server/tests/test_groups_pull.py`

**Interfaces:**
- Consumes: `models.GroupRecord`/`GroupsPullResponse` (Task 2).
- Produces: `GET /groups?since=<int>` route.

- [ ] **Step 1: Write the failing tests**

`server/clipt-sync-server/tests/test_groups_pull.py`:
```python
import base64


def _b64(s: str) -> str:
    return base64.b64encode(s.encode()).decode("ascii")


def test_pull_since_zero_returns_everything(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})
    client.put("/groups/g2", json={"ciphertext": _b64("v1"), "based_on_version": 0})

    response = client.get("/groups", params={"since": 0})

    assert response.status_code == 200
    body = response.json()
    ids = {g["id"] for g in body["groups"]}
    assert ids == {"g1", "g2"}
    assert body["latest_version"] == 2


def test_pull_since_filters_older_changes(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})
    g2 = client.put("/groups/g2", json={"ciphertext": _b64("v1"), "based_on_version": 0}).json()

    response = client.get("/groups", params={"since": 1})

    body = response.json()
    ids = [g["id"] for g in body["groups"]]
    assert ids == ["g2"]
    assert body["groups"][0]["version"] == g2["version"]


def test_pull_includes_tombstones_with_null_ciphertext(client):
    created = client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0}).json()
    client.delete("/groups/g1", json={"based_on_version": created["version"]})

    response = client.get("/groups", params={"since": 0})

    body = response.json()
    assert len(body["groups"]) == 1
    assert body["groups"][0]["id"] == "g1"
    assert body["groups"][0]["deleted"] is True
    assert body["groups"][0]["ciphertext"] is None


def test_pull_with_no_changes_returns_empty_list(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})

    response = client.get("/groups", params={"since": 1})

    body = response.json()
    assert body["groups"] == []
    assert body["latest_version"] == 1
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `python -m pytest tests/test_groups_pull.py -v`
Expected: FAIL — 404/405 (no `GET /groups` route yet).

- [ ] **Step 3: Add the route to `app/main.py`**

```python
from .models import GroupRecord, GroupsPullResponse


@app.get("/groups", response_model=GroupsPullResponse, dependencies=[Depends(require_auth)])
def pull_groups(since: int = 0, conn: sqlite3.Connection = Depends(get_db)) -> GroupsPullResponse:
    rows = conn.execute(
        "SELECT id, ciphertext, version, updated_at, deleted FROM groups "
        "WHERE version > ? ORDER BY version ASC",
        (since,),
    ).fetchall()

    groups = [
        GroupRecord(
            id=row[0],
            ciphertext=None if row[4] else base64.b64encode(row[1]).decode("ascii"),
            version=row[2],
            updated_at=row[3],
            deleted=bool(row[4]),
        )
        for row in rows
    ]

    meta_row = conn.execute("SELECT next_version FROM sync_meta WHERE id = 1").fetchone()
    latest_version = (meta_row[0] - 1) if meta_row else 0

    return GroupsPullResponse(groups=groups, latest_version=latest_version)
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python -m pytest tests/test_groups_pull.py -v`
Expected: 4 passed.

- [ ] **Step 4b: Run the full server test suite**

Run: `python -m pytest -v`
Expected: all tests across every file pass (18 total: 3 db + 2 kdf + 2 auth + 4 upsert + 3 delete + 4 pull).

- [ ] **Step 5: Commit**

```bash
git add app/main.py tests/test_groups_pull.py
git commit -m "clipt-sync-server: GET /groups?since= incremental pull"
```

---

### Task 6: Deployment artifacts (manual VPS step)

**Files:**
- Create: `server/clipt-sync-server/deploy/clipt-sync.service`
- Create: `server/clipt-sync-server/deploy/nginx-clipt-sync.conf`
- Create: `server/clipt-sync-server/README.md`

This task produces no code and has no automated test — its deliverable is deployment instructions the user runs themselves over SSH, since this session has no access to the VPS. Review the files below for correctness against the box's actual setup (nginx already runs as the `www-data`/default nginx user per Task 6's `curl -I` findings from the design spec; certbot is assumed already installed, since it's already issuing certs for the other subdomains on this box).

- [ ] **Step 1: Write the systemd unit template**

`server/clipt-sync-server/deploy/clipt-sync.service`:
```ini
[Unit]
Description=Clipt group sync API
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/home/ubuntu/clipt-sync-server
Environment=CLIPT_SYNC_TOKEN=REPLACE_WITH_GENERATED_TOKEN
Environment=CLIPT_SYNC_DB_PATH=/home/ubuntu/clipt-sync-server/clipt_sync.db
ExecStart=/home/ubuntu/clipt-sync-server/.venv/bin/gunicorn app.main:app \
    --workers 1 \
    --worker-class uvicorn.workers.UvicornWorker \
    --bind 127.0.0.1:8100
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

- [ ] **Step 2: Write the nginx site block template**

`server/clipt-sync-server/deploy/nginx-clipt-sync.conf`:
```nginx
server {
    listen 80;
    server_name sync.monkeyskin.au;

    location / {
        proxy_pass http://127.0.0.1:8100;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        limit_req zone=clipt_sync_zone burst=10 nodelay;
    }
}
```

Add this near the top of `/etc/nginx/nginx.conf`, inside the existing `http {}` block (alongside any other `limit_req_zone` directives already there):
```nginx
limit_req_zone $binary_remote_addr zone=clipt_sync_zone:10m rate=5r/s;
```

- [ ] **Step 3: Write the deployment README**

`server/clipt-sync-server/README.md`:
```markdown
# Clipt Sync Server — Deployment

Run entirely on the Ubuntu VPS (`vps-1ecd20d4`, `monkeyskin.au`), over SSH. Built and tested locally first — see the test suite under `tests/` (`python -m pytest -v`).

## 1. Copy the code to the box

From your workstation:
\```bash
scp -r server/clipt-sync-server ubuntu@monkeyskin.au:~/clipt-sync-server
\```

## 2. Install and set up a virtualenv (on the box)

\```bash
cd ~/clipt-sync-server
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
\```

## 3. Generate the bearer token

\```bash
python3 -c "import secrets; print(secrets.token_urlsafe(32))"
\```
Copy this into `deploy/clipt-sync.service`'s `CLIPT_SYNC_TOKEN=` line (replacing `REPLACE_WITH_GENERATED_TOKEN`) before the next step. This same token gets entered into Clipt's sync setup on every device — treat it like an API key.

## 4. Install the systemd service

\```bash
sudo cp deploy/clipt-sync.service /etc/systemd/system/clipt-sync.service
sudo systemctl daemon-reload
sudo systemctl enable --now clipt-sync
sudo systemctl status clipt-sync   # should show "active (running)"
\```

## 5. Add the nginx site and rate-limit zone

\```bash
sudo cp deploy/nginx-clipt-sync.conf /etc/nginx/sites-available/clipt-sync.conf
sudo ln -s /etc/nginx/sites-available/clipt-sync.conf /etc/nginx/sites-enabled/clipt-sync.conf
\```

Edit `/etc/nginx/nginx.conf` and add the `limit_req_zone` line from Task 6 Step 2 inside the `http {}` block, then:
\```bash
sudo nginx -t
sudo systemctl reload nginx
\```

## 6. Get a TLS certificate

\```bash
sudo certbot --nginx -d sync.monkeyskin.au
\```
(Matches how the existing subdomains on this box already got their certs — no new certbot setup needed.)

## 7. Verify from any machine

\```bash
curl -I https://sync.monkeyskin.au/kdf
# expect 401 (no token supplied) — confirms nginx + the service are both up

curl -H "Authorization: Bearer <the token from step 3>" https://sync.monkeyskin.au/kdf
# expect 200 with salt + argon2 params
\```

## Updating the deployed code later

\```bash
scp -r server/clipt-sync-server ubuntu@monkeyskin.au:~/clipt-sync-server
ssh ubuntu@monkeyskin.au 'cd ~/clipt-sync-server && .venv/bin/pip install -r requirements.txt && sudo systemctl restart clipt-sync'
\```
```

- [ ] **Step 4: Commit**

```bash
git add deploy/clipt-sync.service deploy/nginx-clipt-sync.conf README.md
git commit -m "clipt-sync-server: deployment artifacts and README"
```

---

## Self-Review Notes

- **Spec coverage:** `/kdf` (Task 2), `PUT /groups/{id}` optimistic concurrency (Task 3), `DELETE /groups/{id}` tombstone (Task 4), `GET /groups?since=` (Task 5), auth on every route (Task 2, exercised again implicitly by every other task's fixtures using the authed `client`), rate limiting + deployment (Task 6). SQLite schema matches the spec's `groups`/`kdf_params` tables exactly; `sync_meta` is an explicit addition needed to make `since` filtering well-ordered across every row (the spec's "version counter" language implies this — it isn't meaningful per-row in isolation).
- **Not covered here (client plan owns these):** actual encryption/decryption, passphrase handling, and anything running on a Windows device — this plan's server never sees plaintext by design.
