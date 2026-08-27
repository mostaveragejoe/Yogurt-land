"""Whole-database backup and restore.

The database is a single SQLite file holding months of relationship history
that cannot be reconstructed from any external source -- NCUA can tell you a
credit union's assets again, but nothing can tell you that you messaged their
CLO three times and he never replied. That asymmetry is the reason this
module exists.

JSON rather than a SQLite copy: readable, diffable, greppable, and restorable
into a schema that has moved on since the backup was taken.
"""

from __future__ import annotations

import datetime as dt
import json
from pathlib import Path

from .models import COLUMNS, Partner

# Bumped when the shape of the backup file itself changes, not when the
# database gains a column -- restore tolerates unknown and missing columns.
BACKUP_FORMAT_VERSION = 1

TABLES = ("partners", "contacts", "events", "deals")


def dump(conn) -> dict:
    """Read the entire database into a plain dict."""
    payload = {
        "format_version": BACKUP_FORMAT_VERSION,
        "created_at": dt.datetime.now().isoformat(timespec="seconds"),
        "tables": {},
        "counts": {},
    }
    for table in TABLES:
        rows = [dict(r) for r in conn.execute(f"SELECT * FROM {table}")]
        payload["tables"][table] = rows
        payload["counts"][table] = len(rows)
    return payload


def write(conn, path: str | Path) -> dict:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = dump(conn)
    # Write to a temporary file and replace, so an interrupted backup cannot
    # truncate a good one.
    temp = path.with_suffix(path.suffix + ".partial")
    temp.write_text(json.dumps(payload, indent=2, ensure_ascii=False))
    temp.replace(path)
    return payload["counts"]


def read(path: str | Path) -> dict:
    payload = json.loads(Path(path).read_text(encoding="utf-8"))
    if "tables" not in payload:
        raise ValueError(f"{path} is not a prospector backup (no 'tables' key).")
    version = payload.get("format_version", 0)
    if version > BACKUP_FORMAT_VERSION:
        raise ValueError(
            f"Backup format version {version} is newer than this tool "
            f"understands ({BACKUP_FORMAT_VERSION}). Upgrade the tool.")
    return payload


def is_empty(conn) -> bool:
    return all(
        conn.execute(f"SELECT COUNT(*) AS n FROM {t}").fetchone()["n"] == 0
        for t in TABLES
    )


def restore(conn, payload: dict) -> dict:
    """Replace the database contents with a backup's.

    Columns absent from the target schema are dropped and columns absent from
    the backup are left at their defaults, so a backup taken before a schema
    change still restores.
    """
    counts: dict[str, int] = {}
    for table in TABLES:
        rows = payload["tables"].get(table, [])
        live_columns = {r["name"] for r in conn.execute(f"PRAGMA table_info({table})")}
        conn.execute(f"DELETE FROM {table}")
        written = 0
        for row in rows:
            usable = {k: v for k, v in row.items() if k in live_columns}
            if not usable:
                continue
            placeholders = ", ".join("?" for _ in usable)
            conn.execute(
                f"INSERT INTO {table} ({', '.join(usable)}) VALUES ({placeholders})",
                list(usable.values()))
            written += 1
        counts[table] = written
    conn.commit()
    return counts


def dropped_columns(conn, payload: dict) -> dict[str, list[str]]:
    """Columns present in the backup but not in the current schema."""
    out: dict[str, list[str]] = {}
    for table in TABLES:
        rows = payload["tables"].get(table, [])
        if not rows:
            continue
        live = {r["name"] for r in conn.execute(f"PRAGMA table_info({table})")}
        missing = sorted(set(rows[0].keys()) - live)
        if missing:
            out[table] = missing
    return out
