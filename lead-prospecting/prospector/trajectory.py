"""Direction of travel, not just position.

Cap pressure or CRE concentration today is a snapshot. With two or more
quarters of call-report data you get the trend, and the trend often matters
more than the level:

- A credit union at 60% of its cap climbing 6 points a quarter will be
  constrained inside a year. It is not an A-tier today; it will be, and the
  relationship you want in place then is the one you start now.
- A credit union parked at 92% for three years has already solved the problem
  some other way -- a participation network, a correspondent, a CUSO. Static
  high pressure is a *worse* prospect than rising medium pressure, and a
  snapshot ranks it first.

So the read is two-dimensional: level AND slope. A pure slope bonus would get
it backwards, because falling concentration at a high level means the
institution is actively shedding -- declining deals right now -- which is the
best moment there is.
"""

from __future__ import annotations

import datetime as dt

# Slope thresholds in percentage points of concentration per quarter.
RISING = 2.0
FAST_RISING = 5.0
FALLING = -2.0

# Quarters of near-zero movement before an institution counts as entrenched.
ENTRENCHED_QUARTERS = 3

# Fit adjustments per pattern. Bounded deliberately: trajectory refines the
# ranking, it does not overturn the level.
ADJUSTMENTS = {
    "breaching": 10.0,
    "approaching": 8.0,
    "shedding": 6.0,
    "accelerating": 5.0,
    "entrenched": -6.0,
    "steady": 0.0,
    "receding": 0.0,
    "unknown": 0.0,
}


def slope_per_quarter(points: list[tuple[str, float]]) -> float | None:
    """Average change in concentration per quarter, in percentage points.

    `points` is [(as_of_date, concentration_as_fraction), ...]. Least-squares
    over the whole series rather than first-to-last, so one odd quarter does
    not dominate.
    """
    usable = []
    for as_of, value in points:
        if value is None:
            continue
        try:
            day = dt.date.fromisoformat(as_of)
        except (ValueError, TypeError):
            continue
        usable.append((day, value))

    if len(usable) < 2:
        return None

    usable.sort()
    base = usable[0][0]
    # x in quarters since the first observation, y in percentage points.
    xs = [(day - base).days / 91.3125 for day, _ in usable]
    ys = [value * 100.0 for _, value in usable]

    n = len(xs)
    mean_x = sum(xs) / n
    mean_y = sum(ys) / n
    denominator = sum((x - mean_x) ** 2 for x in xs)
    if denominator == 0:
        return None
    numerator = sum((x - mean_x) * (y - mean_y) for x, y in zip(xs, ys))
    return numerator / denominator


def classify(level: float | None, slope: float | None,
             observations: int, trigger: float = 1.0) -> str:
    """Name the pattern.

    `level` and `trigger` are fractions of the constraint (1.0 == at the cap
    for a credit union, or at the 300% CRE criterion for a bank).
    """
    if level is None or slope is None or observations < 2:
        return "unknown"

    high = level >= trigger * 0.85
    mid = trigger * 0.4 <= level < trigger * 0.85

    if high and slope >= RISING:
        # At the limit and still climbing. Whatever outlet they have is not
        # keeping up, and the decisions are being made this quarter. The most
        # urgent pattern there is -- and the one a pure slope bonus or a pure
        # level score would both miss.
        return "breaching"
    if high and slope <= FALLING:
        # Shedding at the ceiling: turning business away right now.
        return "shedding"
    if high and abs(slope) < RISING and observations >= ENTRENCHED_QUARTERS:
        # Sat at the ceiling for a year without moving. Whatever they do with
        # the overflow, they already do it, and it is not with us.
        return "entrenched"
    if mid and slope >= RISING:
        return "approaching"
    if level < trigger * 0.4 and slope >= FAST_RISING:
        return "accelerating"
    if slope <= FALLING:
        return "receding"
    return "steady"


def quarters_to_ceiling(level: float | None, slope: float | None,
                        trigger: float = 1.0) -> float | None:
    """How long until this institution hits its constraint, at current pace."""
    if level is None or slope is None or slope <= 0 or level >= trigger:
        return None
    gap_points = (trigger - level) * 100.0
    return gap_points / slope


def describe(pattern: str, slope: float | None, level: float | None,
             observations: int, quarters: float | None) -> str:
    """One line for the dossier and the score rationale."""
    if pattern == "unknown":
        return ("Only one quarter of data on file -- import an older quarter "
                "to get direction of travel.")

    rate = f"{slope:+.1f} pts/quarter over {observations} quarters"

    if pattern == "breaching":
        return (f"At the limit and still climbing: {rate}. Whatever outlet "
                "they have is not keeping up. Call this quarter.")
    if pattern == "approaching":
        eta = (f", hits the ceiling in about {quarters:.0f} quarters"
               if quarters else "")
        return (f"Approaching its limit: {rate}{eta}. Worth reaching before "
                "they need you, not after.")
    if pattern == "shedding":
        return (f"At the limit and shedding: {rate}. They are turning business "
                "away right now.")
    if pattern == "entrenched":
        return (f"Parked at the limit: {rate} across {observations} quarters. "
                "They have already found an outlet for the overflow, and it is "
                "not us. Ranks below a rising institution at a lower level.")
    if pattern == "accelerating":
        eta = f", roughly {quarters:.0f} quarters out" if quarters else ""
        return (f"Early but moving fast: {rate}{eta}. Not constrained yet -- "
                "a cheap relationship to start now.")
    if pattern == "receding":
        return f"Concentration falling from a low level: {rate}. No pressure."
    return f"Broadly flat: {rate}."


def adjustment(pattern: str) -> float:
    return ADJUSTMENTS.get(pattern, 0.0)
