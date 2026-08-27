"""Generic CSV import for partner types with no clean public feed.

CPAs, business brokers, CRE brokers and equipment dealers have no NCUA-style
dataset. You build those lists by hand or by export -- Minnesota Board of
Accountancy licensee data, IBBA/MNBBA member directories, a LinkedIn Sales
Navigator export -- and drop them in here.

Expected headers (all optional except `name`):

    name, partner_type, city, state, website, phone, linkedin_url,
    contact_name, contact_title, headcount, does_attest_work,
    has_advisory_practice, industry_specialties, active_listings,
    years_active, warm_intro_path, notes

`industry_specialties` is pipe- or comma-separated, e.g. "construction|dental".
Booleans accept y/yes/true/1.
"""

from __future__ import annotations

import csv
import re
from pathlib import Path

from .models import ID_PREFIX, Partner, PartnerType

_TRUE = {"y", "yes", "true", "1", "t"}


def _norm(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", (text or "").lower()).strip("-")


def _boolish(raw) -> bool | None:
    if raw in (None, ""):
        return None
    return str(raw).strip().lower() in _TRUE


def _int(raw) -> int | None:
    try:
        return int(float(str(raw).replace(",", "").strip()))
    except (TypeError, ValueError):
        return None


def _split(raw) -> list[str]:
    if not raw:
        return []
    return [part.strip() for part in re.split(r"[|,;]", str(raw)) if part.strip()]


def make_id(partner_type: str, name: str, city: str, taken: set[str]) -> str:
    """Build a stable, unique id for a partner.

    Two firms sharing a name is ordinary in real data -- "Smith & Associates"
    exists in every city -- so the city is part of the id. When even that
    collides, a counter is appended rather than letting the second record
    silently overwrite the first.
    """
    prefix = ID_PREFIX.get(partner_type, "other")
    parts = [prefix, _norm(name)]
    if city:
        parts.append(_norm(city))
    base = "-".join(p for p in parts if p)

    candidate, suffix = base, 2
    while candidate in taken:
        candidate = f"{base}-{suffix}"
        suffix += 1
    taken.add(candidate)
    return candidate


def load(path: str | Path, default_type: str = PartnerType.CPA_FIRM.value,
         state: str = "MN") -> tuple[list[Partner], dict]:
    """Parse a hand-built CSV into Partner records."""
    with open(path, newline="", encoding="utf-8-sig", errors="replace") as fh:
        rows = list(csv.DictReader(fh))

    valid_types = {t.value for t in PartnerType}
    partners: list[Partner] = []
    skipped = 0
    bad_types: set[str] = set()
    taken_ids: set[str] = set()
    collisions = 0

    for row in rows:
        row = { (k or "").strip().lower(): v for k, v in row.items() }
        name = (row.get("name") or "").strip()
        if not name:
            skipped += 1
            continue

        ptype = (row.get("partner_type") or default_type).strip().lower()
        if ptype not in valid_types:
            bad_types.add(ptype)
            ptype = default_type

        city = (row.get("city") or "").strip()
        partner_id = make_id(ptype, name, city, taken_ids)
        if partner_id.rsplit("-", 1)[-1].isdigit():
            collisions += 1

        partners.append(Partner(
            id=partner_id,
            name=name,
            partner_type=ptype,
            city=city,
            state=(row.get("state") or state).strip().upper(),
            website=(row.get("website") or "").strip(),
            phone=(row.get("phone") or "").strip(),
            linkedin_url=(row.get("linkedin_url") or "").strip(),
            contact_name=(row.get("contact_name") or "").strip(),
            contact_title=(row.get("contact_title") or "").strip(),
            headcount=_int(row.get("headcount")),
            does_attest_work=_boolish(row.get("does_attest_work")),
            has_advisory_practice=bool(_boolish(row.get("has_advisory_practice"))),
            industry_specialties=_split(row.get("industry_specialties")),
            active_listings=_int(row.get("active_listings")),
            years_active=_int(row.get("years_active")),
            warm_intro_path=(row.get("warm_intro_path") or "").strip(),
            notes=(row.get("notes") or "").strip(),
        ))

    diagnostics = {
        "file": str(path),
        "rows_read": len(rows),
        "imported": len(partners),
        "skipped_no_name": skipped,
        "unrecognized_types": sorted(bad_types),
        "name_collisions": collisions,
    }
    return partners, diagnostics


TEMPLATE_HEADERS = [
    "name", "partner_type", "city", "state", "website", "phone", "linkedin_url",
    "contact_name", "contact_title", "headcount", "does_attest_work",
    "has_advisory_practice", "industry_specialties", "active_listings",
    "years_active", "warm_intro_path", "notes",
]


def write_template(path: str | Path) -> None:
    """Emit an empty CSV with the expected headers and one example row."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", newline="", encoding="utf-8") as fh:
        writer = csv.writer(fh)
        writer.writerow(TEMPLATE_HEADERS)
        writer.writerow([
            "Example CPA Group", "cpa_firm", "Minneapolis", "MN",
            "https://example.com", "612-555-0100", "", "Jane Doe", "Managing Partner",
            "24", "yes", "yes", "construction|trucking", "", "", "", "",
        ])
