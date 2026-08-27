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


# ---------------------------------------------------------------------------
# Follow-up surfacing
# ---------------------------------------------------------------------------

def due_list(rows: list[dict], today=None, show_upcoming: bool = False) -> str:
    """What needs action, most urgent first.

    `rows` are dicts of {partner, overdue, unanswered, stale, action} built by
    the caller (which owns the database handle). Overdue items come first,
    then stale ones, then -- optionally -- what is coming up.
    """
    import datetime as dt
    today = today or dt.date.today()

    overdue = [r for r in rows if r["overdue"] > 0]
    stale = [r for r in rows if r["overdue"] <= 0 and r["stale"]]
    upcoming = [r for r in rows if r["overdue"] <= 0 and not r["stale"]
                and r["partner"].next_action_due]

    overdue.sort(key=lambda r: -r["priority"])
    stale.sort(key=lambda r: -r["partner"].total_score)
    upcoming.sort(key=lambda r: r["partner"].next_action_due)

    out = [f"FOLLOW-UP  --  {today.isoformat()}", "=" * 78]

    if not (overdue or stale or upcoming):
        out.append("")
        out.append("  Nothing due. Run `worklist` to open new conversations.")
        return "\n".join(out)

    if overdue:
        out += ["", f"OVERDUE ({len(overdue)})", "-" * 78]
        for r in overdue:
            p = r["partner"]
            flag = "  <-- REPLIED, UNANSWERED" if p.stage == Stage.RESPONDED.value else ""
            out.append(f"  {r['overdue']:>3}d late  [{p.tier}] {p.total_score:>5.1f}  "
                       f"{p.name[:40]:<40}{flag}")
            out.append(f"            {p.stage:<14} {r['action']}")
            if r["unanswered"]:
                out.append(f"            {r['unanswered']} unanswered touch(es)")
            out.append("")

    if stale:
        out += [f"GONE QUIET ({len(stale)})", "-" * 78]
        for r in stale:
            p = r["partner"]
            out.append(f"  last touch {p.last_touch or '--':<12} [{p.tier}] "
                       f"{p.name[:40]:<40}")
            out.append(f"            {r['action']}")
        out.append("")

    if show_upcoming and upcoming:
        out += [f"COMING UP ({len(upcoming)})", "-" * 78]
        for r in upcoming[:15]:
            p = r["partner"]
            out.append(f"  due {p.next_action_due:<12} [{p.tier}] {p.name[:40]:<40}")

    return "\n".join(out)


def history(partner, events: list[dict]) -> str:
    """Interaction history for one partner."""
    if not events:
        return "  (no logged interactions)"
    lines = []
    for e in events:
        note = f"  {e['note']}" if e["note"] else ""
        lines.append(f"  {e['date']}  {e['kind']:<10}{note}")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Outcome reporting
# ---------------------------------------------------------------------------

def _pct(triple) -> str:
    """Render a Wilson triple as 'point% [low-high]'."""
    point, low, high = triple
    return f"{point:5.0%}  [{low:.0%}-{high:.0%}]"


def _money(value: float) -> str:
    if not value:
        return "--"
    if value >= 1e6:
        return f"${value/1e6:,.2f}M"
    if value >= 1e3:
        return f"${value/1e3:,.0f}k"
    return f"${value:,.0f}"


def channels_report(stats: dict, can_rank: bool, gate_reason: str) -> str:
    """Where should the next month of time go?"""
    from .analytics import MIN_WORKED_FOR_RATE, rank_channels

    if not stats:
        return "No partners recorded yet."

    out = ["WHERE THE TIME IS PAYING OFF", "=" * 78, ""]
    out.append(f"{'CHANNEL':<18} {'WORKED':>6} {'AGREED':>6} {'SENT':>5} "
               f"{'DEALS':>5} {'FUNDED':>6} {'REVENUE':>10} {'DAYS':>5}")
    out.append("-" * 78)

    for st in sorted(stats.values(), key=lambda s: -s.worked):
        days = st.median_days_to_deal
        out.append(
            f"{st.channel[:18]:<18} {st.worked:>6} {st.agreed:>6} "
            f"{st.producing:>5} {st.deals:>5} {st.funded:>6} "
            f"{_money(st.revenue):>10} {(f'{days:.0f}' if days is not None else '--'):>5}"
        )

    out += ["", "WORKED = partners actually contacted.  SENT = partners that sent "
            "at least one deal.", "DAYS = median days from first contact to first "
            "deal.", ""]

    # Rates, with intervals, only where the denominator supports them.
    rated = [s for s in stats.values() if s.has_enough_for_rate]
    thin = [s for s in stats.values() if not s.has_enough_for_rate and s.worked]
    if rated:
        out += ["PRODUCTION RATE  (share of worked partners that sent a deal)",
                "-" * 78]
        for st in sorted(rated, key=lambda s: -s.production_rate[0]):
            out.append(f"  {st.channel[:20]:<20} {_pct(st.production_rate)}"
                       f"   n={st.worked}")
        out.append("")
    if thin:
        out.append("Not enough worked partners for a rate "
                   f"(need {MIN_WORKED_FOR_RATE}): "
                   + ", ".join(f"{s.channel} (n={s.worked})" for s in thin))
        out.append("")

    # A channel with a real denominator and zero production is worth naming
    # even when no comparative verdict is available. It is not proof the
    # channel cannot work, but it is the most actionable fact on the page.
    barren = [s for s in stats.values()
              if s.has_enough_for_rate and s.producing == 0]
    if barren:
        out += ["WORTH NOTICING", "-" * 78]
        for st in sorted(barren, key=lambda s: -s.worked):
            label = st.channel.replace("_", " ")
            out.append(f"  {st.worked} {label} partners worked, zero deals.")
            if st.agreed:
                out.append(f"  {st.agreed} of them agreed to refer and still sent "
                           "nothing -- the ask may be landing, the follow-through "
                           "is not.")
        out += ["  Not proof the channel cannot work. It is a reason to change "
                "the approach", "  before spending another month on it.", ""]

    out += ["VERDICT", "-" * 78]
    if can_rank:
        ranked = rank_channels(stats)
        best, worst = ranked[0], ranked[-1]
        _, best_low, _ = best.production_rate
        _, _, worst_high = worst.production_rate
        if best_low > worst_high:
            out.append(f"  Spend more time on {best.channel.replace('_',' ')}. "
                       f"It out-produces {worst.channel.replace('_',' ')} and the "
                       "intervals do not overlap.")
        else:
            out.append("  No channel is clearly ahead -- the confidence intervals "
                       "still overlap.")
            out.append("  Keep all channels running; do not reallocate on this yet.")
    else:
        out.append(f"  Withheld. {gate_reason}")
        out.append("  The counts above are real. The comparison is not yet.")

    return "\n".join(out)


