"""Follow-up scheduling and staleness rules.

The premise: whoever is doing outreach should never have to decide when to
follow up. Logging what happened is the only input; the next action and its
due date fall out of the event.
"""

from __future__ import annotations

import datetime as dt

from .models import Stage, PartnerType

# --- Event vocabulary ---------------------------------------------------
# Each event optionally moves the stage and always restarts the clock.
OUTBOUND = ("messaged", "nudge")
INBOUND = ("replied", "meeting", "agreement", "referral")

EVENT_KINDS = {
    "messaged":  ("First outbound message", Stage.CONTACTED.value),
    "nudge":     ("Follow-up sent", None),          # stage unchanged
    "replied":   ("They responded", Stage.RESPONDED.value),
    "meeting":   ("Call or meeting booked/held", Stage.MEETING_SET.value),
    "agreement": ("Referral arrangement agreed", Stage.AGREEMENT.value),
    "referral":  ("They sent us a deal", Stage.PRODUCING.value),
    "no":        ("Declined", Stage.DEAD.value),
    "dormant":   ("Parked, revisit later", Stage.DORMANT.value),
    "note":      ("Note only, no status change", None),
}

# Business days until the next action, by stage.
FOLLOW_UP_DAYS = {
    Stage.RESEARCHING.value: 3,
    Stage.NOT_CONTACTED.value: 3,
    Stage.CONTACTED.value: 5,
    Stage.RESPONDED.value: 2,       # hot -- do not let a reply cool off
    Stage.MEETING_SET.value: 2,
    Stage.AGREEMENT.value: 21,
    Stage.PRODUCING.value: 45,
    Stage.DORMANT.value: 90,
    Stage.DEAD.value: 0,            # no follow-up
}

# Unanswered outbound touches before a partner is parked automatically.
MAX_UNANSWERED = 3

# A partner is stale when the silence exceeds its stage cadence by this factor.
STALE_FACTOR = 1.5

# --- CPA tax season -----------------------------------------------------
# CPAs are unreachable from mid-January to mid-April. A follow-up that lands
# in that window is pushed past it rather than wasted.
TAX_SEASON_START = (1, 15)
TAX_SEASON_END = (4, 15)
TAX_SEASON_RESUME = (4, 20)


def add_business_days(start: dt.date, days: int) -> dt.date:
    """Add business days, skipping weekends."""
    if days <= 0:
        return start
    current, remaining = start, days
    while remaining > 0:
        current += dt.timedelta(days=1)
        if current.weekday() < 5:
            remaining -= 1
    return current


def in_tax_season(day: dt.date) -> bool:
    start = dt.date(day.year, *TAX_SEASON_START)
    end = dt.date(day.year, *TAX_SEASON_END)
    return start <= day <= end


def defer_past_tax_season(day: dt.date) -> dt.date:
    """Push a CPA follow-up out of the window where nobody will answer."""
    if not in_tax_season(day):
        return day
    return dt.date(day.year, *TAX_SEASON_RESUME)


def next_due(stage: str, partner_type: str, frm: dt.date | None = None) -> str:
    """Due date for the next action, as an ISO string. Empty when none."""
    frm = frm or dt.date.today()
    days = FOLLOW_UP_DAYS.get(stage, 7)
    if days <= 0:
        return ""
    due = add_business_days(frm, days)
    if partner_type == PartnerType.CPA_FIRM.value:
        due = defer_past_tax_season(due)
    return due.isoformat()


# Tiers worth a second contact rather than the parking lot.
PERSIST_TIERS = ("A", "B")

# Business days before retrying a high-value partner through another contact.
ANOTHER_CONTACT_DAYS = 10


def should_park(unanswered: int, tier: str) -> bool:
    """Whether silence should park this partner.

    A weak prospect that has ignored three messages is finished. A strong one
    is not -- you have exhausted one contact, not the institution. Parking an
    A-tier credit union for ninety days because the CLO never opened LinkedIn
    is how good prospects get lost.
    """
    return unanswered >= MAX_UNANSWERED and tier not in PERSIST_TIERS


def suggest_action(stage: str, unanswered: int, partner_type: str,
                   tier: str = "") -> str:
    """Plain-language next action, so the due list is directly workable."""
    if stage == Stage.RESPONDED.value:
        return "They replied -- respond and propose a specific time"
    if stage == Stage.MEETING_SET.value:
        return "Confirm the meeting, then send the one-pager after"
    if stage == Stage.AGREEMENT.value:
        return "Check in: has anything come across their desk yet?"
    if stage == Stage.PRODUCING.value:
        return "Relationship check-in -- keep it warm"
    if stage == Stage.DORMANT.value:
        return "Re-approach with a new angle or a recent win"
    if stage == Stage.CONTACTED.value:
        if unanswered >= MAX_UNANSWERED:
            if tier in PERSIST_TIERS:
                return (f"{unanswered} touches, no reply -- worth too much to park. "
                        "Try a different contact at the same firm "
                        "(CEO, VP Business Lending, another partner)")
            return "No reply after several touches -- park it or try another contact"
        if unanswered >= 2:
            tail = " (a CPA in tax season is not ignoring you)" \
                if partner_type == PartnerType.CPA_FIRM.value else ""
            return f"Third touch -- change the angle{tail}"
        return "Follow up on the opening message"
    return "Open the conversation"


def is_stale(stage: str, last_touch: str, partner_type: str,
             today: dt.date | None = None) -> bool:
    """True when silence has run well past this stage's normal cadence."""
    if not last_touch or stage in (Stage.DEAD.value, Stage.NOT_CONTACTED.value):
        return False
    today = today or dt.date.today()
    try:
        last = dt.date.fromisoformat(last_touch)
    except ValueError:
        return False
    cadence = FOLLOW_UP_DAYS.get(stage, 7)
    if cadence <= 0:
        return False
    # Weekend-inclusive approximation of the business-day cadence.
    threshold = int(cadence * STALE_FACTOR * 7 / 5)
    if partner_type == PartnerType.CPA_FIRM.value and in_tax_season(today):
        return False        # not stale, just April
    return (today - last).days > threshold


def days_overdue(due: str, today: dt.date | None = None) -> int:
    """Positive when past due, negative when still ahead."""
    if not due:
        return 0
    today = today or dt.date.today()
    try:
        return (today - dt.date.fromisoformat(due)).days
    except ValueError:
        return 0


def priority(score: float, overdue: int, stage: str) -> float:
    """Ranking for the due list.

    Lateness dominates, but a high-scoring partner outranks a low-scoring one
    that went overdue on the same day. Replies are boosted hard -- a warm reply
    left sitting is the most expensive thing in the pipeline.
    """
    weight = 3.0 if stage == Stage.RESPONDED.value else 1.0
    return (max(overdue, 0) * 10.0 + score * 0.5) * weight
