"""Outcome analysis: which channels and partners actually produce.

Two design commitments here, both deliberate:

1. **Rates are always shown with a confidence interval.** "3/8 = 38%" invites
   a decision. "38% (CI 14-69%)" makes the uncertainty impossible to miss.
   At the sample sizes this tool will see for its first year, the interval is
   the most honest thing on the page.

2. **Verdicts are gated; data is not.** The tool will always show you what it
   has. It will refuse to declare one channel better than another until there
   is enough evidence to support the claim, and it says how much more it
   needs. A report that says "too thin to compare" is worth more than three
   confident percentages you act on and regret.

What this module deliberately does NOT do is auto-tune the scoring weights in
scoring.py. Fitting weights to twenty data points reproduces noise and would
degrade the rankings while looking sophisticated. Outcomes are reported; the
human adjusts the constants.
"""

from __future__ import annotations

import datetime as dt
import math
from collections import defaultdict

from .models import Stage

# --- Evidence thresholds ------------------------------------------------
# Below these, the tool reports counts but withholds comparative verdicts.
MIN_WORKED_FOR_RATE = 8          # partners worked before a rate is meaningful
MIN_DEALS_FOR_VERDICT = 10       # deals before channels can be ranked
MIN_PER_TIER = 6                 # worked partners per tier for calibration
MIN_DEALS_FOR_CALIBRATION = 8

Z_95 = 1.959963984540054

# A partner counts as "worked" once outreach actually happened. Partners
# sitting untouched in the list must not dilute a conversion denominator.
WORKED_STAGES = {
    Stage.CONTACTED.value, Stage.RESPONDED.value, Stage.MEETING_SET.value,
    Stage.AGREEMENT.value, Stage.PRODUCING.value, Stage.DORMANT.value,
    Stage.DEAD.value,
}

AGREED_STAGES = {
    Stage.AGREEMENT.value, Stage.PRODUCING.value,
}


def wilson(successes: int, trials: int, z: float = Z_95) -> tuple[float, float, float]:
    """Wilson score interval for a proportion.

    Chosen over the normal approximation because it behaves sanely at small n
    and at proportions near 0 or 1 -- exactly the regime this tool lives in.
    Returns (point_estimate, low, high), all as fractions of 1.
    """
    if trials <= 0:
        return 0.0, 0.0, 0.0
    p = successes / trials
    denom = 1 + z * z / trials
    center = (p + z * z / (2 * trials)) / denom
    margin = (z / denom) * math.sqrt(p * (1 - p) / trials
                                     + z * z / (4 * trials * trials))
    return p, max(0.0, center - margin), min(1.0, center + margin)


def _median(values: list[float]) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    mid = len(ordered) // 2
    if len(ordered) % 2:
        return ordered[mid]
    return (ordered[mid - 1] + ordered[mid]) / 2


def days_between(start: str | None, end: str | None) -> int | None:
    if not start or not end:
        return None
    try:
        return (dt.date.fromisoformat(end) - dt.date.fromisoformat(start)).days
    except ValueError:
        return None


class ChannelStats:
    """Aggregate outcomes for one partner type."""

    def __init__(self, channel: str):
        self.channel = channel
        self.total = 0
        self.worked = 0
        self.agreed = 0
        self.producing = 0          # partners that sent at least one deal
        self.deals = 0
        self.funded = 0
        self.funded_amount = 0.0
        self.revenue = 0.0
        self.days_to_first_deal: list[float] = []

    # -- derived ---------------------------------------------------------
    @property
    def agreement_rate(self):
        return wilson(self.agreed, self.worked)

    @property
    def production_rate(self):
        """Share of worked partners that ever sent a deal. The real number."""
        return wilson(self.producing, self.worked)

    @property
    def funded_rate(self):
        return wilson(self.funded, self.deals)

    @property
    def median_days_to_deal(self):
        return _median(self.days_to_first_deal)

    @property
    def revenue_per_worked(self) -> float:
        return self.revenue / self.worked if self.worked else 0.0

    @property
    def has_enough_for_rate(self) -> bool:
        return self.worked >= MIN_WORKED_FOR_RATE


def channel_stats(conn, partners, deals_by_partner, first_contacts) -> dict:
    """Aggregate every partner and deal into per-channel statistics."""
    stats: dict[str, ChannelStats] = {}

    for p in partners:
        st = stats.setdefault(p.partner_type, ChannelStats(p.partner_type))
        st.total += 1
        if p.stage not in WORKED_STAGES:
            continue
        st.worked += 1
        if p.stage in AGREED_STAGES:
            st.agreed += 1

        partner_deals = deals_by_partner.get(p.id, [])
        if not partner_deals:
            continue

        st.producing += 1
        st.deals += len(partner_deals)
        for d in partner_deals:
            if d["status"] == "funded":
                st.funded += 1
                st.funded_amount += d["amount"] or 0.0
                st.revenue += d["revenue"] or 0.0

        gap = days_between(first_contacts.get(p.id),
                           min(d["referred_date"] for d in partner_deals))
        if gap is not None and gap >= 0:
            st.days_to_first_deal.append(gap)

    return stats


