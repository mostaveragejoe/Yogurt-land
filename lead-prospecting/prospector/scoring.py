"""Scoring engine: rank referral partners by viability and quality.

Every partner is scored on three axes that add to 100:

    FIT      (0-40)  Does their reject/client flow map onto our product sheet?
    CAPACITY (0-35)  How many deals can they realistically send in a year?
    ACCESS   (0-25)  Can we actually get a conversation with a decision maker?

FIT and CAPACITY answer "quality". ACCESS answers "viability" -- a $12B credit
union has enormous capacity and is functionally unreachable, so it should not
outrank a $400M credit union whose CLO will take your call.

Each rubric returns points *and* a rationale string, because the reason a
partner scores well is the opening line of the LinkedIn message.
"""

from __future__ import annotations

from .models import Partner, PartnerType

# --- Statutory constants ------------------------------------------------
# Federal Credit Union Act, as amended by CUMAA (1998): a credit union's
# member business lending is capped at the LESSER of 1.75x net worth or
# 12.25% of total assets. Low-income-designated credit unions are exempt.
MBL_CAP_ASSET_RATIO = 0.1225
MBL_CAP_NET_WORTH_MULTIPLE = 1.75

# 2006 Interagency Guidance on CRE Concentration Risk Management (OCC/FRB/FDIC).
# Two supervisory criteria, measured against total risk-based capital:
#   - construction, land and land development >= 100% of capital
#   - total non-owner-occupied CRE >= 300% of capital, with 50%+ growth over 36 months
# These are supervisory triggers, NOT limits. A bank over them is not in
# violation -- it is under heightened examination scrutiny, which is precisely
# what makes it start declining CRE deals it would otherwise book.
CRE_CONCENTRATION_TRIGGER = 3.0
CONSTRUCTION_CONCENTRATION_TRIGGER = 1.0

# Legal lending limit: a national bank may lend one borrower up to 15% of
# capital and surplus unsecured. It is why a small bank declines a large deal
# on size alone, with nothing wrong with the credit.
LEGAL_LENDING_LIMIT_RATIO = 0.15

# Tier thresholds on the 0-100 total.
TIER_BREAKS = ((75, "A"), (60, "B"), (45, "C"), (0, "D"))

# A credible warm introduction is worth more than any amount of firmographics.
WARM_INTRO_BONUS = 10.0


def mbl_cap(total_assets: float | None, net_worth: float | None) -> float | None:
    """Statutory member-business-lending ceiling for a credit union."""
    if not total_assets:
        return None
    by_assets = total_assets * MBL_CAP_ASSET_RATIO
    if net_worth:
        return min(by_assets, net_worth * MBL_CAP_NET_WORTH_MULTIPLE)
    return by_assets


def cap_pressure(partner: Partner) -> float | None:
    """Fraction of the statutory MBL cap already consumed.

    This is the single most useful number in the whole tool. A credit union at
    0.90+ of its cap is declining business loans it *wants* to make, for
    regulatory reasons rather than credit reasons. Those borrowers are
    bank-quality paper turned away for a non-credit reason -- the best referral
    available anywhere in this market.
    """
    cap = mbl_cap(partner.total_assets, partner.net_worth)
    if not cap or partner.business_loans_outstanding is None:
        return None
    return partner.business_loans_outstanding / cap


def cre_concentration(partner: Partner) -> float | None:
    """Total CRE loans as a multiple of total risk-based capital."""
    if not partner.risk_based_capital or partner.cre_loans is None:
        return None
    return partner.cre_loans / partner.risk_based_capital


def construction_concentration(partner: Partner) -> float | None:
    if not partner.risk_based_capital or partner.construction_loans is None:
        return None
    return partner.construction_loans / partner.risk_based_capital


def legal_lending_limit(partner: Partner) -> float | None:
    """Roughly the largest single-borrower loan this bank may write."""
    if not partner.risk_based_capital:
        return None
    return partner.risk_based_capital * LEGAL_LENDING_LIMIT_RATIO


def _band(value, bands, default=0.0):
    """Return the points for the first band whose threshold value clears."""
    for threshold, points in bands:
        if value >= threshold:
            return points
    return default


# ---------------------------------------------------------------------------
# Credit unions and community banks
# ---------------------------------------------------------------------------

