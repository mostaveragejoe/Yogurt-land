"""Ranked output: the "who do I message today" list."""

from __future__ import annotations

import csv
from pathlib import Path

from .models import Partner, PartnerType, Stage
from .scoring import cap_pressure, mbl_cap

# The opening angle for each partner type. The score tells you who to contact;
# this tells you what to say. Keep these short enough to fit a LinkedIn
# connection note (300 characters).
ANGLES = {
    PartnerType.CREDIT_UNION.value: (
        "Lead with their cap. You are not asking for their members -- you are "
        "offering somewhere to send the business loans they legally cannot book. "
        "Name the products they don't do in-house: SBA, equipment leasing, "
        "bridge, A/R, fix & flip."
    ),
    PartnerType.COMMUNITY_BANK.value: (
        "Same as credit unions but the constraint is policy and concentration "
        "limits rather than a statutory cap. Ask what they decline most often."
    ),
    PartnerType.CPA_FIRM.value: (
        "Never open with a referral fee -- AICPA Rule 503 bars it outright for "
        "any client they do attest or compilation work for. Open with the client "
        "they had to tell no. Offer reciprocal referrals, not commission."
    ),
    PartnerType.BUSINESS_BROKER.value: (
        "Pure self-interest: their deal dies without financing. Lead with speed "
        "to term sheet on business acquisition and SBA. Fastest channel to a "
        "first funded deal -- use these for proof points."
    ),
    PartnerType.CRE_BROKER.value: (
        "Lead with bridge and fix & flip speed. Ask what their last deal died on."
    ),
    PartnerType.EQUIPMENT_DEALER.value: (
        "Pitch a vendor-finance program, not a referral relationship. They want "
        "the sale closed; financing is the obstacle."
    ),
    PartnerType.MEDICAL_ADJACENT.value: (
        "Medical Working Capital and A/R financing. Billing companies and "
        "practice consultants see the cash-flow gap before anyone else."
    ),
    PartnerType.ATTORNEY.value: (
        "Bankruptcy and turnaround counsel map onto Debt Restructuring."
    ),
}

_MONEY = lambda v: f"${v/1e6:,.1f}M" if v else "--"


def _fmt_pressure(partner: Partner) -> str:
    pressure = cap_pressure(partner)
    return f"{pressure:.0%}" if pressure is not None else "--"


def ranked_table(partners: list[Partner], limit: int = 25) -> str:
    """Compact leaderboard."""
    rows = sorted(partners, key=lambda p: p.total_score, reverse=True)[:limit]
    if not rows:
        return "No partners match that filter."

    out = [
        f"{'#':>3}  {'T':<2} {'SCORE':>5}  {'NAME':<38} {'CITY':<16} "
        f"{'TYPE':<16} {'CAP%':>5} {'ASSETS':>10}  STAGE",
        "-" * 132,
    ]
    for i, p in enumerate(rows, 1):
        out.append(
            f"{i:>3}  {p.tier:<2} {p.total_score:>5.1f}  {p.name[:38]:<38} "
            f"{p.city[:16]:<16} {p.partner_type[:16]:<16} "
            f"{_fmt_pressure(p):>5} {_MONEY(p.total_assets):>10}  {p.stage}"
        )
    return "\n".join(out)