def can_rank_channels(stats: dict) -> tuple[bool, str]:
    """Whether there is enough evidence to declare one channel better."""
    total_deals = sum(s.deals for s in stats.values())
    comparable = [s for s in stats.values() if s.has_enough_for_rate]

    if total_deals < MIN_DEALS_FOR_VERDICT:
        return False, (
            f"{total_deals} deal(s) recorded. Need about {MIN_DEALS_FOR_VERDICT} "
            "before one channel can be called better than another."
        )
    if len(comparable) < 2:
        return False, (
            f"Only {len(comparable)} channel(s) have {MIN_WORKED_FOR_RATE}+ "
            "worked partners. Need two to compare."
        )
    return True, ""


def rank_channels(stats: dict) -> list[ChannelStats]:
    """Channels ordered by production rate, then revenue per partner worked."""
    return sorted(
        [s for s in stats.values() if s.has_enough_for_rate],
        key=lambda s: (s.production_rate[0], s.revenue_per_worked),
        reverse=True,
    )


class ProducerRow:
    """One partner's contribution."""

    def __init__(self, partner):
        self.partner = partner
        self.deals = 0
        self.funded = 0
        self.funded_amount = 0.0
        self.revenue = 0.0
        self.last_deal_date = ""
        self.days_to_first_deal = None


def producer_stats(partners, deals_by_partner, first_contacts) -> list[ProducerRow]:
    """Per-partner outcomes for every partner that agreed or produced.

    Includes zero-deal partners that reached an agreement -- those are the
    maintenance cost you are deciding whether to keep paying.
    """
    rows = []
    for p in partners:
        partner_deals = deals_by_partner.get(p.id, [])
        if not partner_deals and p.stage not in AGREED_STAGES:
            continue
        row = ProducerRow(p)
        row.deals = len(partner_deals)
        for d in partner_deals:
            if d["status"] == "funded":
                row.funded += 1
                row.funded_amount += d["amount"] or 0.0
                row.revenue += d["revenue"] or 0.0
        if partner_deals:
            row.last_deal_date = max(d["referred_date"] for d in partner_deals)
            row.days_to_first_deal = days_between(
                first_contacts.get(p.id),
                min(d["referred_date"] for d in partner_deals))
        rows.append(row)

    rows.sort(key=lambda r: (r.revenue, r.funded_amount, r.deals), reverse=True)
    return rows


class TierRow:
    def __init__(self, tier: str):
        self.tier = tier
        self.worked = 0
        self.producing = 0
        self.deals = 0
        self.revenue = 0.0

    @property
    def production_rate(self):
        return wilson(self.producing, self.worked)


def tier_calibration(partners, deals_by_partner) -> dict:
    """Did the A/B/C/D tiers predict anything?

    This is the slowest of the reports to become answerable and the least
    valuable when it does. It exists to catch the case where the scoring model
    is decoration -- where A-tier and C-tier convert identically and the
    cap-pressure theory is simply wrong.
    """
    rows: dict[str, TierRow] = {}
    for p in partners:
        if p.stage not in WORKED_STAGES or not p.tier:
            continue
        row = rows.setdefault(p.tier, TierRow(p.tier))
        row.worked += 1
        partner_deals = deals_by_partner.get(p.id, [])
        if partner_deals:
            row.producing += 1
            row.deals += len(partner_deals)
            row.revenue += sum(d["revenue"] or 0.0 for d in partner_deals
                               if d["status"] == "funded")
    return rows


def can_calibrate(rows: dict) -> tuple[bool, str]:
    eligible = [r for r in rows.values() if r.worked >= MIN_PER_TIER]
    total_deals = sum(r.deals for r in rows.values())
    if len(eligible) < 2:
        worked = sum(r.worked for r in rows.values())
        return False, (
            f"{worked} worked partner(s) across {len(rows)} tier(s). Need "
            f"{MIN_PER_TIER}+ in at least two tiers before the tiers can be "
            "checked against outcomes."
        )
    if total_deals < MIN_DEALS_FOR_CALIBRATION:
        return False, (
            f"{total_deals} deal(s) recorded. Need about "
            f"{MIN_DEALS_FOR_CALIBRATION} before tier performance means anything."
        )
    return True, ""


def calibration_verdict(rows: dict) -> str:
    """Plain reading of whether the tiers separated."""
    eligible = sorted([r for r in rows.values() if r.worked >= MIN_PER_TIER],
                      key=lambda r: r.tier)
    if len(eligible) < 2:
        return ""
    best = max(eligible, key=lambda r: r.production_rate[0])
    worst = min(eligible, key=lambda r: r.production_rate[0])

    # Overlapping intervals mean the difference is not established.
    _, best_low, _ = best.production_rate
    _, _, worst_high = worst.production_rate
    if best_low > worst_high:
        return (f"Tiers separated: {best.tier} converts better than "
                f"{worst.tier}, and the intervals do not overlap. The scoring "
                "model is carrying real signal.")
    return ("Tiers have NOT separated -- the confidence intervals overlap, so "
            "any apparent difference could be noise. Either the model is not "
            "predictive or there is not enough data yet. Do not re-weight on "
            "this.")