def _score_depository(partner: Partner) -> tuple[float, float, float, list[str]]:
    why: list[str] = []

    # --- FIT: driven by cap pressure -----------------------------------
    pressure = cap_pressure(partner)
    if partner.low_income_designated:
        # Exempt from the cap, so no regulatory forced declines. Still worth
        # contacting for out-of-policy declines, but the strongest angle is gone.
        fit = 14.0
        why.append("Low-income designated: exempt from the MBL cap, so no "
                   "regulatory forced declines. Pitch out-of-policy declines instead.")
    elif pressure is None:
        fit = 12.0
        why.append("No business-lending figures on file: cap pressure unknown.")
    elif partner.business_loans_outstanding == 0:
        fit = 10.0
        why.append("Runs no business lending at all. Different pitch: they have "
                   "commercial members and no product to serve them.")
    else:
        fit = _band(pressure, [(0.90, 40.0), (0.75, 32.0), (0.60, 24.0), (0.40, 14.0)], 8.0)
        why.append(f"At {pressure:.0%} of the statutory MBL cap.")
        if pressure >= 0.90:
            why.append("Near or over the cap -- must decline good paper for "
                       "regulatory reasons. Highest-value referral source there is.")
        elif pressure >= 0.75:
            why.append("Approaching the cap; already rationing business credit.")

    # --- CAPACITY: how much business flow do they actually see? --------
    if partner.business_loan_count:
        capacity = _band(partner.business_loan_count,
                         [(500, 35.0), (200, 30.0), (75, 24.0), (25, 16.0), (5, 9.0)], 4.0)
        why.append(f"{partner.business_loan_count:,} business loans outstanding.")
    elif partner.business_loans_outstanding:
        capacity = _band(partner.business_loans_outstanding,
                         [(100e6, 35.0), (40e6, 29.0), (15e6, 23.0),
                          (5e6, 16.0), (1e6, 9.0)], 4.0)
        why.append(f"${partner.business_loans_outstanding/1e6:,.1f}M business loan portfolio.")
    elif partner.total_assets:
        capacity = _band(partner.total_assets,
                         [(1e9, 26.0), (300e6, 20.0), (100e6, 13.0)], 7.0)
        why.append("Capacity estimated from asset size only.")
    else:
        capacity = 6.0

    # --- ACCESS: smaller institutions are reachable ---------------------
    # Deliberately inverted against size. Big shops have commercial lending
    # departments with a wall around them; at a $400M credit union you can get
    # the chief lending officer on the phone this week.
    assets = partner.total_assets or 0
    if assets == 0:
        access = 12.0
    elif assets < 250e6:
        access, note = 25.0, "Small enough that the CEO or CLO is directly reachable."
        why.append(note)
    elif assets < 1e9:
        access, note = 19.0, "Mid-size: lending leadership is reachable with one intro."
        why.append(note)
    elif assets < 3e9:
        access = 12.0
        why.append("Large: expect a commercial lending department and a vendor process.")
    else:
        access = 6.0
        why.append("Very large: gatekept, long procurement cycle, likely existing partners.")

    return fit, capacity, access, why


# ---------------------------------------------------------------------------
# CPA firms
# ---------------------------------------------------------------------------

# Specialties whose client base maps hard onto the product sheet.
HIGH_FIT_SPECIALTIES = {
    "construction": "equipment_leasing",
    "trucking": "equipment_leasing",
    "transportation": "equipment_leasing",
    "agriculture": "equipment_leasing",
    "manufacturing": "equipment_leasing",
    "medical": "medical_working_capital",
    "dental": "medical_working_capital",
    "healthcare": "medical_working_capital",
    "veterinary": "medical_working_capital",
    "real_estate": "real_estate",
    "restaurant": "unsecured_loans_loc",
    "hospitality": "unsecured_loans_loc",
    "staffing": "accounts_receivable",
    "wholesale": "accounts_receivable",
}


def _score_cpa(partner: Partner) -> tuple[float, float, float, list[str]]:
    why: list[str] = []

    # --- FIT ------------------------------------------------------------
    fit = 8.0  # baseline: any CPA sees financially stressed business clients
    matched = [s for s in partner.industry_specialties if s.lower() in HIGH_FIT_SPECIALTIES]
    if matched:
        fit += min(20.0, 7.0 * len(matched))
        why.append("Specializes in " + ", ".join(matched) +
                   " -- client base maps directly onto our product sheet.")
    if partner.has_advisory_practice:
        fit += 10.0
        why.append("Runs a CAS/advisory practice, so already in an advisory posture "
                   "rather than pure compliance work.")

    if partner.does_attest_work:
        # AICPA Rule 503 bars a referral fee outright for any client the firm
        # performs audit, review or compilation work for. No disclosure cures
        # it. This does not kill the relationship -- it changes the pitch to a
        # no-fee reciprocal arrangement -- but it does lower fit.
        fit -= 8.0
        why.append("ATTEST FLAG: does audit/review/compilation work. Referral fees "
                   "are barred for those clients under AICPA Rule 503 -- pitch a "
                   "no-fee reciprocal arrangement, never a commission.")
    fit = max(0.0, min(40.0, fit))

    # --- CAPACITY: headcount proxies business-client count --------------
    head = partner.headcount or 0
    if head >= 76:
        capacity = 28.0
        why.append("Large firm: real client volume, but expect an internal referral "
                   "policy and existing national-bank relationships.")
    elif head >= 31:
        capacity = 35.0
        why.append("Sweet spot: enough business clients to matter, small enough to "
                   "have no formal referral policy.")
    elif head >= 11:
        capacity = 30.0
    elif head >= 3:
        capacity = 22.0
    elif head >= 1:
        capacity = 10.0
        why.append("Solo practitioner: low volume, but a fast yes if the fit is right.")
    else:
        capacity = 12.0

    # --- ACCESS ---------------------------------------------------------
    if head and head >= 76:
        access = 10.0
        why.append("Partner-level contact is gatekept.")
    elif head and head >= 31:
        access = 17.0
    else:
        access = 23.0
        why.append("Small firm: the decision maker is the person who answers LinkedIn.")

    return fit, capacity, access, why


