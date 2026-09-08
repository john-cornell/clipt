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
