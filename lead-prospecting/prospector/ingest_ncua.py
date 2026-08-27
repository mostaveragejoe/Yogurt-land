"""Turn NCUA call-report data into scored credit-union referral prospects.

NCUA publishes an "all active federally insured credit unions" dataset each
quarter: https://ncua.gov/analysis/credit-union-corporate-call-report-data

Column headers vary by export vintage and by which file in the ZIP you use, so
this module maps loosely rather than hardcoding account codes: it looks for a
header matching any of several candidate substrings. Run `--inspect` first to
see what your file actually contains, and pass `--map` to override.
"""

from __future__ import annotations

import csv
import json
import re
from pathlib import Path

from .models import Partner, PartnerType

# Candidate header fragments, checked case-insensitively, best match first.
DEFAULT_MAP = {
    "name":         ["cu_name", "credit union name", "cuname", "name"],
    "city":         ["city"],
    "state":        ["state", "st"],
    "charter":      ["cu_number", "charter", "rssd", "cu number"],
    "total_assets": ["total assets", "totalassets", "acct_010", "assets"],
    "net_worth":    ["total net worth", "net worth", "acct_997", "networth"],
    "business_loans_outstanding": [
        "total commercial loans", "member business loans",
        "total amount of business loans", "commercial loans outstanding",
        "business loans", "mbl",
    ],
    "business_loan_count": [
        "number of commercial loans", "number of business loans",
        "count of business loans",
    ],
    "low_income": ["low income", "low_income", "lid"],
}


def _norm(text: str) -> str:
    return re.sub(r"[^a-z0-9 ]+", " ", (text or "").lower()).strip()


def resolve_columns(headers: list[str], overrides: dict | None = None) -> dict[str, str]:
    """Match our field names to the headers actually present in the file."""
    mapping = dict(DEFAULT_MAP)
    if overrides:
        for field, candidates in overrides.items():
            mapping[field] = candidates if isinstance(candidates, list) else [candidates]

    normalized = {h: _norm(h) for h in headers}
    resolved: dict[str, str] = {}
    for field, candidates in mapping.items():
        for candidate in candidates:
            cand = _norm(candidate)
            exact = [h for h, n in normalized.items() if n == cand]
            if exact:
                resolved[field] = exact[0]
                break
            partial = [h for h, n in normalized.items() if cand in n]
            if partial:
                resolved[field] = sorted(partial, key=len)[0]
                break
    return resolved


def _number(raw) -> float | None:
    if raw in (None, "", "NA", "N/A"):
        return None
    try:
        return float(str(raw).replace(",", "").replace("$", "").strip())
    except ValueError:
        return None


def inspect(path: str | Path) -> list[str]:
    """Return the headers in a file, for building a --map override."""
    with open(path, newline="", encoding="utf-8-sig", errors="replace") as fh:
        return next(csv.reader(fh))


def load(path: str | Path, state: str = "MN",
         overrides: dict | None = None) -> tuple[list[Partner], dict]:
    """Parse an NCUA export into Partner records for one state.

    Returns (partners, diagnostics). Diagnostics names which columns matched
    and which did not, so a silently-wrong mapping shows up as a warning
    instead of a table full of zeroes.
    """
    with open(path, newline="", encoding="utf-8-sig", errors="replace") as fh:
        reader = csv.DictReader(fh)
        headers = reader.fieldnames or []
        cols = resolve_columns(headers, overrides)
        rows = list(reader)

    missing = [f for f in ("name", "total_assets") if f not in cols]
    if missing:
        raise SystemExit(
            f"Could not find required column(s) {missing} in {path}.\n"
            f"Headers present: {headers}\n"
            "Re-run with --inspect, then pass --map with a JSON override."
        )

    partners: list[Partner] = []
    for row in rows:
        row_state = (row.get(cols.get("state", ""), "") or "").strip().upper()
        if state and row_state and row_state != state.upper():
            continue

        name = (row.get(cols["name"], "") or "").strip()
        if not name:
            continue

        charter = (row.get(cols.get("charter", ""), "") or "").strip()
        low_income = (row.get(cols.get("low_income", ""), "") or "").strip().lower()
        count = _number(row.get(cols.get("business_loan_count", "")))

        partners.append(Partner(
            id=f"cu-{charter or _norm(name).replace(' ', '-')}",
            name=name,
            partner_type=PartnerType.CREDIT_UNION.value,
            city=(row.get(cols.get("city", ""), "") or "").strip(),
            state=row_state or state.upper(),
            total_assets=_number(row.get(cols.get("total_assets", ""))),
            net_worth=_number(row.get(cols.get("net_worth", ""))),
            business_loans_outstanding=_number(
                row.get(cols.get("business_loans_outstanding", ""))),
            business_loan_count=int(count) if count else None,
            low_income_designated=low_income in ("y", "yes", "1", "true"),
        ))

    diagnostics = {
        "file": str(path),
        "rows_read": len(rows),
        "matched_state": len(partners),
        "columns_matched": cols,
        "columns_unmatched": [f for f in DEFAULT_MAP if f not in cols],
    }
    return partners, diagnostics


def load_map(path: str | Path | None) -> dict | None:
    return json.loads(Path(path).read_text()) if path else None
