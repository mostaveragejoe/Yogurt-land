#!/usr/bin/env python3
"""Populate a database with six months of SYNTHETIC activity.

The outcome reports pay off in month four, not week one. That makes it hard to
judge whether logging deals is worth the effort. This generates a plausible
six months so you can see what the reports look like once they have data --
and so the gating logic can be tested against a populated database.

Everything it writes is invented. Never point it at your real database.

    python3 demo_seed.py --database /tmp/demo.db
    python3 prospect.py --database /tmp/demo.db channels
"""

from __future__ import annotations

import argparse
import datetime as dt
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from prospector import cadence, db
from prospector.models import Partner, PartnerType, Stage
from prospector.scoring import score_partner

START = dt.date(2026, 3, 2)

# (type, count, P(reply), P(agreement|reply), P(deal|agreement), deals/producer)
PROFILES = [
    (PartnerType.CREDIT_UNION.value,   34, 0.42, 0.55, 0.70, (1, 4)),
    (PartnerType.CPA_FIRM.value,       26, 0.30, 0.45, 0.28, (1, 2)),
    (PartnerType.BUSINESS_BROKER.value, 14, 0.60, 0.60, 0.62, (1, 3)),
    (PartnerType.CRE_BROKER.value,      9, 0.55, 0.50, 0.55, (1, 2)),
    (PartnerType.EQUIPMENT_DEALER.value, 7, 0.45, 0.40, 0.45, (1, 2)),
]

PRODUCTS_BY_TYPE = {
    PartnerType.CREDIT_UNION.value: ["sba_loans", "equipment_leasing",
                                     "commercial_bridge", "accounts_receivable"],
    PartnerType.CPA_FIRM.value: ["debt_restructuring", "business_term_loans",
                                 "unsecured_loans_loc"],
    PartnerType.BUSINESS_BROKER.value: ["business_acquisition", "sba_loans"],
    PartnerType.CRE_BROKER.value: ["real_estate", "commercial_bridge", "fix_and_flip"],
    PartnerType.EQUIPMENT_DEALER.value: ["equipment_leasing"],
}

CITIES = ["Minneapolis", "Saint Paul", "Rochester", "Duluth", "Bloomington",
          "Saint Cloud", "Mankato", "Eagan", "Moorhead", "Winona"]

# Explicit, distinct id prefixes. Truncating the type name collides:
# credit_union[:3] and cre_broker[:3] are both "cre".
ID_PREFIX = {
    PartnerType.CREDIT_UNION.value: "cu",
    PartnerType.CPA_FIRM.value: "cpa",
    PartnerType.BUSINESS_BROKER.value: "bb",
    PartnerType.CRE_BROKER.value: "creb",
    PartnerType.EQUIPMENT_DEALER.value: "eq",
}


def business_day(base: dt.date, offset: int) -> dt.date:
    return cadence.add_business_days(base, offset)


def seed(path: str, seed_value: int = 7) -> None:
    rng = random.Random(seed_value)
    conn = db.connect(path)

    deal_no = 0
    for ptype, count, p_reply, p_agree, p_deal, deal_range in PROFILES:
        for i in range(count):
            pid = f"demo-{ID_PREFIX[ptype]}-{i:03d}"
            partner = Partner(
                id=pid,
                name=f"[DEMO] {ptype.replace('_',' ').title()} {i+1:02d}",
                partner_type=ptype,
                city=rng.choice(CITIES),
                state="MN",
            )
            # Give depositories plausible call-report figures so tiers vary.
            if ptype == PartnerType.CREDIT_UNION.value:
                assets = rng.choice([80e6, 150e6, 280e6, 460e6, 700e6, 1.4e9, 3.2e9])
                partner.total_assets = assets
                partner.net_worth = assets * rng.uniform(0.085, 0.12)
                cap = min(assets * 0.1225, partner.net_worth * 1.75)
                partner.business_loans_outstanding = cap * rng.uniform(0.15, 1.05)
                partner.business_loan_count = int(
                    partner.business_loans_outstanding / rng.uniform(2e5, 4e5))
            else:
                partner.headcount = rng.choice([2, 6, 14, 28, 45, 90])
                partner.active_listings = rng.choice([0, 4, 12, 26, 41])
                partner.years_active = rng.randint(2, 22)
                if ptype == PartnerType.CPA_FIRM.value:
                    partner.does_attest_work = rng.random() < 0.6
                    partner.has_advisory_practice = rng.random() < 0.5
                    partner.industry_specialties = rng.sample(
                        ["construction", "trucking", "dental", "agriculture"],
                        k=rng.randint(0, 2))

            score_partner(partner)
            db.upsert(conn, partner)

            # --- walk the relationship forward ---------------------------
            first = business_day(START, rng.randint(0, 90))
            db.add_event(conn, pid, "messaged", first.isoformat())
            stage, last = Stage.CONTACTED.value, first

            if rng.random() < p_reply:
                last = business_day(last, rng.randint(2, 12))
                db.add_event(conn, pid, "replied", last.isoformat())
                stage = Stage.RESPONDED.value

                if rng.random() < p_agree:
                    last = business_day(last, rng.randint(5, 25))
                    db.add_event(conn, pid, "meeting", last.isoformat())
                    last = business_day(last, rng.randint(3, 20))
                    db.add_event(conn, pid, "agreement", last.isoformat())
                    stage = Stage.AGREEMENT.value

                    if rng.random() < p_deal:
                        stage = Stage.PRODUCING.value
                        for _ in range(rng.randint(*deal_range)):
                            last = business_day(last, rng.randint(10, 60))
                            if last > dt.date(2026, 8, 27):
                                break
                            deal_no += 1
                            amount = rng.choice([65e3, 120e3, 180e3, 250e3,
                                                 400e3, 750e3, 1.2e6])
                            did = db.add_deal(
                                conn, pid, rng.choice(PRODUCTS_BY_TYPE[ptype]),
                                last.isoformat(), amount=amount)
                            db.add_event(conn, pid, "referral", last.isoformat(),
                                         f"demo deal #{did}")
                            roll = rng.random()
                            if roll < 0.55:
                                closed = business_day(last, rng.randint(10, 40))
                                db.update_deal(conn, did, "funded",
                                               closed_date=closed.isoformat(),
                                               revenue=amount * rng.uniform(0.02, 0.04))
                            elif roll < 0.75:
                                db.update_deal(conn, did, "declined",
                                               closed_date=business_day(
                                                   last, rng.randint(10, 30)).isoformat())
            else:
                # Silence: a couple of nudges, then park the weak ones.
                for _ in range(rng.randint(1, 3)):
                    last = business_day(last, rng.randint(4, 9))
                    db.add_event(conn, pid, "nudge", last.isoformat())
                if cadence.should_park(db.unanswered_touches(conn, pid), partner.tier):
                    stage = Stage.DORMANT.value

            db.update_fields(
                conn, pid, stage=stage, last_touch=last.isoformat(),
                next_action=cadence.suggest_action(
                    stage, db.unanswered_touches(conn, pid), ptype, partner.tier),
                next_action_due=cadence.next_due(stage, ptype, last))

    conn.commit()
    partners = len(db.all_partners(conn))
    deals = len(db.all_deals(conn))
    funded = len(db.all_deals(conn, status="funded"))
    conn.close()
    print(f"Seeded {path}")
    print(f"  {partners} synthetic partners, {deals} deals ({funded} funded)")
    print(f"  All names prefixed [DEMO]. Do not mix with real data.")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--database", required=True)
    ap.add_argument("--seed", type=int, default=7)
    args = ap.parse_args()
    seed(args.database, args.seed)
