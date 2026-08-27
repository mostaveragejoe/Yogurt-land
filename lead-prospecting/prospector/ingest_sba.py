"""SBA 7(a) and 504 loan data: who actually originates SBA, and who does not.

SBA publishes loan-level FOIA data for every 7(a) and 504 loan approved since
1990 at <https://data.sba.gov/dataset/7-a-504-foia>, updated quarterly.

Two things it does for lead sorting, both of which the tool currently guesses:

1. **Which institutions in your database do SBA at all.** A credit union or
   bank with commercial borrowers and zero SBA originations is the strongest
   SBA referral partner available -- the demand is on their books, the product
   is not. Right now the tool asserts "most credit unions are not PLP lenders"
   as a domain assumption. This measures it per institution.

2. **CDCs as leads.** Certified Development Companies do the 504 real-estate
   half and structurally need a partner for working capital and equipment.
   They appear by name in the 504 records.
"""

from __future__ import annotations

import csv
import json
import re
from collections import defaultdict
from pathlib import Path

from .models import ID_PREFIX, Partner, PartnerType

DEFAULT_MAP = {
    # 7(a) records name the originating bank directly.
    "lender": ["bankname", "bank name", "lender name", "lender"],
    "lender_city": ["bankcity", "bank city"],
    "lender_state": ["bankstate", "bank state"],
    # 504 records name the CDC and the third-party lender separately.
    "cdc": ["cdc_name", "cdc name", "cdcname"],
    "cdc_city": ["cdc_city", "cdc city"],
    "cdc_state": ["cdc_state", "cdc state"],
    "third_party": ["thirdpartylender_name", "third party lender name",
                    "thirdpartylender"],
    "borrower_state": ["borrstate", "borrower state", "projectstate",
                       "project state"],
    "borrower_city": ["borrcity", "borrower city", "projectcity"],
    "amount": ["grossapproval", "gross approval", "sbaguaranteedapproval",
               "approval amount"],
    "approval_date": ["approvaldate", "approval date"],
    "fiscal_year": ["approvalfiscalyear", "fiscal year"],
    "program": ["program", "loan type", "subprogram"],
    "naics": ["naicscode", "naics code", "naics"],
}


def _norm(text: str) -> str:
    return re.sub(r"[^a-z0-9 ]+", " ", (text or "").lower()).strip()


def resolve_columns(headers: list[str], overrides: dict | None = None) -> dict[str, str]:
    mapping = dict(DEFAULT_MAP)
    if overrides:
        for field, candidates in overrides.items():
            mapping[field] = candidates if isinstance(candidates, list) else [candidates]
    normalized = {h: _norm(h) for h in headers}
    resolved: dict[str, str] = {}
    claimed: set[str] = set()
    for field, candidates in mapping.items():
        for candidate in candidates:
            cand = _norm(candidate)
            exact = [h for h, n in normalized.items()
                     if n == cand and h not in claimed]
            if exact:
                resolved[field] = exact[0]
                claimed.add(exact[0])
                break
    for field, candidates in mapping.items():
        if field in resolved:
            continue
        for candidate in candidates:
            cand = _norm(candidate)
            partial = [h for h, n in normalized.items()
                       if cand in n and h not in claimed]
            if partial:
                chosen = sorted(partial, key=len)[0]
                resolved[field] = chosen
                claimed.add(chosen)
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
    with open(path, newline="", encoding="utf-8-sig", errors="replace") as fh:
        return next(csv.reader(fh))


def sample_values(path: str | Path, limit: int = 3) -> list[tuple[str, list[str]]]:
    with open(path, newline="", encoding="utf-8-sig", errors="replace") as fh:
        reader = csv.reader(fh)
        headers = next(reader)
        rows = [row for i, row in enumerate(reader) if i < limit]
    return [(h, [r[i] if i < len(r) else "" for r in rows])
            for i, h in enumerate(headers)]


def suggest_mapping(path: str | Path) -> dict:
    return {f: [h] for f, h in resolve_columns(inspect(path)).items()}


class LenderActivity:
    """One originator's SBA record in the target state."""

    def __init__(self, name: str):
        self.name = name
        self.count = 0
        self.volume = 0.0
        self.last_approval = ""
        self.city = ""
        self.state = ""
        self.by_year: dict[str, int] = defaultdict(int)

    def add(self, amount: float | None, approval_date: str, year: str) -> None:
        self.count += 1
        self.volume += amount or 0.0
        if approval_date and approval_date > self.last_approval:
            self.last_approval = approval_date
        if year:
            self.by_year[year] += 1

    def recent(self, years: int = 2) -> int:
        """Approvals in the most recent N fiscal years present in the data."""
        if not self.by_year:
            return 0
        newest = sorted(self.by_year)[-years:]
        return sum(self.by_year[y] for y in newest)


def load(path: str | Path, state: str = "MN",
         overrides: dict | None = None) -> tuple[dict, list[Partner], dict]:
    """Parse an SBA FOIA export.

    Returns (lenders_by_canonical_name, cdc_partners, diagnostics).
    """
    from .db import canonical_name

    with open(path, newline="", encoding="utf-8-sig", errors="replace") as fh:
        reader = csv.DictReader(fh)
        headers = reader.fieldnames or []
        cols = resolve_columns(headers, overrides)
        rows = list(reader)

    if "lender" not in cols and "cdc" not in cols:
        raise SystemExit(
            f"Found neither a lender nor a CDC column in {path}.\n"
            f"Headers present: {headers}\n"
            "Run --inspect, then pass --map with a JSON override.")

    def cell(row, field):
        header = cols.get(field)
        return (row.get(header) or "").strip() if header else ""

    lenders: dict[str, LenderActivity] = {}
    cdcs: dict[str, Partner] = {}
    matched_rows = 0

    for row in rows:
        borrower_state = cell(row, "borrower_state").upper()
        if state and borrower_state and borrower_state != state.upper():
            continue
        matched_rows += 1

        amount = _number(cell(row, "amount"))
        approval = cell(row, "approval_date")
        year = cell(row, "fiscal_year")

        for field in ("lender", "third_party"):
            name = cell(row, field)
            if not name:
                continue
            key = canonical_name(name)
            if key not in lenders:
                lenders[key] = LenderActivity(name)
                lenders[key].city = cell(row, "lender_city")
                lenders[key].state = cell(row, "lender_state")
            lenders[key].add(amount, approval, year)

        cdc_name = cell(row, "cdc")
        if cdc_name:
            key = canonical_name(cdc_name)
            if key not in cdcs:
                cdcs[key] = Partner(
                    id=f"{ID_PREFIX[PartnerType.CDC.value]}-"
                       f"{re.sub(r'[^a-z0-9]+', '-', cdc_name.lower()).strip('-')}",
                    name=cdc_name,
                    partner_type=PartnerType.CDC.value,
                    city=cell(row, "cdc_city"),
                    state=cell(row, "cdc_state") or state.upper(),
                )
            # A CDC's own activity is tracked like any other originator.
            if key not in lenders:
                lenders[key] = LenderActivity(cdc_name)
            lenders[key].add(amount, approval, year)

    diagnostics = {
        "file": str(path),
        "rows_read": len(rows),
        "rows_in_state": matched_rows,
        "lenders": len(lenders),
        "cdcs": len(cdcs),
        "columns_matched": cols,
        "columns_unmatched": [f for f in DEFAULT_MAP if f not in cols],
    }
    return lenders, list(cdcs.values()), diagnostics


def load_map(path: str | Path | None) -> dict | None:
    return json.loads(Path(path).read_text()) if path else None
