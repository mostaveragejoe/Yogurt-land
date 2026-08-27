"""Import a LinkedIn Sales Navigator lead export.

Sales Navigator exports rows of *people*, not institutions, which is exactly
the shape the contacts table wants: each row becomes one contact, and the
company column becomes (or joins) a partner.

Two things the export cannot tell us:

- **Partner type.** Nothing in a Sales Navigator row says "this is a credit
  union whose decline flow we care about". Company names carry the signal
  often enough to be worth guessing, so names are matched against a keyword
  table and everything unresolved is written to a file for hand-typing rather
  than silently defaulting into one bucket.
- **Call-report figures.** No assets, no net worth, no cap pressure. Credit
  unions imported this way score on defaults until the NCUA import fills them
  in -- which it will, by name, on the next run.
"""

from __future__ import annotations

import csv
import re
from pathlib import Path

from .models import ID_PREFIX, Partner, PartnerType

# Header fragments, checked case-insensitively against the export's columns.
COLUMN_CANDIDATES = {
    "first_name": ["first name", "firstname", "first"],
    "last_name": ["last name", "lastname", "last"],
    # "name" alone is deliberately absent: it substring-matches "First Name"
    # and "Last Name", which silently reduced every person to their surname.
    "full_name": ["full name", "fullname", "lead name", "contact name"],
    "title": ["title", "position", "job title", "headline"],
    "company": ["company name", "company", "account", "organization"],
    "profile_url": ["profile url", "linkedin profile", "linkedin url",
                    "person linkedin", "profile link", "url"],
    "location": ["location", "geography", "region"],
    "website": ["company website", "website", "domain"],
    "email": ["email", "email address"],
    "phone": ["phone", "phone number"],
}

# Company-name keywords to partner type. Order matters: the first match wins,
# so put the specific patterns above the generic ones.
TYPE_KEYWORDS: list[tuple[str, str]] = [
    (r"\bcredit union\b|\bfcu\b|\bc\.?u\.?$", PartnerType.CREDIT_UNION.value),
    (r"\bbank\b|\bbancorp\b|\bbanc\b|\bsavings\b|\bbancshares\b",
     PartnerType.COMMUNITY_BANK.value),
    (r"\bbusiness broker|\bbusiness brokerage|\bm&a\b|\bbusiness advisors?\b|"
     r"\bbusiness sales\b", PartnerType.BUSINESS_BROKER.value),
    (r"\bcpas?\b|\bc\.p\.a\b|\baccounting\b|\baccountants?\b|\btax\b|"
     r"\bbookkeep", PartnerType.CPA_FIRM.value),
    (r"\brealty\b|\breal estate\b|\bcommercial properties\b|\bproperties\b|"
     r"\bbrokerage\b", PartnerType.CRE_BROKER.value),
    (r"\bequipment\b|\bmachinery\b|\btractor\b|\bimplement\b|\btruck sales\b",
     PartnerType.EQUIPMENT_DEALER.value),
    (r"\bmedical billing\b|\bdental\b|\bveterinary\b|\bpractice management\b|"
     r"\bhealthcare consult", PartnerType.MEDICAL_ADJACENT.value),
    (r"\blaw\b|\blegal\b|\battorneys?\b|\bcounsel\b",
     PartnerType.ATTORNEY.value),
]


def _norm_header(text: str) -> str:
    return re.sub(r"[^a-z0-9 ]+", " ", (text or "").lower()).strip()


def _slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", (text or "").lower()).strip("-")


def resolve_columns(headers: list[str]) -> dict[str, str]:
    """Map our field names onto the export's headers.

    Two passes: every exact match is taken first, then substring matches fill
    the gaps from headers nobody claimed. Doing it in one pass let a loose
    candidate steal a column that another field matched exactly.
    """
    normalized = {h: _norm_header(h) for h in headers}
    resolved: dict[str, str] = {}
    claimed: set[str] = set()

    for field, candidates in COLUMN_CANDIDATES.items():
        for candidate in candidates:
            cand = _norm_header(candidate)
            exact = [h for h, n in normalized.items()
                     if n == cand and h not in claimed]
            if exact:
                resolved[field] = exact[0]
                claimed.add(exact[0])
                break

    for field, candidates in COLUMN_CANDIDATES.items():
        if field in resolved:
            continue
        for candidate in candidates:
            cand = _norm_header(candidate)
            partial = [h for h, n in normalized.items()
                       if cand in n and h not in claimed]
            if partial:
                chosen = sorted(partial, key=len)[0]
                resolved[field] = chosen
                claimed.add(chosen)
                break
    return resolved