# ---------------------------------------------------------------------------
# Transaction intermediaries: business brokers, CRE brokers, equipment dealers
# ---------------------------------------------------------------------------

def _score_intermediary(partner: Partner) -> tuple[float, float, float, list[str]]:
    why: list[str] = []

    # --- FIT ------------------------------------------------------------
    # These partners are structurally motivated: no financing, no commission.
    # There is no client-relationship guarding to overcome, which is why they
    # are the fastest channel to a first funded deal.
    fit = 26.0
    why.append("Structurally motivated: their deal dies without financing, so "
               "there is no relationship-guarding to overcome.")
    if partner.partner_type == PartnerType.BUSINESS_BROKER.value:
        fit += 8.0
        why.append("Maps onto Business Acquisition Financing and SBA.")
    elif partner.partner_type == PartnerType.CRE_BROKER.value:
        fit += 8.0
        why.append("Maps onto Real Estate Financing, Commercial Bridge and Fix & Flip.")
    elif partner.partner_type == PartnerType.EQUIPMENT_DEALER.value:
        fit += 6.0
        why.append("Maps onto Equipment Leasing via a vendor-finance program.")
    fit = min(40.0, fit)

    # --- CAPACITY -------------------------------------------------------
    listings = partner.active_listings or 0
    capacity = _band(listings, [(40, 35.0), (20, 29.0), (10, 23.0), (4, 16.0), (1, 10.0)], 8.0)
    if listings:
        why.append(f"{listings} active listings.")
    if partner.years_active and partner.years_active >= 5:
        capacity = min(35.0, capacity + 3.0)
        why.append(f"{partner.years_active} years active: established deal flow.")

    # --- ACCESS ---------------------------------------------------------
    access = 22.0
    why.append("Brokers answer LinkedIn -- it is a prospecting channel for them too.")

    return fit, capacity, access, why


def _score_bank(partner: Partner) -> tuple[float, float, float, list[str]]:
    """Community banks. Deliberately not the credit-union rubric.

    Banks have no member-business-lending cap -- that is a credit-union
    statutory construct. What throttles a community bank's commercial
    appetite is CRE concentration against the 2006 interagency supervisory
    criteria, plus a legal lending limit that makes large single deals
    impossible regardless of credit quality.
    """
    why: list[str] = []

    # --- FIT: concentration pressure ------------------------------------
    cre = cre_concentration(partner)
    construction = construction_concentration(partner)

    if cre is None and construction is None:
        fit = 12.0
        why.append("No CRE or capital figures on file: concentration unknown.")
    else:
        fit = 8.0
        if cre is not None:
            fit = _band(cre, [(3.0, 40.0), (2.25, 32.0), (1.5, 24.0),
                              (0.75, 14.0)], 8.0)
            why.append(f"CRE at {cre:.0%} of total risk-based capital.")
            if cre >= CRE_CONCENTRATION_TRIGGER:
                why.append("Over the 300% interagency supervisory trigger -- "
                           "not a violation, but heightened examiner scrutiny, "
                           "which is exactly when a bank starts turning CRE away.")
            elif cre >= 2.25:
                why.append("Approaching the 300% trigger; already rationing CRE.")
        if construction is not None and construction >= CONSTRUCTION_CONCENTRATION_TRIGGER:
            fit = max(fit, 36.0)
            why.append(f"Construction and land development at "
                       f"{construction:.0%} of capital, over the 100% trigger.")

    limit = legal_lending_limit(partner)
    if limit:
        why.append(f"Legal lending limit roughly ${limit/1e6:,.1f}M to one "
                   "borrower -- anything larger is declined on size alone.")

    # --- CAPACITY --------------------------------------------------------
    if partner.cre_loans:
        capacity = _band(partner.cre_loans,
                         [(500e6, 35.0), (200e6, 30.0), (75e6, 24.0),
                          (25e6, 16.0), (5e6, 9.0)], 4.0)
    elif partner.total_assets:
        capacity = _band(partner.total_assets,
                         [(1e9, 30.0), (500e6, 24.0), (200e6, 18.0),
                          (75e6, 12.0)], 6.0)
        why.append("Capacity estimated from asset size.")
    else:
        capacity = 6.0

    # --- ACCESS: same inverse-size logic as credit unions ----------------
    assets = partner.total_assets or 0
    if assets == 0:
        access = 12.0
    elif assets < 500e6:
        access = 25.0
        why.append("Community-sized: the commercial lender or president is "
                   "directly reachable.")
    elif assets < 2e9:
        access = 18.0
    elif assets < 10e9:
        access = 10.0
        why.append("Regional: expect a commercial department and a vendor process.")
    else:
        access = 5.0
        why.append("Too large to be a referral partner in practice.")

    return fit, capacity, access, why


