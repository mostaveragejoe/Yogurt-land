"""Core data model for referral-partner prospecting.

A "partner" here is NOT a borrower. It is an institution or professional who
sees borrowers we can serve and has a reason to hand them to us -- a credit
union that must decline good paper, a CPA whose client just got turned down,
a business broker whose deal dies without financing.
"""

from __future__ import annotations

from dataclasses import dataclass, field, asdict, fields
from enum import Enum
from typing import Optional


class PartnerType(str, Enum):
    """Referral-partner archetypes, each scored by its own rubric."""

    CREDIT_UNION = "credit_union"
    COMMUNITY_BANK = "community_bank"
    CPA_FIRM = "cpa_firm"
    BUSINESS_BROKER = "business_broker"
    CRE_BROKER = "cre_broker"
    EQUIPMENT_DEALER = "equipment_dealer"
    MEDICAL_ADJACENT = "medical_adjacent"
    ATTORNEY = "attorney"
    OTHER = "other"


class Stage(str, Enum):
    """Where a partner sits in the relationship pipeline."""

    NOT_CONTACTED = "not_contacted"
    RESEARCHING = "researching"
    CONTACTED = "contacted"
    RESPONDED = "responded"
    MEETING_SET = "meeting_set"
    AGREEMENT = "agreement"
    PRODUCING = "producing"
    DORMANT = "dormant"
    DEAD = "dead"


# Short, explicit id prefixes per partner type.
#
# Never derive these by truncating the type name: credit_union[:3] and
# cre_broker[:3] are both "cre", which silently overwrites one type's records
# with the other's on import.
ID_PREFIX = {
    PartnerType.CREDIT_UNION.value: "cu",
    PartnerType.COMMUNITY_BANK.value: "bank",
    PartnerType.CPA_FIRM.value: "cpa",
    PartnerType.BUSINESS_BROKER.value: "bb",
    PartnerType.CRE_BROKER.value: "creb",
    PartnerType.EQUIPMENT_DEALER.value: "eq",
    PartnerType.MEDICAL_ADJACENT.value: "med",
    PartnerType.ATTORNEY.value: "atty",
    PartnerType.OTHER.value: "other",
}


# Products on the Elite Business Financing sheet. Used to record which of our
# products a given partner's decline flow actually maps onto -- this is what
# goes in the LinkedIn message, so it is worth storing explicitly.
PRODUCTS = (
    "debt_restructuring",
    "business_term_loans",
    "sba_loans",
    "medical_working_capital",
    "unsecured_loans_loc",
    "accounts_receivable",
    "commercial_bridge",
    "business_acquisition",
    "equipment_leasing",
    "real_estate",
    "fix_and_flip",
)


@dataclass
class Partner:
    """One prospective referral partner."""

    # --- identity -------------------------------------------------------
    id: str
    name: str
    partner_type: str = PartnerType.OTHER.value
    city: str = ""
    state: str = "MN"

    # --- contact --------------------------------------------------------
    website: str = ""
    phone: str = ""
    linkedin_url: str = ""
    contact_name: str = ""
    contact_title: str = ""

    # --- credit-union / bank metrics (from NCUA or FFIEC call reports) ---
    total_assets: Optional[float] = None
    net_worth: Optional[float] = None
    business_loans_outstanding: Optional[float] = None
    business_loan_count: Optional[int] = None
    low_income_designated: bool = False

    # --- professional-firm metrics (entered by hand or scraped) ---------
    headcount: Optional[int] = None
    does_attest_work: Optional[bool] = None
    has_advisory_practice: bool = False
    industry_specialties: list[str] = field(default_factory=list)
    active_listings: Optional[int] = None
    years_active: Optional[int] = None

    # --- relationship ---------------------------------------------------
    warm_intro_path: str = ""          # mutual connection, alumni, past client
    products_matched: list[str] = field(default_factory=list)
    stage: str = Stage.NOT_CONTACTED.value
    owner: str = ""
    last_touch: str = ""               # ISO date
    next_action: str = ""
    next_action_due: str = ""          # ISO date
    notes: str = ""
    do_not_contact: bool = False

    # --- computed (populated by scoring.score_partner) -------------------
    fit_score: float = 0.0
    capacity_score: float = 0.0
    access_score: float = 0.0
    total_score: float = 0.0
    tier: str = ""
    score_rationale: list[str] = field(default_factory=list)

    def to_row(self) -> dict:
        """Flatten to a dict of SQLite-safe primitives."""
        row = asdict(self)
        for key in ("industry_specialties", "products_matched", "score_rationale"):
            row[key] = "|".join(row[key] or [])
        for key in ("low_income_designated", "has_advisory_practice", "do_not_contact"):
            row[key] = int(bool(row[key]))
        if row["does_attest_work"] is not None:
            row["does_attest_work"] = int(bool(row["does_attest_work"]))
        return row

    @classmethod
    def from_row(cls, row) -> "Partner":
        """Rebuild from a SQLite row (sqlite3.Row or dict)."""
        data = dict(row)
        for key in ("industry_specialties", "products_matched", "score_rationale"):
            raw = data.get(key) or ""
            data[key] = [part for part in raw.split("|") if part]
        for key in ("low_income_designated", "has_advisory_practice", "do_not_contact"):
            data[key] = bool(data.get(key))
        if data.get("does_attest_work") is not None:
            data["does_attest_work"] = bool(data["does_attest_work"])
        # SQLite stores these as REAL; restore them as ints so they format
        # as counts ("214 loans") rather than floats ("214.0 loans").
        for key in ("business_loan_count", "headcount", "active_listings",
                    "years_active"):
            if data.get(key) is not None:
                data[key] = int(float(data[key]))
        known = {f.name for f in fields(cls)}
        return cls(**{k: v for k, v in data.items() if k in known})


# Column order used for SQLite DDL and CSV export alike.
COLUMNS = [f.name for f in fields(Partner)]
