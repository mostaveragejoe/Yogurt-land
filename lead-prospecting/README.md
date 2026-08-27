# Referral Partner Prospecting

A prospecting and scoring tool for **referral partners** — credit unions, CPA
firms, business brokers, CRE brokers, equipment dealers and medical-adjacent
firms who see borrowers we can serve and have a reason to send them to us.

The prospect here is not the borrower. It is the person or institution that
already has the borrower and cannot help them.

Built for the Elite Business Financing product sheet: Debt Restructuring,
Business Term Loans, SBA, Medical Working Capital, Unsecured Loans & LOC,
Accounts Receivable Financing, Commercial Bridge, Business Acquisition,
Equipment Leasing, Real Estate, Fix & Flip.

## Requirements

Python 3.9+. **No dependencies** — standard library only, no `pip install`.

## Quick start

```bash
cd lead-prospecting

python3 prospect.py init                                    # create the database
python3 prospect.py ingest-ncua data/sample_ncua_mn.csv     # load credit unions
python3 prospect.py ingest-csv  data/sample_cpas_mn.csv     # load everyone else
python3 prospect.py worklist                                # today's outreach queue
python3 prospect.py show cu-90001                           # full dossier
```

The two files in `data/` are **synthetic samples with invented institution
names** so the tool runs before you have real data. Replace them; do not treat
those numbers as real.

## Loading real data

### Credit unions — NCUA (free, quarterly)

Download the current quarter from
<https://ncua.gov/analysis/credit-union-corporate-call-report-data>, then:

```bash
python3 prospect.py ingest-ncua path/to/ncua.csv --state MN --inspect   # see headers
python3 prospect.py ingest-ncua path/to/ncua.csv --state MN
```

Header names change between export vintages, so the ingest matches columns
loosely and **reports what it matched and what it did not**. If it warns that
`total_assets`, `net_worth` or `business_loans_outstanding` are unmatched,
cap-pressure scoring is degraded — run `--inspect` and pass a mapping:

```bash
echo '{"net_worth": ["ACCT_997"], "business_loans_outstanding": ["ACCT_400A"]}' > map.json
python3 prospect.py ingest-ncua path/to/ncua.csv --map map.json
```

### Everyone else — hand-built CSV

There is no NCUA equivalent for CPAs or brokers. Build those lists from the
Minnesota Board of Accountancy licensee data, IBBA/MNBBA member directories, or
a LinkedIn Sales Navigator export, then import:

```bash
python3 prospect.py ingest-csv my-cpas.csv --type cpa_firm
```

`prospect.py init` writes `data/partner_import_template.csv` with the expected
headers.

## Daily use

```bash
python3 prospect.py due                       # what needs action today  <- start here
python3 prospect.py worklist --size 10        # new conversations to open
python3 prospect.py show northgate            # read before you message
python3 prospect.py log northgate messaged "referenced their 94% cap"
python3 prospect.py pipeline                  # the weekly number
python3 prospect.py export out/partners.csv   # hand to a CRM or spreadsheet
```

Partners resolve by **name fragment as well as id** — `northgate` works, you
never have to remember `cu-90001`. If a fragment is ambiguous the tool lists
the candidates instead of guessing.

## Logging what happened

Outreach is done by a person, so recording it has to be near-frictionless. One
command logs the touch, advances the stage, and schedules the next action:

```bash
python3 prospect.py log northgate messaged "LinkedIn connect sent"
python3 prospect.py log bluestem  replied  "CLO wants to talk SBA"
python3 prospect.py log northstar meeting  "Thursday 2pm"
python3 prospect.py log "mill city,twin ports" messaged      # several at once
python3 prospect.py log northgate nudge --date 2026-08-21    # backdate
```

Events: `messaged` `nudge` `replied` `meeting` `agreement` `referral` `no`
`dormant` `note`. Each one sets the stage and the next due date for you — you
never decide when to follow up.

```bash
python3 prospect.py history northgate         # the full interaction log
```

## Follow-up rules

`due` sorts by lateness, but **an unanswered reply carries 3× weight** — a warm
reply left sitting is the most expensive thing in a pipeline.

| Stage | Next touch | |
|---|---|---|
| contacted | 5 business days | |
| responded | 2 business days | hot — do not let it cool |
| meeting_set | 2 business days | |
| agreement | 21 business days | |
| producing | 45 business days | |
| dormant | 90 business days | |

Two rules encode judgment rather than arithmetic:

- **CPA follow-ups skip tax season.** Anything due between 15 January and
  15 April is pushed to 20 April, and a silent CPA is not counted as stale
  during that window. They aren't ignoring you; it's March.
- **Silence parks a weak prospect, not a strong one.** After three unanswered
  touches a C/D partner goes dormant. An A or B partner does not — you've
  exhausted one contact, not the institution, and the tool tells you to try the
  CEO or VP Business Lending instead. Parking an A-tier credit union for ninety
  days because one person never opened LinkedIn is how good prospects get lost.

Both live in `prospector/cadence.py` as constants at the top of the file.

Re-ingesting refreshed source data **never overwrites** hand-entered fields —
stage, notes, contact name, LinkedIn URL, owner, next action and warm-intro
path all survive. Metrics update; your relationship record does not move.

## How scoring works

Three axes summing to 100, then a tier:

| Axis | Points | Question |
|---|---|---|
| Fit | 40 | Does their decline/client flow map onto our product sheet? |
| Capacity | 35 | How many deals can they realistically send in a year? |
| Access | 25 | Can we actually reach a decision maker? |

Plus **+10 for a warm introduction path**, which is worth more than any
firmographic signal.

Tiers: **A** ≥75 · **B** 60–74 · **C** 45–59 · **D** <45.

Full rationale for every weight, and the argument for why Access is scored
inversely to size: [`docs/scoring-model.md`](docs/scoring-model.md).

Per-channel outreach guidance, including the AICPA referral-fee constraint that
shapes every CPA conversation: [`docs/outreach-playbook.md`](docs/outreach-playbook.md).

## Layout

```
prospect.py              CLI
prospector/
  models.py              Partner record, enums, product list
  scoring.py             the three-axis rubrics  <- the interesting file
  db.py                  SQLite, with re-ingest field guarding
  ingest_ncua.py         NCUA call-report -> credit unions
  ingest_csv.py          hand-built CSV -> everyone else
  report.py              leaderboard, dossier, worklist, export
data/                    database and source files (gitignored except samples)
docs/                    scoring rationale and outreach playbook
```

## A note on where this lives

This tool sits inside the `Yogurt-land` repository, which is otherwise a Godot
game project. That is not a natural home — it is here because it is the only
repository this work had access to. It is fully self-contained in this
directory and shares nothing with the game. Move it to its own repository when
one exists; nothing outside `lead-prospecting/` refers to it.