_RUBRICS = {
    PartnerType.CREDIT_UNION.value: _score_depository,
    PartnerType.COMMUNITY_BANK.value: _score_bank,
    PartnerType.CPA_FIRM.value: _score_cpa,
    PartnerType.BUSINESS_BROKER.value: _score_intermediary,
    PartnerType.CRE_BROKER.value: _score_intermediary,
    PartnerType.EQUIPMENT_DEALER.value: _score_intermediary,
}


def _score_generic(partner: Partner) -> tuple[float, float, float, list[str]]:
    return 15.0, 15.0, 15.0, ["No type-specific rubric; scored on defaults."]


def score_partner(partner: Partner) -> Partner:
    """Score one partner in place and return it."""
    if partner.do_not_contact:
        partner.fit_score = partner.capacity_score = partner.access_score = 0.0
        partner.total_score = 0.0
        partner.tier = "X"
        partner.score_rationale = ["Marked do-not-contact."]
        return partner

    rubric = _RUBRICS.get(partner.partner_type, _score_generic)
    fit, capacity, access, why = rubric(partner)

    total = fit + capacity + access
    if partner.warm_intro_path:
        total += WARM_INTRO_BONUS
        why.append(f"WARM PATH: {partner.warm_intro_path} (+{WARM_INTRO_BONUS:.0f}).")
    total = max(0.0, min(100.0, total))

    partner.fit_score = round(fit, 1)
    partner.capacity_score = round(capacity, 1)
    partner.access_score = round(access, 1)
    partner.total_score = round(total, 1)
    partner.tier = next(letter for cut, letter in TIER_BREAKS if total >= cut)
    partner.score_rationale = why

    # Record which products this partner's flow actually feeds, so the
    # outreach message can name them.
    if not partner.products_matched:
        partner.products_matched = infer_products(partner)
    return partner


def infer_products(partner: Partner) -> list[str]:
    """Best guess at which of our products this partner's flow maps onto."""
    t = partner.partner_type
    if t == PartnerType.CREDIT_UNION.value:
        # Exactly the products a depository cannot or will not do in-house.
        return ["sba_loans", "equipment_leasing", "commercial_bridge",
                "accounts_receivable", "fix_and_flip", "business_acquisition"]
    if t == PartnerType.COMMUNITY_BANK.value:
        # A concentration-constrained bank sheds CRE first, then anything
        # over its lending limit.
        return ["real_estate", "commercial_bridge", "fix_and_flip",
                "business_acquisition", "sba_loans", "accounts_receivable"]
    if t == PartnerType.CPA_FIRM.value:
        out = ["debt_restructuring", "business_term_loans", "unsecured_loans_loc", "sba_loans"]
        for spec in partner.industry_specialties:
            product = HIGH_FIT_SPECIALTIES.get(spec.lower())
            if product and product not in out:
                out.append(product)
        return out
    if t == PartnerType.BUSINESS_BROKER.value:
        return ["business_acquisition", "sba_loans", "commercial_bridge"]
    if t == PartnerType.CRE_BROKER.value:
        return ["real_estate", "commercial_bridge", "fix_and_flip"]
    if t == PartnerType.EQUIPMENT_DEALER.value:
        return ["equipment_leasing"]
    if t == PartnerType.MEDICAL_ADJACENT.value:
        return ["medical_working_capital", "accounts_receivable", "equipment_leasing"]
    return []


def score_all(partners: list[Partner]) -> list[Partner]:
    """Score a batch and return it sorted best-first."""
    scored = [score_partner(p) for p in partners]
    return sorted(scored, key=lambda p: p.total_score, reverse=True)
