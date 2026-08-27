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

# ---------------------------------------------------------------------------
# Validation
# ---------------------------------------------------------------------------
# This import is the tool's only route to real credit-union data and the least
# forgiving place to be wrong, because the failure modes are silent. A
# mis-mapped or mis-scaled column does not raise -- it produces a plausible
# table with a quietly corrupted ranking.

# Federally insured credit unions below this are vanishingly rare; a whole
# file under it means the figures are in thousands, not dollars.
IMPLAUSIBLE_MEDIAN_ASSETS = 5e6


def _median(values: list[float]) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    mid = len(ordered) // 2
    return ordered[mid] if len(ordered) % 2 else (ordered[mid-1] + ordered[mid]) / 2


def validate(partners: list[Partner]) -> list[dict]:
    """Check parsed rows for the mistakes that do not announce themselves.

    Returns a list of {level, code, message} where level is "error" (the
    import is wrong, do not trust it) or "warning" (worth a look).
    """
    findings: list[dict] = []
    if not partners:
        return [{"level": "error", "code": "empty",
                 "message": "No rows matched. Check the --state filter and the file."}]

    assets = [p.total_assets for p in partners if p.total_assets]

    # --- scale ----------------------------------------------------------
    # Cap pressure is a ratio and survives a units error, so nothing looks
    # broken. Access scoring does not: every credit union reads as tiny,
    # scores maximum reachability, and the ranking silently shifts.
    median_assets = _median(assets)
    if median_assets is not None and median_assets < IMPLAUSIBLE_MEDIAN_ASSETS:
        findings.append({
            "level": "error", "code": "scale",
            "message": (
                f"Median assets are ${median_assets:,.0f}, far below any real "
                "credit union. The file is almost certainly reported in "
                "THOUSANDS.\n"
                "        Cap pressure would still look right -- it is a ratio -- "
                "but access\n"
                "        scoring would treat every institution as tiny and the "
                "ranking would be wrong.\n"
                "        Re-run with --units thousands."),
        })

    # --- impossible relationships ---------------------------------------
    nw_over = [p.name for p in partners
               if p.total_assets and p.net_worth and p.net_worth > p.total_assets]
    if nw_over:
        findings.append({
            "level": "error", "code": "net_worth_exceeds_assets",
            "message": (f"{len(nw_over)} institution(s) report net worth above "
                        f"total assets, which is impossible -- the columns are "
                        f"probably mapped to the wrong fields. "
                        f"First: {nw_over[0]}"),
        })

    mbl_over = [p.name for p in partners
                if p.total_assets and p.business_loans_outstanding
                and p.business_loans_outstanding > p.total_assets]
    if mbl_over:
        findings.append({
            "level": "error", "code": "loans_exceed_assets",
            "message": (f"{len(mbl_over)} institution(s) report business loans "
                        f"above total assets. Check the business-loan column "
                        f"mapping. First: {mbl_over[0]}"),
        })

    negative = [p.name for p in partners
                if (p.total_assets or 0) < 0 or (p.net_worth or 0) < 0
                or (p.business_loans_outstanding or 0) < 0]
    if negative:
        findings.append({
            "level": "error", "code": "negative",
            "message": f"{len(negative)} institution(s) have negative figures. "
                       f"First: {negative[0]}",
        })

    # --- coverage --------------------------------------------------------
    missing_assets = sum(1 for p in partners if not p.total_assets)
    if missing_assets:
        share = missing_assets / len(partners)
        findings.append({
            "level": "error" if share > 0.5 else "warning",
            "code": "missing_assets",
            "message": (f"{missing_assets} of {len(partners)} rows have no total "
                        "assets. Those cannot be scored for reachability."),
        })

    missing_nw = sum(1 for p in partners if not p.net_worth)
    if missing_nw > len(partners) * 0.5:
        findings.append({
            "level": "warning", "code": "missing_net_worth",
            "message": (f"{missing_nw} of {len(partners)} rows have no net worth. "
                        "The MBL cap falls back to 12.25% of assets, which "
                        "overstates the cap for thinly capitalized institutions."),
        })

    missing_mbl = sum(1 for p in partners if p.business_loans_outstanding is None)
    if missing_mbl > len(partners) * 0.5:
        findings.append({
            "level": "error", "code": "missing_business_loans",
            "message": (f"{missing_mbl} of {len(partners)} rows have no business-loan "
                        "figure. Cap pressure is the whole point of this import "
                        "and cannot be computed without it."),
        })

    return findings


def rescale(partners: list[Partner], factor: float) -> None:
    """Multiply every monetary field in place -- for a --units correction."""
    for p in partners:
        for field in ("total_assets", "net_worth", "business_loans_outstanding"):
            value = getattr(p, field)
            if value is not None:
                setattr(p, field, value * factor)


UNIT_FACTORS = {"dollars": 1.0, "thousands": 1_000.0, "millions": 1_000_000.0}


def sample_values(path: str | Path, limit: int = 3) -> list[tuple[str, list[str]]]:
    """Headers paired with their first few values.

    A bare header list is not enough to build a mapping from -- NCUA account
    codes like ACCT_010 are meaningless without seeing what is under them.
    """
    with open(path, newline="", encoding="utf-8-sig", errors="replace") as fh:
        reader = csv.reader(fh)
        headers = next(reader)
        rows = []
        for i, row in enumerate(reader):
            if i >= limit:
                break
            rows.append(row)
    out = []
    for idx, header in enumerate(headers):
        out.append((header, [r[idx] if idx < len(r) else "" for r in rows]))
    return out


def suggest_mapping(path: str | Path) -> dict:
    """The mapping the parser would use, as a paste-ready override."""
    headers = inspect(path)
    resolved = resolve_columns(headers)
    return {field: [header] for field, header in resolved.items()}