def detail(partner: Partner) -> str:
    """Full dossier for one partner -- what you read before you message them."""
    lines = [
        "=" * 74,
        f"{partner.name}   [{partner.tier}]  {partner.total_score:.1f}/100",
        "=" * 74,
        f"Type      : {partner.partner_type}",
        f"Location  : {partner.city}, {partner.state}",
    ]
    if partner.contact_name:
        lines.append(f"Contact   : {partner.contact_name} -- {partner.contact_title}")
    for label, value in (("Website", partner.website), ("Phone", partner.phone),
                         ("LinkedIn", partner.linkedin_url)):
        if value:
            lines.append(f"{label:<10}: {value}")

    lines.append("")
    lines.append(f"Score     : fit {partner.fit_score:.0f}/40  "
                 f"capacity {partner.capacity_score:.0f}/35  "
                 f"access {partner.access_score:.0f}/25")

    if partner.total_assets:
        cap = mbl_cap(partner.total_assets, partner.net_worth)
        lines += [
            "",
            "Call report",
            f"  Total assets          : {_MONEY(partner.total_assets)}",
            f"  Net worth             : {_MONEY(partner.net_worth)}",
            f"  Business loans        : {_MONEY(partner.business_loans_outstanding)}"
            + (f"  ({partner.business_loan_count:,} loans)"
               if partner.business_loan_count else ""),
            f"  Statutory MBL cap     : {_MONEY(cap)}",
            f"  Cap utilization       : {_fmt_pressure(partner)}",
            f"  Low-income designated : {'yes (cap-exempt)' if partner.low_income_designated else 'no'}",
        ]

    if partner.score_rationale:
        lines += ["", "Why this score"]
        lines += [f"  - {reason}" for reason in partner.score_rationale]

    if partner.products_matched:
        lines += ["", "Products their flow feeds",
                  "  " + ", ".join(partner.products_matched)]

    angle = ANGLES.get(partner.partner_type)
    if angle:
        lines += ["", "Outreach angle", "  " + angle]

    lines += ["", "Pipeline",
              f"  Stage       : {partner.stage}",
              f"  Owner       : {partner.owner or '--'}",
              f"  Last touch  : {partner.last_touch or '--'}",
              f"  Next action : {partner.next_action or '--'}"
              + (f" (due {partner.next_action_due})" if partner.next_action_due else "")]
    if partner.warm_intro_path:
        lines.append(f"  Warm path   : {partner.warm_intro_path}")
    if partner.notes:
        lines += ["", "Notes", "  " + partner.notes]
    return "\n".join(lines)


def worklist(partners: list[Partner], size: int = 10) -> str:
    """Today's outreach queue: best unworked partners, grouped by type.

    Deliberately mixes types rather than sending you down a single vertical --
    credit unions are a long institutional cycle and brokers close fast, so you
    want both moving at once.
    """
    fresh = [p for p in partners
             if p.stage in (Stage.NOT_CONTACTED.value, Stage.RESEARCHING.value)
             and not p.do_not_contact]
    fresh.sort(key=lambda p: p.total_score, reverse=True)

    by_type: dict[str, list[Partner]] = {}
    for p in fresh:
        by_type.setdefault(p.partner_type, []).append(p)

    if not by_type:
        return "Nothing unworked. Every partner is contacted or later."

    out = [f"OUTREACH QUEUE -- top {size} unworked, by channel", "=" * 74]
    for ptype, group in sorted(by_type.items(),
                               key=lambda kv: -max(p.total_score for p in kv[1])):
        out.append("")
        out.append(f"{ptype.upper().replace('_', ' ')}  ({len(group)} unworked)")
        angle = ANGLES.get(ptype)
        if angle:
            out.append(f"  angle: {angle}")
        out.append("")
        for p in group[:size]:
            warm = "  [WARM]" if p.warm_intro_path else ""
            out.append(f"    {p.total_score:>5.1f} [{p.tier}]  {p.name[:44]:<44}"
                       f"  {p.city[:14]:<14}{warm}")
    return "\n".join(out)


def pipeline_summary(partners: list[Partner]) -> str:
    """Counts by stage and tier -- the weekly number."""
    stages: dict[str, int] = {}
    tiers: dict[str, int] = {}
    for p in partners:
        stages[p.stage] = stages.get(p.stage, 0) + 1
        tiers[p.tier or "?"] = tiers.get(p.tier or "?", 0) + 1

    out = ["PIPELINE", "=" * 40, "", "By stage"]
    for stage in Stage:
        if stages.get(stage.value):
            out.append(f"  {stage.value:<16} {stages[stage.value]:>4}")
    out += ["", "By tier"]
    for tier in ("A", "B", "C", "D", "X"):
        if tiers.get(tier):
            out.append(f"  {tier:<16} {tiers[tier]:>4}")
    out += ["", f"  {'TOTAL':<16} {len(partners):>4}"]
    return "\n".join(out)


def export_csv(partners: list[Partner], path: str | Path) -> int:
    """Write the scored list out for a spreadsheet or a CRM import."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    rows = [p.to_row() for p in sorted(partners, key=lambda p: p.total_score, reverse=True)]
    for row, partner in zip(rows, sorted(partners, key=lambda p: p.total_score, reverse=True)):
        pressure = cap_pressure(partner)
        row["cap_utilization"] = f"{pressure:.4f}" if pressure is not None else ""
    if not rows:
        return 0
    with open(path, "w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    return len(rows)
