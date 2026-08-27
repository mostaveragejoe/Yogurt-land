"""Turn FDIC institution data into community-bank referral prospects.

FDIC publishes institution and financial data at
<https://banks.data.fdic.gov/> (bulk download and API).

Banks are scored on a different rubric from credit unions and need different
columns: there is no member-business-lending cap, so what matters is CRE
concentration against total risk-based capital. See scoring._score_bank.

Same defensive posture as the NCUA importer, for the same reason: a
mis-mapped or mis-scaled column here does not raise, it produces a plausible
table with a quietly wrong ranking.
"""

from __future__ import annotations

import csv
import json
import re
from pathlib import Path

from .models import Partner, PartnerType

DEFAULT_MAP = {
    "name": ["institution name", "instname", "name", "bank name"],
    "city": ["city"],
    "state": ["stalp", "state abbreviation", "state", "stname"],
    "cert": ["cert", "fdic certificate", "certificate", "id"],
    "total_assets": ["total assets", "asset", "assets"],
    "risk_based_capital": [
        "total risk based capital", "rbct", "risk based capital",
        "total capital", "rbc",
    ],
    "cre_loans": [
        "lnrenres", "commercial real estate", "nonfarm nonresidential",
        "cre loans", "total commercial real estate",
    ],
    "construction_loans": [
        "lnrecons", "construction and land development", "construction",
        "land development",
    ],
}

# Community banks below this are rare; a whole file under it is in thousands.
IMPLAUSIBLE_MEDIAN_ASSETS = 5e6

UNIT_FACTORS = {"dollars": 1.0, "thousands": 1_000.0, "millions": 1_000_000.0}


def _norm(text: str) -> str:
    return re.sub(r"[^a-z0-9 ]+", " ", (text or "").lower()).strip()


def resolve_columns(headers: list[str], overrides: dict | None = None) -> dict[str, str]:
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


def load(path: str | Path, state: str = "MN",
         overrides: dict | None = None) -> tuple[list[Partner], dict]:
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
            "Re-run with --inspect, then pass --map with a JSON override.")

    partners: list[Partner] = []
    for row in rows:
        row_state = (row.get(cols.get("state", ""), "") or "").strip().upper()
        if state and row_state and row_state != state.upper():
            continue
        name = (row.get(cols["name"], "") or "").strip()
        if not name:
            continue
        cert = (row.get(cols.get("cert", ""), "") or "").strip()
        partners.append(Partner(
            id=f"bank-{cert or _norm(name).replace(' ', '-')}",
            name=name,
            partner_type=PartnerType.COMMUNITY_BANK.value,
            city=(row.get(cols.get("city", ""), "") or "").strip(),
            state=row_state or state.upper(),
            total_assets=_number(row.get(cols.get("total_assets", ""))),
            risk_based_capital=_number(row.get(cols.get("risk_based_capital", ""))),
            cre_loans=_number(row.get(cols.get("cre_loans", ""))),
            construction_loans=_number(row.get(cols.get("construction_loans", ""))),
        ))

    diagnostics = {
        "file": str(path),
        "rows_read": len(rows),
        "matched_state": len(partners),
        "columns_matched": cols,
        "columns_unmatched": [f for f in DEFAULT_MAP if f not in cols],
    }
    return partners, diagnostics


def _median(values: list[float]) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    mid = len(ordered) // 2
    return ordered[mid] if len(ordered) % 2 else (ordered[mid-1] + ordered[mid]) / 2


def validate(partners: list[Partner]) -> list[dict]:
    findings: list[dict] = []
    if not partners:
        return [{"level": "error", "code": "empty",
                 "message": "No rows matched. Check the --state filter and the file."}]

    assets = [p.total_assets for p in partners if p.total_assets]
    median_assets = _median(assets)
    if median_assets is not None and median_assets < IMPLAUSIBLE_MEDIAN_ASSETS:
        findings.append({
            "level": "error", "code": "scale",
            "message": (f"Median assets are ${median_assets:,.0f}, far below any "
                        "real bank. The file is almost certainly reported in "
                        "THOUSANDS.\n        CRE concentration is a ratio and "
                        "would still look right, but access scoring would treat "
                        "every\n        bank as tiny. Re-run with --units thousands."),
        })

    cap_over = [p.name for p in partners
                if p.total_assets and p.risk_based_capital
                and p.risk_based_capital > p.total_assets]
    if cap_over:
        findings.append({
            "level": "error", "code": "capital_exceeds_assets",
            "message": (f"{len(cap_over)} bank(s) report risk-based capital above "
                        f"total assets, which is impossible. Check the column "
                        f"mapping. First: {cap_over[0]}"),
        })

    cre_over = [p.name for p in partners
                if p.total_assets and p.cre_loans
                and p.cre_loans > p.total_assets]
    if cre_over:
        findings.append({
            "level": "error", "code": "cre_exceeds_assets",
            "message": (f"{len(cre_over)} bank(s) report CRE loans above total "
                        f"assets. Check the CRE column. First: {cre_over[0]}"),
        })

    negative = [p.name for p in partners
                if (p.total_assets or 0) < 0 or (p.risk_based_capital or 0) < 0
                or (p.cre_loans or 0) < 0]
    if negative:
        findings.append({"level": "error", "code": "negative",
                         "message": f"{len(negative)} bank(s) report negative "
                                    f"figures. First: {negative[0]}"})

    missing_capital = sum(1 for p in partners if not p.risk_based_capital)
    if missing_capital > len(partners) * 0.5:
        findings.append({
            "level": "error", "code": "missing_capital",
            "message": (f"{missing_capital} of {len(partners)} rows have no "
                        "risk-based capital. CRE concentration is the whole "
                        "point of this import and cannot be computed without it."),
        })

    missing_cre = sum(1 for p in partners if p.cre_loans is None)
    if missing_cre > len(partners) * 0.5:
        findings.append({
            "level": "error", "code": "missing_cre",
            "message": (f"{missing_cre} of {len(partners)} rows have no CRE loan "
                        "figure -- concentration cannot be computed."),
        })
    return findings


def rescale(partners: list[Partner], factor: float) -> None:
    for p in partners:
        for field in ("total_assets", "risk_based_capital", "cre_loans",
                      "construction_loans"):
            value = getattr(p, field)
            if value is not None:
                setattr(p, field, value * factor)


def load_map(path: str | Path | None) -> dict | None:
    return json.loads(Path(path).read_text()) if path else None
