"""Website contact scraper for Lead Engine v2.

This module finds named people on a firm website.  It obeys robots.txt.
It reads team pages and contact pages.  It decodes Cloudflare email
protection.  It gives each contact a confidence value from 0.0 to 1.0.

The public entry point is scrape_firm(website).
"""

# =====================================================================
# EDIT THIS ONE VALUE.
# Put your business name and a real contact email in the string below.
# This is the only value in the codebase that you must edit.
# The app runs correctly before you edit it.
# =====================================================================
USER_AGENT = (
    "LeadEngineBot/2.0 (commercial loan brokerage lead research; "
    "contact: YOUR_EMAIL@example.com)"
)

import re
import time
import urllib.robotparser
from html import unescape
from urllib.parse import urljoin, urlparse

import requests
from bs4 import BeautifulSoup

# Polite limits.  Do not change these to be less polite.
REQUEST_DELAY_SECONDS = 2.0
MAX_PAGES_PER_SITE = 8
REQUEST_TIMEOUT = 20

# Path words that point to pages with people on them.
CONTACT_PATH_HINTS = (
    "contact", "about", "team", "our-team", "people", "staff",
    "leadership", "advisors", "advisers", "professionals", "attorneys",
    "agents", "bios", "meet", "who-we-are", "management", "officers",
)

# Words that appear in job titles.
TITLE_WORDS = (
    "president", "vice president", "vp", "ceo", "cfo", "coo", "cio",
    "founder", "partner", "principal", "owner", "director", "manager",
    "officer", "advisor", "adviser", "planner", "analyst", "associate",
    "agent", "broker", "attorney", "counsel", "cpa", "accountant",
    "banker", "lender", "chairman", "chair", "managing member",
    "wealth", "portfolio",
)

EMAIL_RE = re.compile(
    r"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"
)
PHONE_RE = re.compile(
    r"(?:\+?1[\s.\-]?)?\(?\d{3}\)?[\s.\-]?\d{3}[\s.\-]?\d{4}"
)
# A person name: two to four capitalized words, with optional middle initial.
NAME_RE = re.compile(
    r"^[A-Z][a-zA-Z'\-]+(?:\s+[A-Z]\.?)?(?:\s+[A-Z][a-zA-Z'\-]+){1,2}"
    r"(?:,?\s*(?:Jr\.?|Sr\.?|II|III|IV|CPA|CFP|CFA|JD|MBA|AIF))*$"
)

# Words that show a string is not a person name.
NOT_NAME_WORDS = (
    "llc", "inc", "corp", "group", "services", "financial", "insurance",
    "bank", "credit", "solutions", "company", "associates", "office",
    "home", "menu", "search", "login", "privacy", "terms", "copyright",
    "read more", "learn more", "contact us", "about us", "our team",
    "get started", "view all", "all rights",
)

_last_request_time = {}


def _polite_wait(host):
    """Wait so that requests to one host are at least 2 seconds apart."""
    now = time.time()
    last = _last_request_time.get(host, 0)
    delta = now - last
    if delta < REQUEST_DELAY_SECONDS:
        time.sleep(REQUEST_DELAY_SECONDS - delta)
    _last_request_time[host] = time.time()


def _robots_allows(url):
    """Return True if robots.txt permits a fetch of this URL."""
    parsed = urlparse(url)
    robots_url = f"{parsed.scheme}://{parsed.netloc}/robots.txt"
    rp = urllib.robotparser.RobotFileParser()
    try:
        _polite_wait(parsed.netloc)
        rp.set_url(robots_url)
        rp.read()
        return rp.can_fetch(USER_AGENT, url)
    except Exception:
        # If robots.txt is not readable, permit the fetch.
        return True