def infer_type(company: str) -> tuple[str, bool]:
    """Guess a partner type from a company name.

    Returns (type, confident). An unconfident guess is the caller's signal to
    route the row for hand-typing rather than importing it into the wrong
    bucket, where it would be scored by the wrong rubric.
    """
    name = (company or "").lower()
    for pattern, ptype in TYPE_KEYWORDS:
        if re.search(pattern, name):
            return ptype, True
    return PartnerType.OTHER.value, False


def load(path: str | Path, default_type: str | None = None,
         state: str = "MN") -> tuple[list[Partner], list[dict], dict]:
    """Parse an export into (partners, contacts, diagnostics).

    Contacts are returned as dicts carrying `partner_id` rather than being
    written here, so the caller owns all database access.
    """
    with open(path, newline="", encoding="utf-8-sig", errors="replace") as fh:
        reader = csv.DictReader(fh)
        headers = reader.fieldnames or []
        cols = resolve_columns(headers)
        rows = list(reader)

    if "company" not in cols:
        raise SystemExit(
            f"No company column found in {path}.\n"
            f"Headers present: {headers}\n"
            "A Sales Navigator export needs a company column -- each row "
            "becomes a person at an institution.")

    def cell(row, field):
        header = cols.get(field)
        return (row.get(header) or "").strip() if header else ""

    partners: dict[str, Partner] = {}
    contacts: list[dict] = []
    unresolved: list[dict] = []
    skipped_no_company = 0
    seen_people: set[tuple[str, str]] = set()

    for row in rows:
        company = cell(row, "company")
        if not company:
            skipped_no_company += 1
            continue

        # Prefer assembling from first + last: a "full name" column that
        # resolved loosely may hold only part of the name.
        name = " ".join(x for x in (cell(row, "first_name"),
                                    cell(row, "last_name")) if x).strip()
        if not name:
            name = cell(row, "full_name")

        ptype, confident = infer_type(company)
        if not confident and default_type:
            ptype, confident = default_type, True

        if not confident:
            unresolved.append({
                "name": company, "partner_type": "", "city": cell(row, "location"),
                "state": state, "website": cell(row, "website"),
                "contact_name": name, "contact_title": cell(row, "title"),
                "linkedin_url": cell(row, "profile_url"),
            })
            continue

        partner_id = f"{ID_PREFIX.get(ptype, 'other')}-{_slug(company)}"
        if partner_id not in partners:
            partners[partner_id] = Partner(
                id=partner_id,
                name=company,
                partner_type=ptype,
                city=cell(row, "location").split(",")[0].strip(),
                state=state,
                website=cell(row, "website"),
            )

        if not name:
            continue
        key = (partner_id, name.lower())
        if key in seen_people:
            continue
        seen_people.add(key)
        contacts.append({
            "partner_id": partner_id,
            "name": name,
            "title": cell(row, "title"),
            "linkedin_url": cell(row, "profile_url"),
            "email": cell(row, "email"),
            "phone": cell(row, "phone"),
        })

    diagnostics = {
        "file": str(path),
        "rows_read": len(rows),
        "partners": len(partners),
        "contacts": len(contacts),
        "unresolved_type": len(unresolved),
        "skipped_no_company": skipped_no_company,
        "columns_matched": cols,
        "columns_unmatched": [f for f in COLUMN_CANDIDATES if f not in cols],
    }
    return list(partners.values()), contacts, diagnostics, unresolved


def write_unresolved(rows: list[dict], path: str | Path) -> int:
    """Write rows we could not type into a CSV the normal importer accepts."""
    if not rows:
        return 0
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = ["name", "partner_type", "city", "state", "website",
              "contact_name", "contact_title", "linkedin_url"]
    # One row per company, not per person -- the type is a company attribute.
    by_company: dict[str, dict] = {}
    for row in rows:
        by_company.setdefault(row["name"].lower(), row)
    with open(path, "w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=fields)
        writer.writeheader()
        writer.writerows(by_company.values())
    return len(by_company)
