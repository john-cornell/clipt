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
