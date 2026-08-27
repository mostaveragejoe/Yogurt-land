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
    conn.execute(_DEALS_DDL)
    conn.execute(_CONTACTS_DDL)
    _migrate(conn)
    _migrate_legacy_contacts(conn)
    conn.commit()
    return conn


def _migrate(conn: sqlite3.Connection) -> None:
    """Add any columns introduced since the database was created."""
    event_cols = {r["name"] for r in conn.execute("PRAGMA table_info(events)")}
    if "contact_id" not in event_cols:
        conn.execute("ALTER TABLE events ADD COLUMN contact_id INTEGER")

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
              date: str, note: str = "", contact_id: int | None = None) -> None:
    conn.execute(
        "INSERT INTO events (partner_id, date, kind, note, contact_id, created_at) "
        "VALUES (?, ?, ?, ?, ?, ?)",
        (partner_id, date, kind, note, contact_id,
         _dt.datetime.now().isoformat(timespec="seconds")),
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


# --- Deals --------------------------------------------------------------
# What a partner actually sent us. The whole point of outcome tracking:
# without this, "which channel works" is unanswerable forever, because it
# cannot be reconstructed after the fact.

DEAL_STATUSES = ("referred", "underwriting", "funded", "declined", "withdrawn")
CLOSED_STATUSES = ("funded", "declined", "withdrawn")

_DEALS_DDL = """
CREATE TABLE IF NOT EXISTS deals (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    partner_id    TEXT NOT NULL,
    product       TEXT,
    status        TEXT NOT NULL,
    referred_date TEXT NOT NULL,
    closed_date   TEXT,
    amount        REAL,
    revenue       REAL,
    note          TEXT,
    created_at    TEXT NOT NULL
)
"""


def add_deal(conn: sqlite3.Connection, partner_id: str, product: str,
             referred_date: str, amount: float | None = None,
             note: str = "") -> int:
    cur = conn.execute(
        "INSERT INTO deals (partner_id, product, status, referred_date, "
        "amount, note, created_at) VALUES (?, ?, 'referred', ?, ?, ?, ?)",
        (partner_id, product, referred_date, amount, note,
         _dt.datetime.now().isoformat(timespec="seconds")),
    )
    return cur.lastrowid


def update_deal(conn: sqlite3.Connection, deal_id: int, status: str,
                closed_date: str | None = None, amount: float | None = None,
                revenue: float | None = None, note: str | None = None) -> bool:
    existing = get_deal(conn, deal_id)
    if not existing:
        return False
    changes = {"status": status}
    if status in CLOSED_STATUSES:
        changes["closed_date"] = closed_date or _dt.date.today().isoformat()
    if amount is not None:
        changes["amount"] = amount
    if revenue is not None:
        changes["revenue"] = revenue
    if note:
        changes["note"] = f"{existing['note']} | {note}".strip(" |") \
            if existing["note"] else note
    sets = ", ".join(f"{k}=?" for k in changes)
    conn.execute(f"UPDATE deals SET {sets} WHERE id=?",
                 [*changes.values(), deal_id])
    return True


def get_deal(conn: sqlite3.Connection, deal_id: int) -> dict | None:
    row = conn.execute("SELECT * FROM deals WHERE id = ?", (deal_id,)).fetchone()
    return dict(row) if row else None


def all_deals(conn: sqlite3.Connection, partner_id: str | None = None,
              status: str | None = None) -> list[dict]:
    query, params = "SELECT * FROM deals WHERE 1=1", []
    if partner_id:
        query += " AND partner_id = ?"
        params.append(partner_id)
    if status:
        query += " AND status = ?"
        params.append(status)
    query += " ORDER BY referred_date, id"
    return [dict(r) for r in conn.execute(query, params)]


def first_contact_date(conn: sqlite3.Connection, partner_id: str) -> str | None:
    """Date of the first outbound touch -- the clock start for time-to-deal."""
    row = conn.execute(
        "SELECT MIN(date) AS d FROM events WHERE partner_id = ? "
        "AND kind IN ('messaged', 'nudge')", (partner_id,)).fetchone()
    return row["d"] if row and row["d"] else None


# --- Contacts -----------------------------------------------------------
# A partner is an institution; a contact is a person inside it. The
# distinction matters because outreach fails at the person level, not the
# institution level: the CLO who never opens LinkedIn says nothing about
# whether the CEO would answer.

CONTACT_STATUSES = ("untried", "active", "cold", "bounced", "left_company",
                    "do_not_contact")
# Statuses that mean this person is no longer a route into the institution.
CONTACT_EXHAUSTED = ("cold", "bounced", "left_company", "do_not_contact")

_CONTACTS_DDL = """
CREATE TABLE IF NOT EXISTS contacts (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    partner_id   TEXT NOT NULL,
    name         TEXT NOT NULL,
    title        TEXT,
    linkedin_url TEXT,
    email        TEXT,
    phone        TEXT,
    is_primary   INTEGER DEFAULT 0,
    status       TEXT NOT NULL DEFAULT 'untried',
    note         TEXT,
    created_at   TEXT NOT NULL
)
"""


def add_contact(conn: sqlite3.Connection, partner_id: str, name: str,
                title: str = "", linkedin_url: str = "", email: str = "",
                phone: str = "", is_primary: bool = False,
                note: str = "") -> int:
    cur = conn.execute(
        "INSERT INTO contacts (partner_id, name, title, linkedin_url, email, "
        "phone, is_primary, status, note, created_at) "
        "VALUES (?, ?, ?, ?, ?, ?, ?, 'untried', ?, ?)",
        (partner_id, name, title, linkedin_url, email, phone,
         int(bool(is_primary)), note,
         _dt.datetime.now().isoformat(timespec="seconds")),
    )
    return cur.lastrowid


def contacts_for(conn: sqlite3.Connection, partner_id: str) -> list[dict]:
    rows = conn.execute(
        "SELECT * FROM contacts WHERE partner_id = ? "
        "ORDER BY is_primary DESC, id", (partner_id,))
    return [dict(r) for r in rows]


def get_contact(conn: sqlite3.Connection, contact_id: int) -> dict | None:
    row = conn.execute("SELECT * FROM contacts WHERE id = ?",
                       (contact_id,)).fetchone()
    return dict(row) if row else None


def update_contact(conn: sqlite3.Connection, contact_id: int, **changes) -> bool:
    allowed = {"name", "title", "linkedin_url", "email", "phone",
               "is_primary", "status", "note"}
    changes = {k: v for k, v in changes.items() if k in allowed and v is not None}
    if not changes:
        return False
    if "is_primary" in changes:
        changes["is_primary"] = int(bool(changes["is_primary"]))
    sets = ", ".join(f"{k}=?" for k in changes)
    cur = conn.execute(f"UPDATE contacts SET {sets} WHERE id=?",
                       [*changes.values(), contact_id])
    return cur.rowcount > 0


def find_contact(conn: sqlite3.Connection, partner_id: str,
                 query: str) -> list[dict]:
    """Resolve a contact within one partner by id or name fragment."""
    if str(query).isdigit():
        found = get_contact(conn, int(query))
        if found and found["partner_id"] == partner_id:
            return [found]
        return []
    like = f"%{str(query).strip().lower()}%"
    rows = conn.execute(
        "SELECT * FROM contacts WHERE partner_id = ? AND lower(name) LIKE ? "
        "ORDER BY is_primary DESC, id", (partner_id, like))
    return [dict(r) for r in rows]


def untried_contacts(conn: sqlite3.Connection, partner_id: str) -> list[dict]:
    """People at this institution not yet exhausted -- the remaining routes in."""
    return [c for c in contacts_for(conn, partner_id)
            if c["status"] not in CONTACT_EXHAUSTED]


def contact_unanswered(conn: sqlite3.Connection, contact_id: int) -> int:
    """Outbound touches to one person since they last responded."""
    from .cadence import INBOUND, OUTBOUND
    rows = conn.execute(
        "SELECT kind FROM events WHERE contact_id = ? ORDER BY date, id",
        (contact_id,))
    count = 0
    for row in reversed(list(rows)):
        if row["kind"] in INBOUND:
            break
        if row["kind"] in OUTBOUND:
            count += 1
    return count


def _migrate_legacy_contacts(conn: sqlite3.Connection) -> int:
    """Lift each partner's single contact_name field into a contacts row.

    Runs once and is idempotent: a partner that already has any contact row is
    skipped, so re-running never duplicates. The legacy columns are left in
    place rather than dropped -- nothing reads them for outreach any more, but
    destroying data during an automatic migration is not worth the tidiness.
    """
    migrated = 0
    rows = conn.execute(
        "SELECT id, contact_name, contact_title, linkedin_url FROM partners "
        "WHERE contact_name IS NOT NULL AND contact_name != ''").fetchall()
    for row in rows:
        existing = conn.execute(
            "SELECT COUNT(*) AS n FROM contacts WHERE partner_id = ?",
            (row["id"],)).fetchone()
        if existing["n"]:
            continue
        add_contact(conn, row["id"], row["contact_name"],
                    title=row["contact_title"] or "",
                    linkedin_url=row["linkedin_url"] or "",
                    is_primary=True, note="migrated from partner record")
        migrated += 1
    return migrated
