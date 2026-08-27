# Scoring model

Every partner scores 0–100 across three axes, plus a warm-intro bonus.

```
FIT      0–40   Does their reject/client flow map onto our product sheet?
CAPACITY 0–35   How many deals can they realistically send in a year?
ACCESS   0–25   Can we actually get a conversation with a decision maker?
WARM    +10     A credible introduction path
```

Fit and Capacity answer **quality**. Access answers **viability**. Keeping them
separate matters: a $9B credit union has enormous capacity and is functionally
unreachable by a new entrant, and a single blended score would hide that.

Tiers: **A** ≥75 · **B** 60–74 · **C** 45–59 · **D** <45 · **X** do-not-contact.

---

## Credit unions and community banks

### Fit is driven by cap pressure

Under the Federal Credit Union Act as amended by CUMAA (1998), a credit union's
member business lending is capped at the **lesser of 1.75× net worth or 12.25%
of total assets**. Low-income-designated credit unions are exempt.

```
cap            = min(0.1225 × total_assets, 1.75 × net_worth)
cap_pressure   = business_loans_outstanding / cap
```

This is the single most useful number in the tool.

A credit union at 90%+ of its cap is **declining business loans it wants to
make, for regulatory reasons rather than credit reasons**. Those borrowers are
bank-quality paper turned away for a non-credit reason — the highest-quality
referral available anywhere in this market, and nothing like an MCA decline.

| Cap pressure | Fit | Reading |
|---|---|---|
| ≥ 90% | 40 | At or over the cap. Forced declines of good paper. |
| 75–90% | 32 | Rationing business credit already. |
| 60–75% | 24 | Will hit the ceiling within a year or two. |
| 40–60% | 14 | Room to lend; declines are credit-driven, not cap-driven. |
| < 40% | 8 | No structural pressure. |

Two special cases score separately:

- **Low-income designated → 14.** Cap-exempt, so the strongest angle is gone.
  Still worth contacting for ordinary out-of-policy declines.
- **Zero business lending → 10.** A different pitch entirely: they have
  commercial members and no product to serve them. Lower score because they may
  not recognize the need, which makes the conversation longer.

### Capacity

Business loan count where the call report provides it, falling back to
portfolio size, falling back to total assets. Count is preferred because it
measures how many business *applications* cross their desk — which is what
actually predicts referral volume — rather than how much money is out.

### Access is scored inversely to size

This is the deliberate, slightly counterintuitive part.

| Assets | Access | Reading |
|---|---|---|
| < $250M | 25 | CEO or CLO directly reachable. |
| $250M–$1B | 19 | Lending leadership reachable with one intro. |
| $1B–$3B | 12 | Commercial lending department, vendor process. |
| > $3B | 6 | Gatekept, long procurement cycle, existing partners. |

Capacity rewards size and Access penalizes it, so the model resolves to a
**mid-size credit union near its cap** as the ideal profile. That is the right
answer, and it falls out of the weights rather than being asserted.

Worked example from the sample data: a $486M credit union at 94% of cap scores
89, edging a $3.85B credit union at 85% of cap on 73 — despite the larger one
having five times the loan count.

---

## CPA firms

### Fit

Baseline 8 (any CPA sees financially stressed business clients), then:

- **+7 per matching industry specialty**, capped at 20 — construction,
  trucking, agriculture, manufacturing, medical, dental, veterinary, real
  estate, restaurant, staffing, wholesale. These map onto specific products.
- **+10 for a CAS/advisory practice.** Already in an advisory posture rather
  than pure compliance work, so a financing referral is a smaller step.
- **−8 for attest work.** See below.

### The attest penalty

AICPA Rule 503 bars a CPA from accepting a referral fee for **any client they
perform audit, review or compilation work for**. The prohibition is absolute —
disclosure does not cure it. For other clients a referral fee is permitted but
requires written disclosure.

Most small-business CPAs do the compilation *and* the tax return, so for
exactly the clients worth referring, the fee is off the table.

This does not kill the relationship. It changes the pitch from commission to a
no-fee reciprocal arrangement. The tool scores it down and raises an
`ATTEST FLAG` in the dossier so nobody opens that conversation with money.

### Capacity

Headcount as a proxy for business-client count. Note the curve is not
monotonic — **31–75 heads scores highest (35), and 76+ drops back to 28.**
Large firms have real volume but also internal referral policies and
established national-bank relationships, which makes them harder to convert
than the tier below.

### Access

Inverse to headcount again. At a small firm the decision maker is the person
who answers LinkedIn.

---

## Transaction intermediaries

Business brokers, CRE brokers, equipment dealers.

Fit starts at a high baseline of 26 for a structural reason: **their deal dies
without financing**. There is no client relationship to guard, because you are
the reason they get paid. That absence of relationship-guarding is why this is
the fastest channel to a first funded deal — which matters disproportionately,
since credit unions and CPAs both open by asking who else you work with.

Capacity keys off active listings; Access is a flat 22, because brokers answer
LinkedIn — it is a prospecting channel for them too.

---

## Warm introduction: +10

Applied across every type, after the three axes. A credible introduction path
outweighs any firmographic signal in this business, and the bonus is large
enough to lift a B-tier partner with a mutual connection above a cold A-tier
one. Record it with `touch --warm "mutual: Dave R. at the chamber"`.

---

## Tuning

All weights are constants at the top of `prospector/scoring.py`. After changing
any of them, run `prospect.py rescore` to re-run the whole database.

The weights are a starting hypothesis, not a finding. Once 20–30 partners have
been worked, compare tier against what actually converted and adjust. The most
likely early correction is that Access is underweighted at 25 — cold
institutional outreach without an introduction converts worse than firmographics
suggest.
