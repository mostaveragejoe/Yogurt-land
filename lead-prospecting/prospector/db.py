"""SQLite persistence. Stdlib only -- no install, one file you can back up."""

from __future__ import annotations

import datetime as _dt
import sqlite3
from pathlib import Path

from .models import Partner, COLUMNS

DEFAULT_DB = Path("data/partners.db")

_NUMERIC = {
    "total_assets", "net_worth", "business_loans_outstanding",
    "business_loan_count", "headcount", "active_listings", "years_active",
    "fit_score", "capacity_score", "access_score", "total_score",
    "low_income_designated", "has_advisory_practice", "do_not_contact",
    "does_attest_work",
}


def _ddl() -> str:
    cols = []
    for name in COLUMNS:
        if name == "id":
            cols.append("id TEXT PRIMARY KEY")
            continue
        kind = "REAL" if name in _NUMERIC else "TEXT"
        cols.append(f"{name} {kind}")
    return f"CREATE TABLE IF NOT EXISTS partners ({', '.join(cols)})"


def connect(path: str | Path = DEFAULT_DB) -> sqlite3.Connection:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(path)
    conn.row_factory = sqlite3.Row
    conn.execute(_ddl())
    conn.execute(_EVENTS_DDL)
    _migrate(conn)
    conn.commit()
    return conn


def _migrate(conn: sqlite3.Connection) -> None:
    """Add any columns introduced since the database was created."""
    existing = {r["name"] for r in conn.execute("PRAGMA table_info(partners)")}
    for name in COLUMNS:
        if name not in existing:
            kind = "REAL" if name in _NUMERIC else "TEXT"
            conn.execute(f"ALTER TABLE partners ADD COLUMN {name} {kind}")


# Hand-entered relationship fields: a re-ingest must never clobber these.
GUARDED = ("stage", "owner", "last_touch", "next_action", "next_action_due",
           "notes", "contact_name", "contact_title", "linkedin_url",
           "warm_intro_path")


def upsert(conn: sqlite3.Connection, partner: Partner) -> None:
    """Insert or update, preserving hand-entered pipeline fields on re-ingest."""
    row = partner.to_row()
    prior = conn.execute("SELECT * FROM partners WHERE id = ?", (partner.id,)).fetchone()
    if prior:
        for guarded in GUARDED:
            if prior[guarded]:
                row[guarded] = prior[guarded]
        if prior["do_not_contact"]:
            row["do_not_contact"] = 1
    placeholders = ", ".join("?" for _ in COLUMNS)
    assignments = ", ".join(f"{c}=excluded.{c}" for c in COLUMNS if c != "id")
    conn.execute(
        f"INSERT INTO partners ({', '.join(COLUMNS)}) VALUES ({placeholders}) "
        f"ON CONFLICT(id) DO UPDATE SET {assignments}",
        [row[c] for c in COLUMNS],
    )


def all_partners(conn: sqlite3.Connection, partner_type: str | None = None,
                 stage: str | None = None, state: str | None = None) -> list[Partner]:
    query, params = "SELECT * FROM partners WHERE 1=1", []
    if partner_type:
        query += " AND partner_type = ?"
        params.append(partner_type)
    if stage:
        query += " AND stage = ?"
        params.append(stage)
    if state:
        query += " AND state = ?"
        params.append(state)
    return [Partner.from_row(r) for r in conn.execute(query, params)]


def get(conn: sqlite3.Connection, partner_id: str) -> Partner | None:
    row = conn.execute("SELECT * FROM partners WHERE id = ?", (partner_id,)).fetchone()
    return Partner.from_row(row) if row else None


def update_fields(conn: sqlite3.Connection, partner_id: str, **changes) -> bool:
    """Patch named columns on one partner."""
    changes = {k: v for k, v in changes.items() if k in COLUMNS and v is not None}
    if not changes:
        return False
    sets = ", ".join(f"{k}=?" for k in changes)
    cur = conn.execute(f"UPDATE partners SET {sets} WHERE id=?",
                       [*changes.values(), partner_id])
    return cur.rowcount > 0

# --- Event log ----------------------------------------------------------
# Every interaction is appended here rather than overwriting a field, so the
# follow-up logic can reason about the actual sequence: how many unanswered
# touches, when the last inbound reply landed, whether a stage ever advanced.

_EVENTS_DDL = """
CREATE TABLE IF NOT EXISTS events (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    partner_id TEXT NOT NULL,
    date       TEXT NOT NULL,
    kind       TEXT NOT NULL,
    note       TEXT,
    created_at TEXT NOT NULL
)
"""


def add_event(conn: sqlite3.Connection, partner_id: str, kind: str,
              date: str, note: str = "") -> None:
    conn.execute(
        "INSERT INTO events (partner_id, date, kind, note, created_at) "
        "VALUES (?, ?, ?, ?, ?)",
        (partner_id, date, kind, note, _dt.datetime.now().isoformat(timespec="seconds")),
    )


def events_for(conn: sqlite3.Connection, partner_id: str) -> list[dict]:
    rows = conn.execute(
        "SELECT * FROM events WHERE partner_id = ? ORDER BY date, id", (partner_id,))
    return [dict(r) for r in rows]


def unanswered_touches(conn: sqlite3.Connection, partner_id: str) -> int:
    """Outbound messages sent since the last time they responded."""
    from .cadence import INBOUND, OUTBOUND
    count = 0
    for event in reversed(events_for(conn, partner_id)):
        if event["kind"] in INBOUND:
            break
        if event["kind"] in OUTBOUND:
            count += 1
    return count


def find(conn: sqlite3.Connection, query: str) -> list[Partner]:
    """Look a partner up by id or by a fragment of its name.

    Typing `cu-90001` during a LinkedIn session is friction nobody sustains,
    so `northgate` resolves too. Exact id wins outright; otherwise every
    name match is returned for the caller to disambiguate.
    """
    exact = get(conn, query)
    if exact:
        return [exact]
    like = f"%{query.strip().lower()}%"
    rows = conn.execute(
        "SELECT * FROM partners WHERE lower(name) LIKE ? OR lower(id) LIKE ? "
        "ORDER BY total_score DESC", (like, like))
    return [Partner.from_row(r) for r in rows]