def producers_report(rows: list, show_all: bool = False) -> str:
    """Which relationships pay, and which are maintenance cost."""
    if not rows:
        return ("No partners have reached an agreement or sent a deal yet.\n"
                "Nothing to evaluate -- keep working the `due` list.")

    producing = [r for r in rows if r.deals]
    dead_weight = [r for r in rows if not r.deals]

    out = ["PARTNER VALUE", "=" * 78, ""]

    if producing:
        out += [f"{'PARTNER':<34} {'TIER':>4} {'DEALS':>5} {'FUNDED':>6} "
                f"{'VOLUME':>10} {'REVENUE':>10} {'DAYS':>5}", "-" * 78]
        for r in producing:
            days = r.days_to_first_deal
            out.append(
                f"{r.partner.name[:34]:<34} {r.partner.tier:>4} {r.deals:>5} "
                f"{r.funded:>6} {_money(r.funded_amount):>10} "
                f"{_money(r.revenue):>10} "
                f"{(str(days) if days is not None else '--'):>5}")
        total_rev = sum(r.revenue for r in producing)
        out += ["-" * 78,
                f"{'TOTAL':<34} {'':>4} {sum(r.deals for r in producing):>5} "
                f"{sum(r.funded for r in producing):>6} "
                f"{_money(sum(r.funded_amount for r in producing)):>10} "
                f"{_money(total_rev):>10}", ""]

    if dead_weight:
        out += [f"AGREED BUT NEVER PRODUCED ({len(dead_weight)})", "-" * 78,
                "These cost you check-ins and follow-ups. Decide which to keep.",
                ""]
        limit = len(dead_weight) if show_all else 15
        for r in dead_weight[:limit]:
            out.append(f"  [{r.partner.tier}] {r.partner.name[:40]:<40} "
                       f"{r.partner.partner_type[:16]:<16} "
                       f"last touch {r.partner.last_touch or '--'}")
        if len(dead_weight) > limit:
            out.append(f"  ... and {len(dead_weight)-limit} more (--all to list)")

    return "\n".join(out)


def calibration_report(rows: dict, can_run: bool, gate_reason: str,
                       verdict: str) -> str:
    """Did the A/B/C/D tiers predict anything?"""
    from .analytics import MIN_PER_TIER

    if not rows:
        return "No worked partners yet -- nothing to calibrate."

    out = ["TIER CALIBRATION", "=" * 78, "",
           "Does the scoring model predict who actually produces?", "",
           f"{'TIER':<6} {'WORKED':>7} {'SENT':>5} {'DEALS':>6} {'REVENUE':>10}"
           f"   PRODUCTION RATE", "-" * 78]

    for tier in ("A", "B", "C", "D"):
        row = rows.get(tier)
        if not row:
            continue
        rate = (_pct(row.production_rate) if row.worked >= MIN_PER_TIER
                else f"n={row.worked}, too few")
        out.append(f"{row.tier:<6} {row.worked:>7} {row.producing:>5} "
                   f"{row.deals:>6} {_money(row.revenue):>10}   {rate}")

    out += ["", "VERDICT", "-" * 78]
    if can_run:
        out.append("  " + verdict)
        out += ["",
                "  If the tiers did not separate, adjust the weights in",
                "  prospector/scoring.py by hand and re-run `rescore`. This tool",
                "  deliberately will not re-fit them for you -- at these sample",
                "  sizes that reproduces noise and degrades the rankings."]
    else:
        out.append(f"  Withheld. {gate_reason}")

    return "\n".join(out)
