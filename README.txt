LEAD ENGINE v2 — README
=======================

Lead Engine v2 is a free app that runs on your own computer.
It collects referral-partner leads and borrower leads from free public
data sources. It removes duplicates. It does quality checks.

It gives each lead a score from 0 to 100. It exports clean CSV files
for your CRM and your outreach lists.

The app never sends email and never makes calls. It only collects,
checks, and exports data.


SETUP (ONE TIME)
----------------
1. Put this folder on your Desktop.
2. Double-click install.bat.
3. Wait until the window shows "Done", then press a key.


START THE APP
-------------
1. Double-click launcher.pyw.
2. The app opens in your browser at http://127.0.0.1:5002.

NOTE: If the app already runs, the launcher only opens a new browser
tab. That is safe.


STOP THE APP
------------
Click the red "Quit App" button at the top right of the app.

CAUTION: If you only close the browser tab, the app continues to run
in the background. Always use the Quit App button.


THE PAGES
---------
Dashboard    - Totals, and one card for each data source. Click
               "Run Now" on a card to collect leads from that source.

Leads        - The full lead list. Use the filters at the top. Click a
               row to see all fields, the score breakdown, and the
               contacts. "Export CSV" downloads the filtered list.

Dedup Review - Possible duplicate pairs, side by side. Click "Merge"
               to combine a pair. Click "Not a duplicate" to keep both.

Import       - Upload a CSV list (for example a purchased list of
               attorneys or agents). Map your columns to lead fields.
               Look at the preview, then commit.

Lenders      - Lenders seen in the SBA loan data, with loan counts.

Settings     - Score weights, target state, the weekly schedule, and
               the optional Google Sheets publish.

NOTE: The SEC updates its adviser data monthly. A weekly automatic
run is the practical maximum.


ONE VALUE TO EDIT
-----------------
Open contact_scraper.py in Notepad. At the top, put your business name
and your real email address in the USER_AGENT text.

Websites and government servers see this identity when the app visits
them. The app runs correctly before you edit this value.


OPTIONAL: GOOGLE SHEETS
-----------------------
The app can push the scored lead list to a Google Sheet. This is off
until you do these steps:
1. Make a free Google Cloud service account with Sheets access.
2. Put its service_account.json file in this folder.
3. Share your sheet with the service-account email address.
4. On the Settings page, save the sheet URL.
5. Click "Publish now".
The push is one-way. The app never reads the sheet.


TROUBLESHOOTING
---------------
The browser shows "connection refused":
  Wait 10 seconds, then reload the page. If that does not help,
  double-click launcher.pyw again.

A source card shows a red error:
  Read the message. Most errors clear when you try again later.
  If the message says that a format changed, the source website
  changed and the connector needs an update.

The install fails:
  Make sure that the internet connection is up. Run install.bat again.

The app is slow during a run:
  Large government files can take some minutes to download. The app
  stays usable. The card shows the progress.

You see a duplicate lead:
  Open Dedup Review and merge the pair. If the pair is not there,
  click "Scan for duplicates now" on that page.


COMPLIANCE NOTE
---------------
Exported lists that you use for email or phone outreach are subject
to CAN-SPAM and the TCPA. Referral-fee agreements with CPAs, banks,
and advisors need attorney review before use.

The app does not send outreach, on purpose.
