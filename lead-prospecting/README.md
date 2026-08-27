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

Partner ids are built from type, name **and city**, because two firms sharing
a name is ordinary in real data. If a genuine duplicate remains after that, a
counter is appended and the import reports how many it disambiguated — a row
is never silently dropped.

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

## Recording outcomes

The point of this half: **"which channel actually works" cannot be
reconstructed later.** It is answerable in February only if you were recording
in September. Recording costs seconds; not recording costs the answer.

```bash
python3 prospect.py deal northgate sba_loans --amount 340000
python3 prospect.py deal-won 1 --revenue 11900        # it funded
python3 prospect.py deal-lost 2 --note "went with their bank"
python3 prospect.py deals                             # everything recorded
```

Recording a deal also logs a `referral` event and moves the partner to
`producing`, so the pipeline and the deal ledger never drift apart.

## Reading the outcomes

```bash
python3 prospect.py channels     # where the next month of time should go
python3 prospect.py producers    # which relationships pay, which are cost
python3 prospect.py calibrate    # did the A/B/C/D tiers predict anything?
```

**`channels`** is the one that earns its keep. It answers a calendar question,
not a statistics question: keep spending Tuesdays on CPA coffees, or move that
time to credit unions?

```
PRODUCTION RATE  (share of worked partners that sent a deal)
  cre_broker             22%  [6%-55%]   n=9
  credit_union           12%  [5%-27%]   n=34
  cpa_firm                0%  [0%-13%]   n=26

WORTH NOTICING
  26 cpa firm partners worked, zero deals.
  5 of them agreed to refer and still sent nothing -- the ask may be
  landing, the follow-through is not.

VERDICT
  No channel is clearly ahead -- the confidence intervals still overlap.
  Keep all channels running; do not reallocate on this yet.
```

Three deliberate design choices in that output:

- **Every rate carries a confidence interval.** `3/8 = 38%` invites a decision;
  `38% [14%-69%]` makes the uncertainty impossible to ignore. For this tool's
  first year the interval is the most honest number on the page.
- **Verdicts are gated, data never is.** Counts always show. A comparative
  verdict is withheld until roughly 10 deals and two channels with 8+ worked
  partners, and the tool says exactly what it is still waiting for.
- **A channel with a real denominator and zero deals gets named anyway.** Not a
  statistical claim — an observation worth acting on before spending another
  month the same way.

### What this deliberately does not do

- **It will not re-fit the scoring weights from your outcomes.** Fitting
  weights to twenty data points reproduces noise and would quietly degrade the
  rankings while looking sophisticated. `calibrate` reports whether the tiers
  separated; changing them is a human decision, made by editing the constants
  in `prospector/scoring.py` and running `rescore`.
- **No pipeline value forecasting.** Expected-value projections need base rates
  you will not have for a year. A made-up number that feels like information is
  worse than no number.
- **No conversion funnel percentages.** At n=30 every stage rate is worth
  plus-or-minus twenty points.

### Seeing it populated before you have data

The outcome reports pay off in month four, which makes it hard to judge whether
the logging is worth it. This fills a throwaway database with six months of
invented activity so you can see the reports with data in them:

```bash
python3 demo_seed.py --database /tmp/demo.db
python3 prospect.py --database /tmp/demo.db channels
```

Every record it writes is synthetic and prefixed `[DEMO]`. Never point it at
your real database.

## Contacts: people inside an institution

A partner is an institution; a contact is a person inside it. The distinction
matters because **outreach fails at the person level, not the institution
level** — a chief lending officer who never opens LinkedIn tells you nothing
about whether the CEO would answer.

```bash
python3 prospect.py contact northgate "Dana Whitfield" --title "Chief Lending Officer" --primary
python3 prospect.py contact northgate "Pat Larsen" --title "CEO"
python3 prospect.py contacts northgate            # the roster
python3 prospect.py log northgate messaged --to dana
python3 prospect.py contact-set 1 --status left_company
```

`--to` takes a contact id or a name fragment. Omit it and the touch attaches
to the only live contact when there is exactly one; with several it stays at
partner level rather than guessing wrong and corrupting someone's history.
Partners with no contacts on file keep working exactly as before.

### What this changes about silence

Three unanswered touches used to park the whole partner. Now they park the
**person**, and the partner is only parked once every route in is exhausted:

| Situation | What happens |
|---|---|
| Other contacts on file | That contact goes cold; the tool names who to try next |
| No others, tier A or B | Contact goes cold, partner stays live — go find another name |
| No others, tier C or D | Partner parks for ~90 days |

```
  next       : 3 touches, no reply. Switch to Pat Larsen (CEO),
               Sam Ruiz (VP Business Lending) -- the person is
               exhausted, the institution is not.
  CONTACT COLD: Dana Whitfield (Chief Lending Officer) marked cold.
               The institution is still live.
```

A reply revives a cold contact and un-parks the partner. Unanswered counts are
per person: three touches each to two people reads as 3 and 3, not 6.

The `due` list names the next live contact, so it tells you *who* to message,
not just which institution.

### Upgrading an existing database

The old single `contact_name` field is lifted into a contacts row
automatically on first open — marked primary, noted as migrated. It runs once
and is idempotent, and the legacy columns are left in place rather than
dropped. Nothing to do by hand.

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
