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

from prospector import db, ingest_csv, ingest_ncua, report
from prospector.models import PartnerType, Stage
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
    partner = db.get(conn, args.id)
    conn.close()
    if not partner:
        print(f"No partner with id {args.id!r}.", file=sys.stderr)
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
    partner = db.get(conn, args.id)
    if not partner:
        print(f"No partner with id {args.id!r}.", file=sys.stderr)
        conn.close()
        return 1

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

    return parser


def main(argv=None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
