import base64
import secrets
import sqlite3
from datetime import datetime, timezone

from fastapi import Depends, FastAPI, HTTPException, status

from . import db as db_module
from .auth import require_auth
from .models import (
    GroupDeleteRequest,
    GroupRecord,
    GroupsPullResponse,
    GroupUpsertRequest,
    GroupUpsertResponse,
    KdfParamsResponse,
)

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
