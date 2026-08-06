# Decisions

This file records the choices made where the master prompt was open or
where reality differed from the prompt. Ordered by build phase.

## Starting point

1. **The repo had no predecessor files.** The prompt names
   `contact_scraper.py`, `advisor_lead_gen.py`, `contacts_module.py`,
   and `contacts_panel.html` as existing files. The repository was
   empty except for a stub README. Because of this, `contact_scraper.py`
   was written new from the prompt's description (robots.txt check,
   card extraction, Cloudflare email decode, confidence score). The
   proven patterns from `advisor_lead_gen.py` (`first_match`, `to_int`,
   ZIP format detection, non-empty-field upsert) were written directly
   into `lead_engine.py` from the prompt's description of them.
2. **Live connector tests were not possible in the build environment.**
   The build container blocks traffic to the data-source hosts. Source
   URLs were confirmed through web search in August 2026. Every
   connector catches its own errors and shows a readable message in
   the UI, as the prompt demands.

## Schema

3. **One extra column: `leads.corroborators`.** It holds a
   comma-separated list of other connectors that also saw the lead.
   The cross-source score signal and the merge logic need it.
4. **Two extra tables: `lenders` and `scrape_log`.** Section 3 of the
   prompt asks for a lender map from the SBA data, and Phase 6 asks
   for a scrape log. The fixed schema list in section 4 has no place
   for them, so they are separate small tables.
5. **`status` and `notes` carry through the upsert** so that the
   migration from `leads.db` keeps old notes.

## App structure

6. **One base template through a Jinja `DictLoader`.** All pages render
   with `render_template_string` and extend an in-memory `base.html`
   string. No template files exist on disk, per Ground Rule 5.
7. **Import-only lead types** (CRE agents, attorneys, OCI insurance
   list) appear as dashboard cards whose button opens the Import page
   with the type preselected.
8. **The demo duplicate pair is inserted with a raw INSERT.** The
   exact-match cascade would merge "Acme Financial LLC, Madison" and
   "Acme Financial, Madison" at insert time (equal normalized name and
   city). The Phase 4 acceptance test needs the pair in the review
   queue, so the demo seed bypasses the cascade for those two rows only.
9. **Demo mode does offline checks only.** Website and MX checks show
   "Not checked yet" until the user runs a verification sweep. Demo
   mode makes no network traffic.

## Dedup

10. **Merge keeps the richer record automatically.** Richer = more
    non-empty fields. The review card marks which record wins before
    the user clicks Merge.
11. **A "Not a duplicate" answer is remembered.** The resolved queue
    row stays in the table, and the fuzzy scan never offers that pair
    again.

## Connectors

12. **NCUA**: the connector downloads the newest quarterly call-report
    ZIP from the NCUA quarterly-data page and reads the `FOICU`
    member (the credit-union directory file) for the target state.
13. **SBA**: the connector asks the CKAN catalog API
    (`data.sba.gov/api/3/action/package_show?id=7-a-504-foia`) for the
    newest 7(a) CSV resource, then streams it and keeps only rows for
    the target state. Loan amount, approval date, and lender go into
    `source_detail`. Lender totals go into the `lenders` table.
14. **SEC link choice**: the file classification looks only at the
    link file name and the link text, not the full URL path. The SEC
    folder path contains the word "exempt" for every file, and a full
    URL match would reject everything.
15. **IRS PTIN rows without a PTIN column** get a stable hash of
    name + ZIP as their external ID, so repeat runs stay idempotent.

## Scoring and settings

16. **A weight change on the Settings page rescored all leads at
    once** from the stored check rows. No new network checks run.
17. **Weights do not need to sum to 100.** The score scales to the
    actual weight total.

## Publish

18. **Google Sheets publish is a background job with one
    `values.update` call**, inside the free quota. It activates only
    when `service_account.json` exists and a sheet URL is saved.

## Shutdown

19. **`/quit` answers first, then calls `os._exit(0)`** on a 0.4 s
    timer. Flask's development server has no clean public shutdown
    hook; for a local single-user app a hard exit is safe because all
    SQLite writes commit per action.