def _fetch(url):
    """Fetch one page politely.  Return the HTML text or None."""
    parsed = urlparse(url)
    _polite_wait(parsed.netloc)
    try:
        resp = requests.get(
            url,
            headers={"User-Agent": USER_AGENT},
            timeout=REQUEST_TIMEOUT,
            allow_redirects=True,
        )
        if resp.status_code != 200:
            return None
        ctype = resp.headers.get("Content-Type", "")
        if "html" not in ctype.lower():
            return None
        return resp.text
    except Exception:
        return None


def decode_cfemail(hex_string):
    """Decode a Cloudflare data-cfemail attribute value to an email."""
    try:
        data = bytes.fromhex(hex_string)
        key = data[0]
        return "".join(chr(b ^ key) for b in data[1:])
    except Exception:
        return None


def _extract_emails(soup, html_text):
    """Return all emails on the page, with Cloudflare emails decoded."""
    emails = set()
    for tag in soup.select("[data-cfemail]"):
        decoded = decode_cfemail(tag.get("data-cfemail", ""))
        if decoded:
            emails.add(decoded.lower())
    for a in soup.select('a[href^="mailto:"]'):
        addr = a.get("href", "")[7:].split("?")[0].strip()
        if addr and "@" in addr:
            emails.add(unescape(addr).lower())
    for match in EMAIL_RE.findall(html_text):
        low = match.lower()
        # Cloudflare puts a fake local part in the visible text.  Skip it.
        if "email-protection" in low or low.endswith((".png", ".jpg", ".gif", ".svg", ".webp")):
            continue
        emails.add(low)
    return emails


def _looks_like_name(text):
    """Return True if the text looks like the name of a person."""
    text = " ".join(text.split())
    if not text or len(text) > 40:
        return False
    low = text.lower()
    for word in NOT_NAME_WORDS:
        if word in low:
            return False
    return bool(NAME_RE.match(text))


def _find_title_near(element):
    """Look for a job title in the element and its next siblings."""
    candidates = []
    parent = element.parent
    if parent is not None:
        for sib in list(parent.find_all(recursive=False)):
            text = " ".join(sib.get_text(" ", strip=True).split())
            if text and text != element.get_text(strip=True):
                candidates.append(text)
    for text in candidates[:6]:
        low = text.lower()
        if len(text) <= 70 and any(w in low for w in TITLE_WORDS):
            return text
    return ""


def _extract_cards(soup, page_url, page_emails):
    """Extract person cards from one page.

    A card is a small block that contains a name, and possibly a title,
    an email, a phone number, and a LinkedIn URL.
    """
    contacts = {}
    heads = soup.find_all(["h1", "h2", "h3", "h4", "h5", "strong", "b"])
    for head in heads:
        raw = head.get_text(" ", strip=True)
        name = " ".join(raw.split())
        if not _looks_like_name(name):
            continue
        card = head
        # Walk up a little to find the card container.
        for _ in range(3):
            if card.parent is None:
                break
            parent_text = card.parent.get_text(" ", strip=True)
            if len(parent_text) > 600:
                break
            card = card.parent

        card_text = card.get_text(" ", strip=True)
        title = _find_title_near(head)
        if not title:
            low = card_text.lower()
            for w in TITLE_WORDS:
                idx = low.find(w)
                if idx >= 0:
                    start = max(0, idx - 30)
                    snippet = card_text[start:idx + len(w) + 30]
                    title = " ".join(snippet.split())[:70]
                    break

        email = ""
        card_html = str(card)
        card_soup = BeautifulSoup(card_html, "html.parser")
        card_emails = _extract_emails(card_soup, card_html)
        if card_emails:
            email = sorted(card_emails)[0]
        else:
            # Try a match between the name and a page-level email.
            first = name.split()[0].lower()
            last = name.split()[-1].lower()
            for addr in page_emails:
                local = addr.split("@")[0]
                if first in local or last in local:
                    email = addr
                    break

        phone = ""
        m = PHONE_RE.search(card_text)
        if m:
            phone = m.group(0).strip()

        linkedin = ""
        for a in card_soup.select('a[href*="linkedin.com"]'):
            linkedin = a.get("href", "")
            break

        confidence = 0.4
        if email:
            confidence += 0.25
        if title:
            confidence += 0.15
        if linkedin:
            confidence += 0.1
        if phone:
            confidence += 0.05
        confidence = round(min(confidence, 0.95), 2)

        key = name.lower()
        record = {
            "name": name,
            "title": title,
            "email": email,
            "phone": phone,
            "linkedin_url": linkedin,
            "source_url": page_url,
            "confidence": confidence,
        }
        old = contacts.get(key)
        if old is None or record["confidence"] > old["confidence"]:
            contacts[key] = record
    return list(contacts.values())


