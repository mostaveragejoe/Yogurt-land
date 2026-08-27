#!/usr/bin/env python3
"""Referral-partner prospecting CLI.

    ./prospect.py init
    ./prospect.py ingest-ncua data/ncua_2026q2.csv --state MN
    ./prospect.py ingest-csv data/cpas.csv --type cpa_firm
    ./prospect.py rank --type credit_union --limit 20
    ./prospect.py show cu-12345
    ./prospect.py worklist
    ./prospect.py touch cu-12345 --stage contacted --note "Connected on LinkedIn"
    ./prospect.py export out/mn-partners.csv

Stdlib only. No pip install.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from prospector import analytics, cadence, db, ingest_csv, ingest_ncua, report
from prospector.models import PRODUCTS, PartnerType, Stage
from prospector.scoring import score_all, score_partner

DB_DEFAULT = str(Path(__file__).resolve().parent / "data" / "partners.db")


def _rescore_and_store(conn, partners) -> int:
    for partner in score_all(partners):
        db.upsert(conn, partner)
    conn.commit()
    return len(partners)


def cmd_init(args) -> int:
    conn = db.connect(args.database)
    conn.close()
    template = Path(args.database).parent / "partner_import_template.csv"
    ingest_csv.write_template(template)
    print(f"Database ready at {args.database}")
    print(f"CSV import template written to {template}")
    return 0


def cmd_ingest_ncua(args) -> int:
    if args.inspect:
        for header in ingest_ncua.inspect(args.path):
            print(header)
        return 0

    overrides = ingest_ncua.load_map(args.map)
    partners, diag = ingest_ncua.load(args.path, state=args.state, overrides=overrides)

    print(f"Read {diag['rows_read']} rows; {diag['matched_state']} in {args.state}.")
    print("Columns matched:")
    for field, header in sorted(diag["columns_matched"].items()):
        print(f"  {field:<28} <- {header}")
    if diag["columns_unmatched"]:
        print("\n  WARNING: unmatched columns: " + ", ".join(diag["columns_unmatched"]))
        print("  Cap-pressure scoring needs total_assets, net_worth and")
        print("  business_loans_outstanding. Re-run with --inspect and pass --map.")

    conn = db.connect(args.database)
    count = _rescore_and_store(conn, partners)
    print(f"\nStored and scored {count} credit unions.")
    print(report.ranked_table(db.all_partners(conn, partner_type="credit_union"), limit=15))
    conn.close()
    return 0


def cmd_ingest_csv(args) -> int:
    partners, diag = ingest_csv.load(args.path, default_type=args.type, state=args.state)
    print(f"Read {diag['rows_read']} rows; imported {diag['imported']}.")
    if diag["skipped_no_name"]:
        print(f"  Skipped {diag['skipped_no_name']} rows with no name.")
    if diag["unrecognized_types"]:
        print(f"  Unrecognized partner_type values (fell back to {args.type}): "
              + ", ".join(diag["unrecognized_types"]))

    conn = db.connect(args.database)
    _rescore_and_store(conn, partners)
    print()
    print(report.ranked_table(partners, limit=15))
    conn.close()
    return 0


def cmd_rank(args) -> int:
    conn = db.connect(args.database)
    partners = db.all_partners(conn, partner_type=args.type, stage=args.stage,
                               state=args.state)
    if args.tier:
        partners = [p for p in partners if p.tier == args.tier.upper()]
    print(report.ranked_table(partners, limit=args.limit))
    conn.close()
    return 0


def cmd_show(args) -> int:
    conn = db.connect(args.database)
    partner = _resolve(conn, args.id)
    conn.close()
    if not partner:
        return 1
    print(report.detail(score_partner(partner)))
    return 0


def cmd_worklist(args) -> int:
    conn = db.connect(args.database)
    partners = db.all_partners(conn, partner_type=args.type, state=args.state)
    print(report.worklist(partners, size=args.size))
    conn.close()
    return 0


def cmd_pipeline(args) -> int:
    conn = db.connect(args.database)
    print(report.pipeline_summary(db.all_partners(conn, state=args.state)))
    conn.close()
    return 0


def cmd_touch(args) -> int:
    conn = db.connect(args.database)
    partner = _resolve(conn, args.id)
    if not partner:
        conn.close()
        return 1
    args.id = partner.id

    changes = {}
    if args.stage:
        changes["stage"] = args.stage
    if args.owner:
        changes["owner"] = args.owner
    if args.next_action:
        changes["next_action"] = args.next_action
    if args.due:
        changes["next_action_due"] = args.due
    if args.contact:
        changes["contact_name"] = args.contact
    if args.linkedin:
        changes["linkedin_url"] = args.linkedin
    if args.warm:
        changes["warm_intro_path"] = args.warm
    if args.dnc:
        changes["do_not_contact"] = 1
    changes["last_touch"] = args.date or dt.date.today().isoformat()

    if args.note:
        stamped = f"[{changes['last_touch']}] {args.note}"
        changes["notes"] = f"{partner.notes}\n{stamped}".strip() if partner.notes else stamped

    db.update_fields(conn, args.id, **changes)
    conn.commit()

    refreshed = score_partner(db.get(conn, args.id))
    db.upsert(conn, refreshed)
    conn.commit()
    print(report.detail(refreshed))
    conn.close()
    return 0


def cmd_export(args) -> int:
    conn = db.connect(args.database)
    partners = db.all_partners(conn, partner_type=args.type, state=args.state)
    written = report.export_csv(partners, args.path)
    conn.close()
    print(f"Wrote {written} rows to {args.path}")
    return 0


def cmd_rescore(args) -> int:
    conn = db.connect(args.database)
    partners = db.all_partners(conn)
    count = _rescore_and_store(conn, partners)
    print(f"Rescored {count} partners.")
    print(report.pipeline_summary(db.all_partners(conn)))
    conn.close()
    return 0



def _resolve(conn, query: str):
    """Turn a name fragment or id into exactly one partner, or explain why not."""
    matches = db.find(conn, query)
    if not matches:
        print(f"No partner matches {query!r}.", file=sys.stderr)
        return None
    if len(matches) > 1:
        exact = [m for m in matches if m.name.lower() == query.strip().lower()]
        if len(exact) == 1:
            return exact[0]
        print(f"{query!r} matches {len(matches)} partners -- be more specific:",
              file=sys.stderr)
        for m in matches[:10]:
            print(f"  {m.id:<28} {m.name}", file=sys.stderr)
        return None
    return matches[0]


def cmd_log(args) -> int:
    """Record what happened. Advances the stage and schedules the next touch."""
    conn = db.connect(args.database)
    when = args.date or dt.date.today().isoformat()
    kind = args.event

    touched = []
    for query in [q.strip() for q in args.id.split(",") if q.strip()]:
        partner = _resolve(conn, query)
        if not partner:
            conn.close()
            return 1

        db.add_event(conn, partner.id, kind, when, args.note or "")

        _label, new_stage = cadence.EVENT_KINDS[kind]
        stage = new_stage or partner.stage
        unanswered = db.unanswered_touches(conn, partner.id)

        # Silence parks a weak prospect. A strong one gets routed to a
        # different contact instead -- see cadence.should_park.
        parked = False
        retry_other_contact = False
        if stage == Stage.CONTACTED.value and unanswered >= cadence.MAX_UNANSWERED:
            if cadence.should_park(unanswered, partner.tier):
                stage = Stage.DORMANT.value
                parked = True
            else:
                retry_other_contact = True

        due = cadence.next_due(stage, partner.partner_type,
                               dt.date.fromisoformat(when))
        if retry_other_contact:
            due = cadence.add_business_days(
                dt.date.fromisoformat(when), cadence.ANOTHER_CONTACT_DAYS).isoformat()

        changes = {
            "stage": stage,
            "last_touch": when,
            "next_action": cadence.suggest_action(
                stage, unanswered, partner.partner_type, partner.tier),
            "next_action_due": due,
        }
        if args.note:
            stamped = f"[{when}] {kind}: {args.note}"
            changes["notes"] = (f"{partner.notes}\n{stamped}".strip()
                                if partner.notes else stamped)
        db.update_fields(conn, partner.id, **changes)
        touched.append((partner, changes, parked, retry_other_contact))

    conn.commit()

    for partner, changes, parked, retry_other_contact in touched:
        due = changes["next_action_due"] or "--"
        print(f"{partner.name}")
        print(f"  logged     : {kind}  ({when})")
        print(f"  stage      : {changes['stage']}")
        print(f"  next       : {changes['next_action']}")
        print(f"  due        : {due}")
        if parked:
            print(f"  PARKED     : {cadence.MAX_UNANSWERED} touches, no reply. "
                  f"Tier {partner.tier} -- revisit in ~90 days.")
        if retry_other_contact:
            print(f"  KEEP GOING : tier {partner.tier} at {partner.total_score:.0f} "
                  "is worth a second contact, not the parking lot.")
        if (partner.partner_type == PartnerType.CPA_FIRM.value
                and due != "--"
                and cadence.in_tax_season(dt.date.fromisoformat(when))):
            print("  note       : pushed past tax season -- CPAs do not answer "
                  "between mid-January and mid-April")
        print()
    conn.close()
    return 0


def cmd_due(args) -> int:
    """What needs action today."""
    conn = db.connect(args.database)
    today = dt.date.fromisoformat(args.today) if args.today else dt.date.today()
    partners = db.all_partners(conn, partner_type=args.type, state=args.state)

    rows = []
    for p in partners:
        if p.do_not_contact or p.stage in (Stage.DEAD.value, Stage.NOT_CONTACTED.value):
            continue
        overdue = cadence.days_overdue(p.next_action_due, today)
        unanswered = db.unanswered_touches(conn, p.id)
        rows.append({
            "partner": p,
            "overdue": overdue,
            "unanswered": unanswered,
            "stale": cadence.is_stale(p.stage, p.last_touch, p.partner_type, today),
            "action": p.next_action or cadence.suggest_action(
                p.stage, unanswered, p.partner_type),
            "priority": cadence.priority(p.total_score, overdue, p.stage),
        })

    print(report.due_list(rows, today=today, show_upcoming=args.upcoming))
    conn.close()
    return 0


def cmd_history(args) -> int:
    conn = db.connect(args.database)
    partner = _resolve(conn, args.id)
    if not partner:
        conn.close()
        return 1
    print(f"{partner.name}  [{partner.tier}] {partner.total_score:.1f}")
    print(f"stage: {partner.stage}    last touch: {partner.last_touch or '--'}")
    print()
    print(report.history(partner, db.events_for(conn, partner.id)))
    conn.close()
    return 0



def _outcome_inputs(conn, partner_type=None, state=None):
    """Shared loading for the outcome reports."""
    partners = db.all_partners(conn, partner_type=partner_type, state=state)
    deals_by_partner = {}
    for deal in db.all_deals(conn):
        deals_by_partner.setdefault(deal["partner_id"], []).append(deal)
    first_contacts = {p.id: db.first_contact_date(conn, p.id) for p in partners}
    return partners, deals_by_partner, first_contacts


def cmd_deal(args) -> int:
    """Record that a partner sent us a deal."""
    conn = db.connect(args.database)
    partner = _resolve(conn, args.id)
    if not partner:
        conn.close()
        return 1

    when = args.date or dt.date.today().isoformat()
    deal_id = db.add_deal(conn, partner.id, args.product, when,
                          amount=args.amount, note=args.note or "")

    # A referral is also an interaction: log it so cadence and history stay
    # consistent rather than diverging from the deal record.
    db.add_event(conn, partner.id, "referral", when,
                 f"{args.product} deal #{deal_id}")
    db.update_fields(
        conn, partner.id,
        stage=Stage.PRODUCING.value,
        last_touch=when,
        next_action=cadence.suggest_action(
            Stage.PRODUCING.value, 0, partner.partner_type, partner.tier),
        next_action_due=cadence.next_due(
            Stage.PRODUCING.value, partner.partner_type,
            dt.date.fromisoformat(when)),
    )
    conn.commit()

    first = db.first_contact_date(conn, partner.id)
    gap = analytics.days_between(first, when)
    print(f"Deal #{deal_id} recorded")
    print(f"  partner : {partner.name}")
    print(f"  product : {args.product}")
    if args.amount:
        print(f"  amount  : ${args.amount:,.0f}")
    print(f"  status  : referred")
    if gap is not None and gap >= 0:
        print(f"  {gap} days from first contact to first deal")
    print()
    print(f"  When it closes:  prospect.py deal-won {deal_id} --revenue N")
    print(f"  If it dies:      prospect.py deal-lost {deal_id}")
    conn.close()
    return 0


def _close_deal(args, status: str) -> int:
    conn = db.connect(args.database)
    deal = db.get_deal(conn, args.deal_id)
    if not deal:
        print(f"No deal #{args.deal_id}.", file=sys.stderr)
        conn.close()
        return 1
    db.update_deal(conn, args.deal_id, status,
                   closed_date=args.date,
                   amount=getattr(args, "amount", None),
                   revenue=getattr(args, "revenue", None),
                   note=args.note)
    conn.commit()
    updated = db.get_deal(conn, args.deal_id)
    partner = db.get(conn, deal["partner_id"])
    print(f"Deal #{args.deal_id} -> {status}")
    print(f"  partner : {partner.name if partner else deal['partner_id']}")
    if updated["amount"]:
        print(f"  amount  : ${updated['amount']:,.0f}")
    if updated["revenue"]:
        print(f"  revenue : ${updated['revenue']:,.0f}")
    conn.close()
    return 0


def cmd_deal_status(args) -> int:
    return _close_deal(args, args.status)


def cmd_deal_won(args) -> int:
    return _close_deal(args, "funded")


def cmd_deal_lost(args) -> int:
    return _close_deal(args, args.reason)


def cmd_deals(args) -> int:
    conn = db.connect(args.database)
    partner_id = None
    if args.partner:
        partner = _resolve(conn, args.partner)
        if not partner:
            conn.close()
            return 1
        partner_id = partner.id

    deals = db.all_deals(conn, partner_id=partner_id, status=args.status)
    if not deals:
        print("No deals recorded.")
        conn.close()
        return 0

    names = {p.id: p.name for p in db.all_partners(conn)}
    print(f"{'#':>4}  {'DATE':<12} {'PARTNER':<32} {'PRODUCT':<22} "
          f"{'STATUS':<12} {'AMOUNT':>10} {'REVENUE':>9}")
    print("-" * 108)
    for d in deals:
        amount = f"${d['amount']:,.0f}" if d["amount"] else "--"
        revenue = f"${d['revenue']:,.0f}" if d["revenue"] else "--"
        print(f"{d['id']:>4}  {d['referred_date']:<12} "
              f"{names.get(d['partner_id'], d['partner_id'])[:32]:<32} "
              f"{(d['product'] or '')[:22]:<22} {d['status']:<12} "
              f"{amount:>10} {revenue:>9}")
    conn.close()
    return 0


def cmd_channels(args) -> int:
    conn = db.connect(args.database)
    partners, deals_by_partner, first_contacts = _outcome_inputs(
        conn, state=args.state)
    stats = analytics.channel_stats(conn, partners, deals_by_partner, first_contacts)
    can_rank, reason = analytics.can_rank_channels(stats)
    print(report.channels_report(stats, can_rank, reason))
    conn.close()
    return 0


def cmd_producers(args) -> int:
    conn = db.connect(args.database)
    partners, deals_by_partner, first_contacts = _outcome_inputs(
        conn, partner_type=args.type, state=args.state)
    rows = analytics.producer_stats(partners, deals_by_partner, first_contacts)
    print(report.producers_report(rows, show_all=args.all))
    conn.close()
    return 0


def cmd_calibrate(args) -> int:
    conn = db.connect(args.database)
    partners, deals_by_partner, _ = _outcome_inputs(conn, state=args.state)
    rows = analytics.tier_calibration(partners, deals_by_partner)
    can_run, reason = analytics.can_calibrate(rows)
    verdict = analytics.calibration_verdict(rows) if can_run else ""
    print(report.calibration_report(rows, can_run, reason, verdict))
    conn.close()
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="prospect",
        description="Score and track commercial-finance referral partners.")
    parser.add_argument("--database", default=DB_DEFAULT, help="SQLite path")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("init", help="create the database and an import template"
                   ).set_defaults(func=cmd_init)

    p = sub.add_parser("ingest-ncua", help="load NCUA call-report data")
    p.add_argument("path")
    p.add_argument("--state", default="MN")
    p.add_argument("--map", help="JSON file overriding column matching")
    p.add_argument("--inspect", action="store_true", help="print headers and exit")
    p.set_defaults(func=cmd_ingest_ncua)

    p = sub.add_parser("ingest-csv", help="load a hand-built partner CSV")
    p.add_argument("path")
    p.add_argument("--type", default=PartnerType.CPA_FIRM.value,
                   choices=[t.value for t in PartnerType])
    p.add_argument("--state", default="MN")
    p.set_defaults(func=cmd_ingest_csv)

    p = sub.add_parser("rank", help="leaderboard")
    p.add_argument("--type", choices=[t.value for t in PartnerType])
    p.add_argument("--stage", choices=[s.value for s in Stage])
    p.add_argument("--tier", choices=list("ABCDX"))
    p.add_argument("--state")
    p.add_argument("--limit", type=int, default=25)
    p.set_defaults(func=cmd_rank)

    p = sub.add_parser("show", help="full dossier for one partner")
    p.add_argument("id")
    p.set_defaults(func=cmd_show)

    p = sub.add_parser("worklist", help="today's outreach queue")
    p.add_argument("--type", choices=[t.value for t in PartnerType])
    p.add_argument("--state")
    p.add_argument("--size", type=int, default=10)
    p.set_defaults(func=cmd_worklist)

    p = sub.add_parser("pipeline", help="counts by stage and tier")
    p.add_argument("--state")
    p.set_defaults(func=cmd_pipeline)

    p = sub.add_parser("touch", help="log an interaction / advance the stage")
    p.add_argument("id")
    p.add_argument("--stage", choices=[s.value for s in Stage])
    p.add_argument("--note")
    p.add_argument("--owner")
    p.add_argument("--next-action", dest="next_action")
    p.add_argument("--due")
    p.add_argument("--date", help="defaults to today")
    p.add_argument("--contact")
    p.add_argument("--linkedin")
    p.add_argument("--warm", help="warm intro path, e.g. 'mutual: Dave R.'")
    p.add_argument("--dnc", action="store_true", help="mark do-not-contact")
    p.set_defaults(func=cmd_touch)

    p = sub.add_parser("export", help="write scored partners to CSV")
    p.add_argument("path")
    p.add_argument("--type", choices=[t.value for t in PartnerType])
    p.add_argument("--state")
    p.set_defaults(func=cmd_export)

    sub.add_parser("rescore", help="re-run scoring over everything"
                   ).set_defaults(func=cmd_rescore)

    p = sub.add_parser("log", help="record an interaction (advances stage, schedules next)")
    p.add_argument("id", help="partner id or name fragment; comma-separate for several")
    p.add_argument("event", choices=sorted(cadence.EVENT_KINDS))
    p.add_argument("note", nargs="?", default="")
    p.add_argument("--date", help="defaults to today; use to backdate")
    p.set_defaults(func=cmd_log)

    p = sub.add_parser("due", help="what needs action today")
    p.add_argument("--type", choices=[t.value for t in PartnerType])
    p.add_argument("--state")
    p.add_argument("--upcoming", action="store_true", help="also show what is coming")
    p.add_argument("--today", help="evaluate as of this date (testing)")
    p.set_defaults(func=cmd_due)

    p = sub.add_parser("history", help="interaction log for one partner")
    p.add_argument("id")
    p.set_defaults(func=cmd_history)

    p = sub.add_parser("deal", help="record a deal a partner sent us")
    p.add_argument("id", help="partner id or name fragment")
    p.add_argument("product", choices=PRODUCTS)
    p.add_argument("--amount", type=float, help="deal size in dollars")
    p.add_argument("--date", help="defaults to today")
    p.add_argument("--note")
    p.set_defaults(func=cmd_deal)

    p = sub.add_parser("deal-won", help="mark a deal funded")
    p.add_argument("deal_id", type=int)
    p.add_argument("--revenue", type=float, help="what we earned")
    p.add_argument("--amount", type=float, help="corrected deal size")
    p.add_argument("--date")
    p.add_argument("--note")
    p.set_defaults(func=cmd_deal_won)

    p = sub.add_parser("deal-lost", help="mark a deal dead")
    p.add_argument("deal_id", type=int)
    p.add_argument("--reason", choices=["declined", "withdrawn"], default="declined")
    p.add_argument("--date")
    p.add_argument("--note")
    p.set_defaults(func=cmd_deal_lost)

    p = sub.add_parser("deal-status", help="set a deal's status explicitly")
    p.add_argument("deal_id", type=int)
    p.add_argument("status", choices=list(db.DEAL_STATUSES))
    p.add_argument("--revenue", type=float)
    p.add_argument("--amount", type=float)
    p.add_argument("--date")
    p.add_argument("--note")
    p.set_defaults(func=cmd_deal_status)

    p = sub.add_parser("deals", help="list recorded deals")
    p.add_argument("--partner")
    p.add_argument("--status", choices=list(db.DEAL_STATUSES))
    p.set_defaults(func=cmd_deals)

    p = sub.add_parser("channels", help="where the time is paying off")
    p.add_argument("--state")
    p.set_defaults(func=cmd_channels)

    p = sub.add_parser("producers", help="which partners pay, which are dead weight")
    p.add_argument("--type", choices=[t.value for t in PartnerType])
    p.add_argument("--state")
    p.add_argument("--all", action="store_true")
    p.set_defaults(func=cmd_producers)

    p = sub.add_parser("calibrate", help="did the tiers predict anything?")
    p.add_argument("--state")
    p.set_defaults(func=cmd_calibrate)

    return parser


def main(argv=None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