def _candidate_pages(base_url, soup):
    """Return internal links that probably show people or contact data."""
    base_host = urlparse(base_url).netloc
    found = []
    seen = set()
    for a in soup.find_all("a", href=True):
        href = a["href"].strip()
        if href.startswith(("mailto:", "tel:", "javascript:", "#")):
            continue
        full = urljoin(base_url, href)
        parsed = urlparse(full)
        if parsed.netloc != base_host:
            continue
        path = parsed.path.lower()
        if any(h in path for h in CONTACT_PATH_HINTS):
            clean = f"{parsed.scheme}://{parsed.netloc}{parsed.path}"
            if clean not in seen:
                seen.add(clean)
                found.append(clean)
    return found


def scrape_firm(website, max_pages=MAX_PAGES_PER_SITE):
    """Scrape one firm website for named contacts.

    Returns a dict:
      {"ok": bool, "error": str or None, "contacts": [ ... ],
       "pages_fetched": int}
    Each contact has: name, title, email, phone, linkedin_url,
    source_url, confidence.
    """
    result = {"ok": False, "error": None, "contacts": [], "pages_fetched": 0}
    if not website:
        result["error"] = "No website on file for this lead."
        return result

    url = website.strip()
    if not url.startswith(("http://", "https://")):
        url = "https://" + url

    if not _robots_allows(url):
        result["error"] = "robots.txt does not permit a scrape of this site."
        return result

    home_html = _fetch(url)
    if home_html is None:
        # Try http as a fallback for old sites.
        if url.startswith("https://"):
            alt = "http://" + url[8:]
            home_html = _fetch(alt)
            if home_html is not None:
                url = alt
    if home_html is None:
        result["error"] = "The website did not give a readable page."
        return result

    result["pages_fetched"] = 1
    soup = BeautifulSoup(home_html, "html.parser")
    all_contacts = {}

    page_emails = _extract_emails(soup, home_html)
    for c in _extract_cards(soup, url, page_emails):
        all_contacts[c["name"].lower()] = c

    for page_url in _candidate_pages(url, soup)[: max_pages - 1]:
        if not _robots_allows(page_url):
            continue
        html = _fetch(page_url)
        if html is None:
            continue
        result["pages_fetched"] += 1
        page_soup = BeautifulSoup(html, "html.parser")
        emails_here = _extract_emails(page_soup, html)
        page_emails |= emails_here
        for c in _extract_cards(page_soup, page_url, emails_here):
            key = c["name"].lower()
            old = all_contacts.get(key)
            if old is None:
                all_contacts[key] = c
            else:
                # Keep the record with more data.
                for field in ("title", "email", "phone", "linkedin_url"):
                    if not old[field] and c[field]:
                        old[field] = c[field]
                old["confidence"] = max(old["confidence"], c["confidence"])

    result["contacts"] = sorted(
        all_contacts.values(), key=lambda c: -c["confidence"]
    )
    result["ok"] = True
    return result


if __name__ == "__main__":
    import json
    import sys

    if len(sys.argv) > 1:
        print(json.dumps(scrape_firm(sys.argv[1]), indent=2))
    else:
        print("Usage: python contact_scraper.py https://example.com")
