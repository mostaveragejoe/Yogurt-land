"""Self-tests. Run: python3 -m unittest test_prospector -v"""

import json
import tempfile
import unittest
from pathlib import Path

from prospector import db, ingest_csv, ingest_ncua
from prospector.models import Partner, PartnerType, Stage
from prospector.scoring import cap_pressure, mbl_cap, score_partner


def cu(**kw):
    base = dict(id="t", name="Test CU", partner_type=PartnerType.CREDIT_UNION.value)
    base.update(kw)
    return Partner(**base)


class CapMath(unittest.TestCase):
    def test_asset_ratio_binds_for_well_capitalized(self):
        # 12.25% of 500M = 61.25M; 1.75 x 55M = 96.25M -> assets bind
        self.assertAlmostEqual(mbl_cap(500e6, 55e6), 61.25e6)

    def test_net_worth_binds_for_thin_capital(self):
        # 1.75 x 30M = 52.5M < 61.25M -> net worth binds
        self.assertAlmostEqual(mbl_cap(500e6, 30e6), 52.5e6)

    def test_falls_back_to_assets_without_net_worth(self):
        self.assertAlmostEqual(mbl_cap(500e6, None), 61.25e6)

    def test_no_assets_gives_no_cap(self):
        self.assertIsNone(mbl_cap(None, 10e6))

    def test_pressure_is_none_without_loan_figure(self):
        self.assertIsNone(cap_pressure(cu(total_assets=500e6, net_worth=55e6)))


class DepositoryScoring(unittest.TestCase):
    def test_cap_pressed_scores_max_fit(self):
        p = score_partner(cu(total_assets=500e6, net_worth=55e6,
                             business_loans_outstanding=57e6))
        self.assertEqual(p.fit_score, 40.0)
        self.assertEqual(p.tier, "A")

    def test_unpressed_scores_low_fit(self):
        p = score_partner(cu(total_assets=500e6, net_worth=55e6,
                             business_loans_outstanding=5e6))
        self.assertEqual(p.fit_score, 8.0)

    def test_access_penalizes_size(self):
        small = score_partner(cu(id="s", total_assets=200e6, net_worth=22e6,
                                 business_loans_outstanding=23e6))
        huge = score_partner(cu(id="h", total_assets=9e9, net_worth=900e6,
                                business_loans_outstanding=1.05e9))
        self.assertGreater(small.access_score, huge.access_score)

    def test_midsize_pressed_beats_giant_pressed(self):
        """The model's central claim: reachability outweighs raw volume."""
        mid = score_partner(cu(id="m", total_assets=486e6, net_worth=53.4e6,
                               business_loans_outstanding=55.9e6,
                               business_loan_count=214))
        giant = score_partner(cu(id="g", total_assets=3.85e9, net_worth=431e6,
                                 business_loans_outstanding=402e6,
                                 business_loan_count=1180))
        self.assertGreater(mid.total_score, giant.total_score)

    def test_low_income_designation_caps_fit(self):
        p = score_partner(cu(total_assets=500e6, net_worth=55e6,
                             business_loans_outstanding=57e6,
                             low_income_designated=True))
        self.assertEqual(p.fit_score, 14.0)
        self.assertTrue(any("exempt" in r for r in p.score_rationale))

    def test_zero_business_lending_is_its_own_case(self):
        p = score_partner(cu(total_assets=500e6, net_worth=55e6,
                             business_loans_outstanding=0))
        self.assertEqual(p.fit_score, 10.0)

    def test_warm_intro_adds_bonus(self):
        cold = score_partner(cu(id="c", total_assets=200e6, net_worth=22e6,
                                business_loans_outstanding=10e6))
        warm = score_partner(cu(id="w", total_assets=200e6, net_worth=22e6,
                                business_loans_outstanding=10e6,
                                warm_intro_path="mutual: Dave R."))
        self.assertEqual(warm.total_score - cold.total_score, 10.0)

    def test_do_not_contact_zeroes_out(self):
        p = score_partner(cu(total_assets=500e6, net_worth=55e6,
                             business_loans_outstanding=57e6, do_not_contact=True))
        self.assertEqual(p.total_score, 0.0)
        self.assertEqual(p.tier, "X")

    def test_score_never_exceeds_100(self):
        p = score_partner(cu(total_assets=200e6, net_worth=22e6,
                             business_loans_outstanding=26e6,
                             business_loan_count=900,
                             warm_intro_path="mutual"))
        self.assertLessEqual(p.total_score, 100.0)


class CpaScoring(unittest.TestCase):
    def test_attest_work_is_penalized_and_flagged(self):
        plain = score_partner(Partner(id="a", name="A", partner_type="cpa_firm",
                                      headcount=20, does_attest_work=False))
        attest = score_partner(Partner(id="b", name="B", partner_type="cpa_firm",
                                       headcount=20, does_attest_work=True))
        self.assertEqual(plain.fit_score - attest.fit_score, 8.0)
        self.assertTrue(any("ATTEST FLAG" in r for r in attest.score_rationale))

    def test_matching_specialty_raises_fit(self):
        generic = score_partner(Partner(id="g", name="G", partner_type="cpa_firm",
                                        headcount=20))
        niche = score_partner(Partner(id="n", name="N", partner_type="cpa_firm",
                                      headcount=20,
                                      industry_specialties=["construction", "trucking"]))
        self.assertGreater(niche.fit_score, generic.fit_score)

    def test_capacity_curve_is_not_monotonic(self):
        """Very large firms are harder to convert than mid-size ones."""
        mid = score_partner(Partner(id="m", name="M", partner_type="cpa_firm", headcount=50))
        huge = score_partner(Partner(id="h", name="H", partner_type="cpa_firm", headcount=200))
        self.assertGreater(mid.capacity_score, huge.capacity_score)

    def test_specialty_drives_product_match(self):
        p = score_partner(Partner(id="d", name="D", partner_type="cpa_firm",
                                  headcount=10, industry_specialties=["dental"]))
        self.assertIn("medical_working_capital", p.products_matched)


class Persistence(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def test_roundtrip_preserves_lists(self):
        p = score_partner(Partner(id="x", name="X", partner_type="cpa_firm",
                                  industry_specialties=["dental", "medical"]))
        db.upsert(self.conn, p)
        back = db.get(self.conn, "x")
        self.assertEqual(back.industry_specialties, ["dental", "medical"])

    def test_counts_round_trip_as_ints(self):
        db.upsert(self.conn, cu(id="c", total_assets=1e8, business_loan_count=214))
        self.assertIsInstance(db.get(self.conn, "c").business_loan_count, int)

    def test_reingest_preserves_hand_entered_fields(self):
        """The behavior that matters over months of use."""
        db.upsert(self.conn, cu(id="c", total_assets=1e8,
                                business_loans_outstanding=5e6))
        db.update_fields(self.conn, "c", stage=Stage.CONTACTED.value,
                         notes="talked to CLO", contact_name="Dana W.")
        # Fresh quarter of NCUA data arrives with updated metrics:
        db.upsert(self.conn, cu(id="c", total_assets=1.2e8,
                                business_loans_outstanding=9e6))
        back = db.get(self.conn, "c")
        self.assertEqual(back.stage, Stage.CONTACTED.value)
        self.assertEqual(back.notes, "talked to CLO")
        self.assertEqual(back.contact_name, "Dana W.")
        self.assertEqual(back.total_assets, 1.2e8)   # metrics did update

    def test_do_not_contact_survives_reingest(self):
        db.upsert(self.conn, cu(id="c", total_assets=1e8))
        db.update_fields(self.conn, "c", do_not_contact=1)
        db.upsert(self.conn, cu(id="c", total_assets=1e8))
        self.assertTrue(db.get(self.conn, "c").do_not_contact)


class NcuaIngest(unittest.TestCase):
    def test_resolves_shouty_headers(self):
        cols = ingest_ncua.resolve_columns(
            ["CU_NUMBER", "CU_NAME", "CITY", "STATE", "TOTAL_ASSETS",
             "TOTAL_NET_WORTH", "TOTAL_COMMERCIAL_LOANS"])
        self.assertEqual(cols["name"], "CU_NAME")
        self.assertEqual(cols["total_assets"], "TOTAL_ASSETS")
        self.assertEqual(cols["net_worth"], "TOTAL_NET_WORTH")

    def test_overrides_win(self):
        cols = ingest_ncua.resolve_columns(
            ["Name", "Assets", "ACCT_997"], overrides={"net_worth": ["ACCT_997"]})
        self.assertEqual(cols["net_worth"], "ACCT_997")

    def test_sample_file_loads_and_filters_state(self):
        partners, diag = ingest_ncua.load("data/sample_ncua_mn.csv", state="MN")
        self.assertEqual(len(partners), 15)
        self.assertEqual(diag["columns_unmatched"], [])
        self.assertTrue(all(p.state == "MN" for p in partners))

    def test_missing_required_column_fails_loudly(self):
        with tempfile.NamedTemporaryFile("w", suffix=".csv", delete=False) as fh:
            fh.write("foo,bar\n1,2\n")
            path = fh.name
        with self.assertRaises(SystemExit):
            ingest_ncua.load(path)


class CsvIngest(unittest.TestCase):
    def test_sample_file_loads_mixed_types(self):
        partners, diag = ingest_csv.load("data/sample_cpas_mn.csv")
        self.assertEqual(diag["imported"], 10)
        self.assertEqual(diag["unrecognized_types"], [])
        self.assertIn("business_broker", {p.partner_type for p in partners})

    def test_specialties_split_on_pipe_and_comma(self):
        with tempfile.NamedTemporaryFile("w", suffix=".csv", delete=False) as fh:
            fh.write("name,industry_specialties\nA,construction|dental\nB,\"trucking, ag\"\n")
            path = fh.name
        partners, _ = ingest_csv.load(path)
        self.assertEqual(partners[0].industry_specialties, ["construction", "dental"])
        self.assertEqual(partners[1].industry_specialties, ["trucking", "ag"])

    def test_rows_without_names_are_skipped_not_crashed(self):
        with tempfile.NamedTemporaryFile("w", suffix=".csv", delete=False) as fh:
            fh.write("name,city\n,Minneapolis\nReal Firm,Saint Paul\n")
            path = fh.name
        partners, diag = ingest_csv.load(path)
        self.assertEqual(diag["imported"], 1)
        self.assertEqual(diag["skipped_no_name"], 1)


if __name__ == "__main__":
    unittest.main()


# ---------------------------------------------------------------------------
# Follow-up cadence
# ---------------------------------------------------------------------------

import datetime as dt
from prospector import cadence


class BusinessDays(unittest.TestCase):
    def test_friday_plus_one_is_monday(self):
        self.assertEqual(cadence.add_business_days(dt.date(2026, 8, 28), 1),
                         dt.date(2026, 8, 31))

    def test_friday_plus_five_is_next_friday(self):
        self.assertEqual(cadence.add_business_days(dt.date(2026, 8, 28), 5),
                         dt.date(2026, 9, 4))

    def test_zero_days_is_a_noop(self):
        self.assertEqual(cadence.add_business_days(dt.date(2026, 8, 28), 0),
                         dt.date(2026, 8, 28))

    def test_due_dates_never_land_on_a_weekend(self):
        start = dt.date(2026, 8, 24)
        for days in range(1, 40):
            self.assertLess(cadence.add_business_days(start, days).weekday(), 5)


class TaxSeason(unittest.TestCase):
    def test_february_is_tax_season(self):
        self.assertTrue(cadence.in_tax_season(dt.date(2027, 2, 10)))

    def test_august_is_not(self):
        self.assertFalse(cadence.in_tax_season(dt.date(2026, 8, 27)))

    def test_cpa_follow_up_is_pushed_past_april(self):
        due = cadence.next_due("contacted", PartnerType.CPA_FIRM.value,
                               dt.date(2027, 1, 20))
        self.assertEqual(due, "2027-04-20")

    def test_credit_union_is_not_deferred(self):
        due = cadence.next_due("contacted", PartnerType.CREDIT_UNION.value,
                               dt.date(2027, 1, 20))
        self.assertEqual(due, "2027-01-27")

    def test_cpa_is_not_stale_during_tax_season(self):
        self.assertFalse(cadence.is_stale("contacted", "2027-01-05",
                                          PartnerType.CPA_FIRM.value,
                                          dt.date(2027, 2, 20)))

    def test_credit_union_is_stale_after_the_same_silence(self):
        self.assertTrue(cadence.is_stale("contacted", "2026-08-01",
                                         PartnerType.CREDIT_UNION.value,
                                         dt.date(2026, 8, 27)))


class Parking(unittest.TestCase):
    def test_weak_prospect_is_parked(self):
        self.assertTrue(cadence.should_park(3, "D"))

    def test_strong_prospect_is_not_parked(self):
        """You exhausted one contact, not the institution."""
        self.assertFalse(cadence.should_park(3, "A"))
        self.assertFalse(cadence.should_park(5, "B"))

    def test_no_parking_below_the_ceiling(self):
        self.assertFalse(cadence.should_park(2, "D"))

    def test_strong_prospect_is_told_to_try_another_contact(self):
        action = cadence.suggest_action("contacted", 3,
                                        PartnerType.CREDIT_UNION.value, "A")
        self.assertIn("different contact", action)


class Overdue(unittest.TestCase):
    def test_past_due_is_positive(self):
        self.assertEqual(cadence.days_overdue("2026-08-20", dt.date(2026, 8, 27)), 7)

    def test_future_is_negative(self):
        self.assertLess(cadence.days_overdue("2026-09-05", dt.date(2026, 8, 27)), 0)

    def test_empty_due_date_is_not_overdue(self):
        self.assertEqual(cadence.days_overdue("", dt.date(2026, 8, 27)), 0)

    def test_unanswered_reply_outranks_a_later_cold_prospect(self):
        replied = cadence.priority(60.0, 2, Stage.RESPONDED.value)
        cold = cadence.priority(90.0, 5, Stage.CONTACTED.value)
        self.assertGreater(replied, cold)


class EventLog(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")
        db.upsert(self.conn, cu(id="c", total_assets=1e8))

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def test_unanswered_counts_outbound_since_last_reply(self):
        db.add_event(self.conn, "c", "messaged", "2026-08-01")
        db.add_event(self.conn, "c", "nudge", "2026-08-05")
        self.assertEqual(db.unanswered_touches(self.conn, "c"), 2)

    def test_a_reply_resets_the_count(self):
        db.add_event(self.conn, "c", "messaged", "2026-08-01")
        db.add_event(self.conn, "c", "nudge", "2026-08-05")
        db.add_event(self.conn, "c", "replied", "2026-08-06")
        self.assertEqual(db.unanswered_touches(self.conn, "c"), 0)

    def test_notes_do_not_count_as_touches(self):
        db.add_event(self.conn, "c", "messaged", "2026-08-01")
        db.add_event(self.conn, "c", "note", "2026-08-02")
        self.assertEqual(db.unanswered_touches(self.conn, "c"), 1)

    def test_events_come_back_in_order(self):
        db.add_event(self.conn, "c", "messaged", "2026-08-01", "first")
        db.add_event(self.conn, "c", "replied", "2026-08-04", "second")
        kinds = [e["kind"] for e in db.events_for(self.conn, "c")]
        self.assertEqual(kinds, ["messaged", "replied"])


class FuzzyLookup(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")
        for pid, name in [("cu-1", "Northgate Community CU"),
                          ("cu-2", "Northern Light Community CU"),
                          ("cpa-1", "Halvorsen & Reed CPAs")]:
            db.upsert(self.conn, Partner(id=pid, name=name,
                                         partner_type="credit_union"))

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def test_name_fragment_resolves(self):
        found = db.find(self.conn, "northgate")
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0].id, "cu-1")

    def test_exact_id_still_works(self):
        self.assertEqual(db.find(self.conn, "cpa-1")[0].name, "Halvorsen & Reed CPAs")

    def test_ambiguous_fragment_returns_all_candidates(self):
        self.assertEqual(len(db.find(self.conn, "community")), 2)

    def test_no_match_returns_empty(self):
        self.assertEqual(db.find(self.conn, "zzzz"), [])


# ---------------------------------------------------------------------------
# Outcome tracking and calibration
# ---------------------------------------------------------------------------

from prospector import analytics


class WilsonInterval(unittest.TestCase):
    def test_point_estimate_is_the_raw_proportion(self):
        point, _, _ = analytics.wilson(3, 8)
        self.assertAlmostEqual(point, 0.375)

    def test_interval_brackets_the_estimate(self):
        point, low, high = analytics.wilson(3, 8)
        self.assertLess(low, point)
        self.assertGreater(high, point)

    def test_small_samples_give_wide_intervals(self):
        """The entire reason Wilson was chosen over a raw percentage."""
        _, small_low, small_high = analytics.wilson(3, 8)
        _, big_low, big_high = analytics.wilson(30, 80)
        self.assertGreater(small_high - small_low, (big_high - big_low) * 2)

    def test_zero_successes_still_has_an_upper_bound(self):
        point, low, high = analytics.wilson(0, 5)
        self.assertEqual(point, 0.0)
        self.assertEqual(low, 0.0)
        self.assertGreater(high, 0.3)      # 0/5 does not mean "never"

    def test_all_successes_stays_within_bounds(self):
        _, low, high = analytics.wilson(5, 5)
        self.assertLessEqual(high, 1.0)
        self.assertLess(low, 1.0)

    def test_no_trials_is_not_a_crash(self):
        self.assertEqual(analytics.wilson(0, 0), (0.0, 0.0, 0.0))


class Gating(unittest.TestCase):
    """The tool must refuse to declare winners on thin data."""

    def _stats(self, spec):
        stats = {}
        for channel, worked, producing, deals in spec:
            st = analytics.ChannelStats(channel)
            st.total = st.worked = worked
            st.producing = producing
            st.deals = deals
            stats[channel] = st
        return stats

    def test_too_few_deals_blocks_ranking(self):
        ok, reason = analytics.can_rank_channels(
            self._stats([("credit_union", 20, 2, 2), ("cpa_firm", 20, 1, 1)]))
        self.assertFalse(ok)
        self.assertIn("Need about", reason)

    def test_one_eligible_channel_blocks_ranking(self):
        ok, reason = analytics.can_rank_channels(
            self._stats([("credit_union", 20, 8, 12), ("cpa_firm", 3, 1, 1)]))
        self.assertFalse(ok)
        self.assertIn("Need two", reason)

    def test_sufficient_data_allows_ranking(self):
        ok, _ = analytics.can_rank_channels(
            self._stats([("credit_union", 20, 8, 10), ("cpa_firm", 20, 2, 3)]))
        self.assertTrue(ok)

    def test_thin_channel_reports_no_rate(self):
        st = analytics.ChannelStats("cpa_firm")
        st.worked = 3
        self.assertFalse(st.has_enough_for_rate)


class ChannelAggregation(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def _partner(self, pid, ptype, stage):
        p = score_partner(Partner(id=pid, name=pid, partner_type=ptype, stage=stage))
        db.upsert(self.conn, p)
        db.update_fields(self.conn, pid, stage=stage)
        return db.get(self.conn, pid)

    def test_untouched_partners_do_not_dilute_the_denominator(self):
        """A list of 500 unworked prospects must not read as a 0% channel."""
        worked = self._partner("a", "credit_union", Stage.CONTACTED.value)
        self._partner("b", "credit_union", Stage.NOT_CONTACTED.value)
        stats = analytics.channel_stats([worked, db.get(self.conn, "b")],
                                        {}, {})
        self.assertEqual(stats["credit_union"].total, 2)
        self.assertEqual(stats["credit_union"].worked, 1)

    def test_producing_counts_partners_not_deals(self):
        p = self._partner("a", "credit_union", Stage.PRODUCING.value)
        deals = {"a": [{"status": "funded", "amount": 100.0, "revenue": 3.0,
                        "referred_date": "2026-05-01"},
                       {"status": "funded", "amount": 200.0, "revenue": 6.0,
                        "referred_date": "2026-06-01"}]}
        stats = analytics.channel_stats([p], deals, {"a": "2026-04-01"})
        st = stats["credit_union"]
        self.assertEqual(st.producing, 1)
        self.assertEqual(st.deals, 2)
        self.assertEqual(st.revenue, 9.0)

    def test_only_funded_deals_count_toward_revenue(self):
        p = self._partner("a", "credit_union", Stage.PRODUCING.value)
        deals = {"a": [{"status": "declined", "amount": 500.0, "revenue": None,
                        "referred_date": "2026-05-01"}]}
        stats = analytics.channel_stats([p], deals, {"a": "2026-04-01"})
        self.assertEqual(stats["credit_union"].funded, 0)
        self.assertEqual(stats["credit_union"].revenue, 0.0)

    def test_time_to_first_deal_measures_from_first_contact(self):
        p = self._partner("a", "credit_union", Stage.PRODUCING.value)
        deals = {"a": [{"status": "funded", "amount": 1.0, "revenue": 1.0,
                        "referred_date": "2026-05-01"}]}
        stats = analytics.channel_stats([p], deals, {"a": "2026-04-01"})
        self.assertEqual(stats["credit_union"].median_days_to_deal, 30)


class Calibration(unittest.TestCase):
    def _rows(self, spec):
        rows = {}
        for tier, worked, producing, deals in spec:
            r = analytics.TierRow(tier)
            r.worked, r.producing, r.deals = worked, producing, deals
            rows[tier] = r
        return rows

    def test_thin_data_blocks_calibration(self):
        ok, reason = analytics.can_calibrate(self._rows([("A", 3, 1, 1)]))
        self.assertFalse(ok)
        self.assertIn("at least two tiers", reason)

    def test_enough_data_allows_calibration(self):
        ok, _ = analytics.can_calibrate(
            self._rows([("A", 20, 8, 8), ("D", 20, 1, 1)]))
        self.assertTrue(ok)

    def test_clear_separation_is_reported(self):
        rows = self._rows([("A", 40, 24, 30), ("D", 40, 1, 1)])
        self.assertIn("separated", analytics.calibration_verdict(rows))

    def test_overlapping_intervals_report_no_separation(self):
        rows = self._rows([("A", 10, 3, 3), ("D", 10, 2, 2)])
        verdict = analytics.calibration_verdict(rows)
        self.assertIn("NOT separated", verdict)
        self.assertIn("Do not re-weight", verdict)


class DealRecords(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")
        db.upsert(self.conn, cu(id="c", total_assets=1e8))

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def test_new_deal_starts_as_referred(self):
        did = db.add_deal(self.conn, "c", "sba_loans", "2026-05-01", amount=250000)
        self.assertEqual(db.get_deal(self.conn, did)["status"], "referred")

    def test_funding_sets_a_close_date(self):
        did = db.add_deal(self.conn, "c", "sba_loans", "2026-05-01")
        db.update_deal(self.conn, did, "funded", revenue=7500.0)
        deal = db.get_deal(self.conn, did)
        self.assertEqual(deal["revenue"], 7500.0)
        self.assertTrue(deal["closed_date"])

    def test_referred_status_does_not_set_a_close_date(self):
        did = db.add_deal(self.conn, "c", "sba_loans", "2026-05-01")
        db.update_deal(self.conn, did, "underwriting")
        self.assertIsNone(db.get_deal(self.conn, did)["closed_date"])

    def test_updating_a_missing_deal_fails_cleanly(self):
        self.assertFalse(db.update_deal(self.conn, 999, "funded"))

    def test_first_contact_ignores_non_outbound_events(self):
        db.add_event(self.conn, "c", "note", "2026-01-01")
        db.add_event(self.conn, "c", "messaged", "2026-03-15")
        self.assertEqual(db.first_contact_date(self.conn, "c"), "2026-03-15")

    def test_filtering_by_status(self):
        db.add_deal(self.conn, "c", "sba_loans", "2026-05-01")
        did = db.add_deal(self.conn, "c", "equipment_leasing", "2026-05-02")
        db.update_deal(self.conn, did, "funded")
        self.assertEqual(len(db.all_deals(self.conn, status="funded")), 1)


class ProducerRanking(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def test_agreed_but_silent_partners_are_included(self):
        """The maintenance cost you are deciding whether to keep paying."""
        p = Partner(id="a", name="Silent", partner_type="cpa_firm",
                    stage=Stage.AGREEMENT.value)
        rows = analytics.producer_stats([p], {}, {})
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0].deals, 0)

    def test_unworked_partners_are_excluded(self):
        p = Partner(id="a", name="Cold", partner_type="cpa_firm",
                    stage=Stage.NOT_CONTACTED.value)
        self.assertEqual(analytics.producer_stats([p], {}, {}), [])

    def test_rows_sort_by_revenue(self):
        small = Partner(id="s", name="Small", partner_type="cpa_firm",
                        stage=Stage.PRODUCING.value)
        big = Partner(id="b", name="Big", partner_type="cpa_firm",
                      stage=Stage.PRODUCING.value)
        deals = {
            "s": [{"status": "funded", "amount": 100.0, "revenue": 5.0,
                   "referred_date": "2026-05-01"}],
            "b": [{"status": "funded", "amount": 900.0, "revenue": 50.0,
                   "referred_date": "2026-05-01"}],
        }
        rows = analytics.producer_stats([small, big], deals, {})
        self.assertEqual(rows[0].partner.id, "b")


# ---------------------------------------------------------------------------
# Regression tests -- each pins a bug found and fixed during the audit round
# ---------------------------------------------------------------------------

from prospector import report
from prospector.models import ID_PREFIX
from prospector.report import _money


class IdCollisionRegression(unittest.TestCase):
    """Partner ids were built from partner_type[:3] plus the name alone.

    Two failure modes, both silent: credit_union[:3] and cre_broker[:3] are
    both "cre", and two firms sharing a name (ordinary in real data) produced
    one id. The second record overwrote the first on import.
    """

    def test_credit_union_and_cre_broker_prefixes_differ(self):
        self.assertNotEqual(ID_PREFIX[PartnerType.CREDIT_UNION.value],
                            ID_PREFIX[PartnerType.CRE_BROKER.value])

    def test_every_partner_type_has_a_unique_prefix(self):
        prefixes = list(ID_PREFIX.values())
        self.assertEqual(len(prefixes), len(set(prefixes)))

    def test_every_partner_type_is_covered(self):
        for ptype in PartnerType:
            self.assertIn(ptype.value, ID_PREFIX)

    def test_same_name_different_city_gets_distinct_ids(self):
        taken = set()
        a = ingest_csv.make_id("cpa_firm", "Smith & Associates", "Minneapolis", taken)
        b = ingest_csv.make_id("cpa_firm", "Smith & Associates", "Duluth", taken)
        self.assertNotEqual(a, b)

    def test_same_name_same_city_falls_back_to_a_counter(self):
        taken = set()
        ids = [ingest_csv.make_id("cpa_firm", "Duplicate Firm", "Eagan", taken)
               for _ in range(3)]
        self.assertEqual(len(set(ids)), 3)

    def test_same_name_different_type_gets_distinct_ids(self):
        taken = set()
        a = ingest_csv.make_id("credit_union", "Lakeside Partners", "Rochester", taken)
        b = ingest_csv.make_id("cre_broker", "Lakeside Partners", "Rochester", taken)
        self.assertNotEqual(a, b)

    def test_import_preserves_every_row(self):
        with tempfile.NamedTemporaryFile("w", suffix=".csv", delete=False) as fh:
            fh.write("name,partner_type,city\n"
                     "Smith & Associates,cpa_firm,Minneapolis\n"
                     "Smith & Associates,cpa_firm,Duluth\n"
                     "Lakeside Partners,credit_union,Rochester\n"
                     "Lakeside Partners,cre_broker,Rochester\n")
            path = fh.name
        partners, diag = ingest_csv.load(path)
        self.assertEqual(diag["imported"], 4)
        self.assertEqual(len({p.id for p in partners}), 4)


class TouchSchedulesFollowUpRegression(unittest.TestCase):
    """`touch --stage` set a stage without scheduling a next action.

    The partner then had no due date and never appeared in `due` -- silently
    dropping out of the pipeline while looking correctly updated.
    """

    def test_stage_change_implies_a_due_date(self):
        for stage in (Stage.CONTACTED.value, Stage.RESPONDED.value,
                      Stage.AGREEMENT.value):
            due = cadence.next_due(stage, PartnerType.CREDIT_UNION.value,
                                   dt.date(2026, 8, 27))
            self.assertTrue(due, f"{stage} produced no due date")

    def test_dead_stage_correctly_has_no_due_date(self):
        self.assertEqual(
            cadence.next_due(Stage.DEAD.value, PartnerType.CREDIT_UNION.value,
                             dt.date(2026, 8, 27)), "")


class MoneyFormatRegression(unittest.TestCase):
    """A millions-only formatter rendered $1.24B as "$1,240.00M"."""

    def test_billions_use_a_b_suffix(self):
        self.assertEqual(_money(1_240_000_000), "$1.24B")

    def test_millions_stay_readable(self):
        self.assertEqual(_money(486_000_000), "$486.0M")
        self.assertEqual(_money(2_950_000), "$2.95M")

    def test_thousands_and_units(self):
        self.assertEqual(_money(86_000), "$86k")
        self.assertEqual(_money(540), "$540")

    def test_empty_values_render_as_a_dash(self):
        self.assertEqual(_money(None), "--")
        self.assertEqual(_money(0), "--")

    def test_output_never_exceeds_the_column_width(self):
        for value in (1e3, 1e6, 1e9, 9.8e9, 4.86e8, 2.95e6):
            self.assertLessEqual(len(_money(value)), 10)


class EmptyDatabaseRegression(unittest.TestCase):
    """`worklist` claimed every partner was worked when there were none."""

    def test_empty_database_says_so(self):
        message = report.worklist([])
        self.assertIn("No partners in the database", message)

    def test_all_worked_is_a_different_message(self):
        p = Partner(id="a", name="A", partner_type="cpa_firm",
                    stage=Stage.CONTACTED.value)
        message = report.worklist([p])
        self.assertIn("Nothing unworked", message)


# ---------------------------------------------------------------------------
# Contacts -- people inside an institution
# ---------------------------------------------------------------------------

class ContactRouting(unittest.TestCase):
    """Silence is a property of a person, not of the institution."""

    def _people(self, n=2):
        return [{"name": f"Person {i}", "title": "CEO"} for i in range(n)]

    def test_below_the_ceiling_nothing_changes(self):
        disposition, _ = cadence.route_after_silence(2, "A", self._people())
        self.assertEqual(disposition, "continue")

    def test_alternates_available_means_switch_not_park(self):
        disposition, why = cadence.route_after_silence(3, "A", self._people())
        self.assertEqual(disposition, "switch_contact")
        self.assertIn("institution is not", why)

    def test_weak_partner_with_alternates_still_switches(self):
        """A free shot at another person is worth taking regardless of tier."""
        disposition, _ = cadence.route_after_silence(3, "D", self._people())
        self.assertEqual(disposition, "switch_contact")

    def test_strong_partner_with_no_alternates_seeks_one(self):
        disposition, why = cadence.route_after_silence(3, "A", [])
        self.assertEqual(disposition, "find_contact")
        self.assertIn("worth finding one", why)

    def test_weak_partner_with_no_alternates_parks(self):
        disposition, _ = cadence.route_after_silence(3, "D", [])
        self.assertEqual(disposition, "park_partner")

    def test_switch_message_names_the_people(self):
        _, why = cadence.route_after_silence(3, "A", self._people(2))
        self.assertIn("Person 0", why)
        self.assertIn("Person 1", why)


class ContactRecords(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")
        db.upsert(self.conn, cu(id="c", total_assets=1e8))

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def test_new_contacts_start_untried(self):
        cid = db.add_contact(self.conn, "c", "Dana Whitfield", title="CLO")
        self.assertEqual(db.get_contact(self.conn, cid)["status"], "untried")

    def test_primary_sorts_first(self):
        db.add_contact(self.conn, "c", "Second")
        db.add_contact(self.conn, "c", "First", is_primary=True)
        self.assertEqual(db.contacts_for(self.conn, "c")[0]["name"], "First")

    def test_untried_excludes_exhausted_statuses(self):
        live = db.add_contact(self.conn, "c", "Live")
        gone = db.add_contact(self.conn, "c", "Gone")
        db.update_contact(self.conn, gone, status="cold")
        remaining = db.untried_contacts(self.conn, "c")
        self.assertEqual([r["id"] for r in remaining], [live])

    def test_unanswered_is_counted_per_person(self):
        """Three touches each to two people is 3 apiece, not 6."""
        a = db.add_contact(self.conn, "c", "Person A")
        b = db.add_contact(self.conn, "c", "Person B")
        for _ in range(3):
            db.add_event(self.conn, "c", "messaged", "2026-06-01", contact_id=a)
        db.add_event(self.conn, "c", "messaged", "2026-06-01", contact_id=b)
        self.assertEqual(db.contact_unanswered(self.conn, a), 3)
        self.assertEqual(db.contact_unanswered(self.conn, b), 1)

    def test_a_reply_resets_that_persons_count_only(self):
        a = db.add_contact(self.conn, "c", "Person A")
        b = db.add_contact(self.conn, "c", "Person B")
        db.add_event(self.conn, "c", "messaged", "2026-06-01", contact_id=a)
        db.add_event(self.conn, "c", "messaged", "2026-06-01", contact_id=b)
        db.add_event(self.conn, "c", "replied", "2026-06-05", contact_id=a)
        self.assertEqual(db.contact_unanswered(self.conn, a), 0)
        self.assertEqual(db.contact_unanswered(self.conn, b), 1)

    def test_find_contact_by_name_fragment(self):
        cid = db.add_contact(self.conn, "c", "Dana Whitfield")
        self.assertEqual(db.find_contact(self.conn, "c", "dana")[0]["id"], cid)

    def test_find_contact_by_id(self):
        cid = db.add_contact(self.conn, "c", "Dana Whitfield")
        self.assertEqual(db.find_contact(self.conn, "c", str(cid))[0]["id"], cid)

    def test_contact_lookup_is_scoped_to_its_partner(self):
        """An id belonging to another partner must not resolve here."""
        db.upsert(self.conn, cu(id="other", total_assets=1e8))
        cid = db.add_contact(self.conn, "other", "Elsewhere")
        self.assertEqual(db.find_contact(self.conn, "c", str(cid)), [])

    def test_events_without_a_contact_still_work(self):
        """Partner-level logging predates contacts and must keep working."""
        db.add_event(self.conn, "c", "messaged", "2026-06-01")
        self.assertEqual(db.unanswered_touches(self.conn, "c"), 1)


class LegacyContactMigration(unittest.TestCase):
    """The single contact_name field lifts into a contacts row, once."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.path = Path(self.tmp.name) / "t.db"

    def tearDown(self):
        self.tmp.cleanup()

    def _seed_legacy(self):
        conn = db.connect(self.path)
        db.upsert(conn, cu(id="c", total_assets=1e8))
        db.update_fields(conn, "c", contact_name="Dana Whitfield",
                         contact_title="CLO")
        conn.commit()
        conn.close()

    def test_legacy_field_becomes_a_contact(self):
        self._seed_legacy()
        conn = db.connect(self.path)
        rows = db.contacts_for(conn, "c")
        conn.close()
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["name"], "Dana Whitfield")
        self.assertEqual(rows[0]["title"], "CLO")
        self.assertTrue(rows[0]["is_primary"])

    def test_migration_does_not_duplicate_on_reopen(self):
        self._seed_legacy()
        for _ in range(3):
            db.connect(self.path).close()
        conn = db.connect(self.path)
        rows = db.contacts_for(conn, "c")
        conn.close()
        self.assertEqual(len(rows), 1)

    def test_partners_without_a_legacy_contact_get_nothing(self):
        conn = db.connect(self.path)
        db.upsert(conn, cu(id="empty", total_assets=1e8))
        conn.commit()
        conn.close()
        conn = db.connect(self.path)
        rows = db.contacts_for(conn, "empty")
        conn.close()
        self.assertEqual(rows, [])


class NoContactRegression(unittest.TestCase):
    """`log` crashed on any partner with no contacts on file.

    The output path built a display name from the contact dict without
    checking it existed. That is the ordinary case before any contacts have
    been added -- i.e. every partner on day one. Unit tests missed it because
    it lived in the print path, not the data layer; the end-to-end sweep
    caught it.
    """

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")
        db.upsert(self.conn, cu(id="c", total_assets=1e8))

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def test_partner_with_no_contacts_has_no_auto_contact(self):
        self.assertEqual(db.untried_contacts(self.conn, "c"), [])

    def test_routing_still_works_with_no_contacts(self):
        disposition, why = cadence.route_after_silence(3, "A", [])
        self.assertEqual(disposition, "find_contact")
        self.assertTrue(why)

    def test_partner_level_events_are_counted_when_no_contact_exists(self):
        for _ in range(3):
            db.add_event(self.conn, "c", "messaged", "2026-06-01")
        self.assertEqual(db.unanswered_touches(self.conn, "c"), 3)

    def test_dossier_renders_without_contacts(self):
        partner = score_partner(db.get(self.conn, "c"))
        self.assertIn(partner.name, report.detail(partner, []))

    def test_roster_renders_without_contacts(self):
        partner = score_partner(db.get(self.conn, "c"))
        self.assertIn("No contacts on file",
                      report.contact_roster(partner, [], {}))


# ---------------------------------------------------------------------------
# NCUA import validation
# ---------------------------------------------------------------------------
# This is the tool's only route to real credit-union data and the least
# forgiving place to be wrong, because every failure mode here is silent.

from prospector import ingest_ncua


def _cu(name, assets, net_worth, loans):
    return Partner(id=name, name=name, partner_type="credit_union",
                   total_assets=assets, net_worth=net_worth,
                   business_loans_outstanding=loans)


def _codes(findings):
    return {f["code"] for f in findings}


def _errors(findings):
    return {f["code"] for f in findings if f["level"] == "error"}


class ScaleDetection(unittest.TestCase):
    """A units mismatch does not crash -- it silently corrupts the ranking.

    Cap pressure is a ratio and survives. Access scoring does not: every
    institution reads as tiny, takes the maximum reachability score, and the
    ordering shifts without any visible error.
    """

    def test_thousands_scaled_file_is_caught(self):
        partners = [_cu("A", 486_000, 53_400, 55_900),
                    _cu("B", 212_000, 19_800, 32_900)]
        self.assertIn("scale", _errors(ingest_ncua.validate(partners)))

    def test_dollar_scaled_file_passes(self):
        partners = [_cu("A", 486e6, 53.4e6, 55.9e6),
                    _cu("B", 212e6, 19.8e6, 32.9e6)]
        self.assertNotIn("scale", _codes(ingest_ncua.validate(partners)))

    def test_the_corruption_it_prevents_is_real(self):
        """Pin the actual damage, so the guard is never removed as noise."""
        correct = score_partner(_cu("A", 486e6, 53.4e6, 55.9e6))
        mis_scaled = score_partner(_cu("A", 486e3, 53.4e3, 55.9e3))
        self.assertAlmostEqual(cap_pressure(correct), cap_pressure(mis_scaled))
        self.assertNotEqual(correct.access_score, mis_scaled.access_score)
        self.assertNotEqual(correct.tier, mis_scaled.tier)

    def test_rescale_restores_the_original_figures(self):
        partners = [_cu("A", 486_000, 53_400, 55_900)]
        ingest_ncua.rescale(partners, ingest_ncua.UNIT_FACTORS["thousands"])
        self.assertEqual(partners[0].total_assets, 486_000_000)
        self.assertEqual(ingest_ncua.validate(partners), [])

    def test_rescale_leaves_missing_values_alone(self):
        partners = [_cu("A", 486_000, None, None)]
        ingest_ncua.rescale(partners, 1000.0)
        self.assertIsNone(partners[0].net_worth)


class ImpossibleValues(unittest.TestCase):
    def test_net_worth_above_assets_is_an_error(self):
        findings = ingest_ncua.validate([_cu("A", 53.4e6, 486e6, 10e6)])
        self.assertIn("net_worth_exceeds_assets", _errors(findings))

    def test_loans_above_assets_is_an_error(self):
        findings = ingest_ncua.validate([_cu("A", 100e6, 10e6, 400e6)])
        self.assertIn("loans_exceed_assets", _errors(findings))

    def test_negative_figures_are_an_error(self):
        findings = ingest_ncua.validate([_cu("A", -486e6, 53e6, 10e6)])
        self.assertIn("negative", _errors(findings))

    def test_a_healthy_file_produces_no_findings(self):
        self.assertEqual(ingest_ncua.validate([_cu("A", 486e6, 53.4e6, 55.9e6)]), [])


class CoverageChecks(unittest.TestCase):
    def test_missing_business_loans_is_an_error(self):
        """Cap pressure is the point of the import; without loans there is none."""
        findings = ingest_ncua.validate([_cu("A", 486e6, 53e6, None),
                                         _cu("B", 212e6, 20e6, None)])
        self.assertIn("missing_business_loans", _errors(findings))

    def test_mostly_missing_net_worth_is_a_warning_not_an_error(self):
        partners = [_cu(f"P{i}", 486e6, None, 55e6) for i in range(4)]
        findings = ingest_ncua.validate(partners)
        self.assertIn("missing_net_worth", _codes(findings))
        self.assertNotIn("missing_net_worth", _errors(findings))

    def test_a_few_missing_assets_warn_but_do_not_block(self):
        partners = [_cu(f"P{i}", 486e6, 53e6, 55e6) for i in range(9)]
        partners.append(_cu("gap", None, 53e6, 55e6))
        findings = ingest_ncua.validate(partners)
        self.assertIn("missing_assets", _codes(findings))
        self.assertNotIn("missing_assets", _errors(findings))

    def test_mostly_missing_assets_blocks(self):
        partners = [_cu("ok", 486e6, 53e6, 55e6),
                    _cu("a", None, 53e6, 55e6), _cu("b", None, 53e6, 55e6)]
        self.assertIn("missing_assets", _errors(ingest_ncua.validate(partners)))

    def test_no_rows_at_all_is_an_error(self):
        self.assertIn("empty", _errors(ingest_ncua.validate([])))


class InspectHelpers(unittest.TestCase):
    def test_sample_values_pair_headers_with_data(self):
        """Account codes like ACCT_010 are unreadable without their values."""
        pairs = dict(ingest_ncua.sample_values("data/sample_ncua_mn.csv", limit=2))
        self.assertIn("CU_NAME", pairs)
        self.assertEqual(pairs["CU_NAME"][0], "Northgate Community CU")

    def test_suggested_mapping_is_paste_ready(self):
        suggested = ingest_ncua.suggest_mapping("data/sample_ncua_mn.csv")
        self.assertEqual(suggested["total_assets"], ["TOTAL_ASSETS"])
        for value in suggested.values():
            self.assertIsInstance(value, list)

    def test_suggested_mapping_round_trips_as_an_override(self):
        suggested = ingest_ncua.suggest_mapping("data/sample_ncua_mn.csv")
        partners, diag = ingest_ncua.load("data/sample_ncua_mn.csv",
                                          overrides=suggested)
        self.assertEqual(len(partners), 15)
        self.assertEqual(diag["columns_unmatched"], [])


# ---------------------------------------------------------------------------
# Backup and restore
# ---------------------------------------------------------------------------

from prospector import backup, ingest_fdic, ingest_linkedin
from prospector.scoring import (construction_concentration, cre_concentration,
                                legal_lending_limit)


class BackupRestore(unittest.TestCase):
    """The database holds months of history no external source can rebuild."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.src = Path(self.tmp.name) / "src.db"
        self.dst = Path(self.tmp.name) / "dst.db"
        self.file = Path(self.tmp.name) / "backup.json"
        conn = db.connect(self.src)
        db.upsert(conn, score_partner(cu(id="c", total_assets=486e6,
                                         net_worth=53e6,
                                         business_loans_outstanding=55e6)))
        cid = db.add_contact(conn, "c", "Dana Whitfield", title="CLO",
                             is_primary=True)
        db.add_event(conn, "c", "messaged", "2026-06-01", "hello", contact_id=cid)
        did = db.add_deal(conn, "c", "sba_loans", "2026-07-01", amount=250000.0)
        db.update_deal(conn, did, "funded", revenue=8000.0)
        conn.commit()
        conn.close()

    def tearDown(self):
        self.tmp.cleanup()

    def _round_trip(self):
        conn = db.connect(self.src)
        counts = backup.write(conn, self.file)
        conn.close()
        target = db.connect(self.dst)
        payload = backup.read(self.file)
        restored = backup.restore(target, payload)
        return counts, restored, target

    def test_every_table_round_trips(self):
        counts, restored, target = self._round_trip()
        target.close()
        self.assertEqual(counts, restored)
        self.assertEqual(counts["partners"], 1)
        self.assertEqual(counts["contacts"], 1)
        self.assertEqual(counts["deals"], 1)

    def test_relationship_history_survives(self):
        _, _, target = self._round_trip()
        self.assertEqual(db.contacts_for(target, "c")[0]["name"], "Dana Whitfield")
        self.assertEqual(db.events_for(target, "c")[0]["note"], "hello")
        self.assertEqual(db.all_deals(target)[0]["revenue"], 8000.0)
        target.close()

    def test_contact_attribution_survives(self):
        _, _, target = self._round_trip()
        contact = db.contacts_for(target, "c")[0]
        self.assertEqual(db.contact_unanswered(target, contact["id"]), 1)
        target.close()

    def test_is_empty_distinguishes_a_fresh_database(self):
        fresh = db.connect(Path(self.tmp.name) / "fresh.db")
        self.assertTrue(backup.is_empty(fresh))
        fresh.close()
        conn = db.connect(self.src)
        self.assertFalse(backup.is_empty(conn))
        conn.close()

    def test_a_non_backup_file_is_rejected(self):
        bad = Path(self.tmp.name) / "bad.json"
        bad.write_text('{"something": 1}')
        with self.assertRaises(ValueError):
            backup.read(bad)

    def test_a_newer_format_version_is_rejected(self):
        newer = Path(self.tmp.name) / "newer.json"
        newer.write_text(json.dumps(
            {"format_version": backup.BACKUP_FORMAT_VERSION + 1, "tables": {}}))
        with self.assertRaises(ValueError):
            backup.read(newer)

    def test_unknown_columns_in_a_backup_are_dropped_not_fatal(self):
        conn = db.connect(self.src)
        payload = backup.dump(conn)
        conn.close()
        payload["tables"]["partners"][0]["a_column_since_removed"] = "x"
        target = db.connect(self.dst)
        self.assertIn("a_column_since_removed",
                      backup.dropped_columns(target, payload).get("partners", []))
        counts = backup.restore(target, payload)
        target.close()
        self.assertEqual(counts["partners"], 1)


# ---------------------------------------------------------------------------
# Identity reconciliation across sources
# ---------------------------------------------------------------------------

class CanonicalNames(unittest.TestCase):
    """NCUA keys on charter, LinkedIn only has a company name. Without
    reconciliation the contacts land on a scoreless stub."""

    def test_credit_union_spellings_reconcile(self):
        self.assertEqual(db.canonical_name("Northgate Community Credit Union"),
                         db.canonical_name("Northgate Community CU"))

    def test_fcu_reconciles(self):
        self.assertEqual(db.canonical_name("Lakeshore FCU"),
                         db.canonical_name("Lakeshore Federal Credit Union"))

    def test_ampersand_and_the_word_and_reconcile(self):
        self.assertEqual(db.canonical_name("Halvorsen & Reed CPAs"),
                         db.canonical_name("Halvorsen and Reed CPAs"))

    def test_legal_suffixes_are_ignored(self):
        self.assertEqual(db.canonical_name("Acme Holdings LLC"),
                         db.canonical_name("Acme Holdings"))

    def test_genuinely_different_names_do_not_reconcile(self):
        self.assertNotEqual(db.canonical_name("Northgate Community CU"),
                            db.canonical_name("Southgate Community CU"))


class Reconciliation(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")
        db.upsert(self.conn, Partner(id="cu-90001", name="Northgate Community CU",
                                     partner_type="credit_union",
                                     total_assets=486e6))

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def test_an_incoming_partner_adopts_the_existing_id(self):
        incoming = [Partner(id="cu-northgate-community-credit-union",
                            name="Northgate Community Credit Union",
                            partner_type="credit_union")]
        reconciled, remapped = db.reconcile_ids(self.conn, incoming)
        self.assertEqual(len(remapped), 1)
        self.assertEqual(reconciled[0].id, "cu-90001")

    def test_matching_is_scoped_by_partner_type(self):
        """A brokerage must never merge into a similarly named credit union."""
        incoming = [Partner(id="creb-northgate-community",
                            name="Northgate Community CU",
                            partner_type="cre_broker")]
        _, remapped = db.reconcile_ids(self.conn, incoming)
        self.assertEqual(remapped, {})

    def test_an_unrelated_partner_keeps_its_id(self):
        incoming = [Partner(id="cu-other", name="Southgate Members CU",
                            partner_type="credit_union")]
        reconciled, remapped = db.reconcile_ids(self.conn, incoming)
        self.assertEqual(remapped, {})
        self.assertEqual(reconciled[0].id, "cu-other")


# ---------------------------------------------------------------------------
# LinkedIn Sales Navigator import
# ---------------------------------------------------------------------------

class LinkedInTypeInference(unittest.TestCase):
    def test_recognises_each_institution_type(self):
        cases = {
            "Northgate Community Credit Union": PartnerType.CREDIT_UNION.value,
            "Lakeshore FCU": PartnerType.CREDIT_UNION.value,
            "First National Bank of Eagan": PartnerType.COMMUNITY_BANK.value,
            "Halvorsen & Reed CPAs": PartnerType.CPA_FIRM.value,
            "Kessler CPA": PartnerType.CPA_FIRM.value,
            "Summit Accounting Group": PartnerType.CPA_FIRM.value,
            "Northstar Business Brokers": PartnerType.BUSINESS_BROKER.value,
            "Twin Ports Commercial Realty": PartnerType.CRE_BROKER.value,
            "Heartland Equipment Sales": PartnerType.EQUIPMENT_DEALER.value,
            "Bell & Voigt Attorneys": PartnerType.ATTORNEY.value,
        }
        for name, expected in cases.items():
            ptype, confident = ingest_linkedin.infer_type(name)
            self.assertTrue(confident, f"{name} was not recognised")
            self.assertEqual(ptype, expected, name)

    def test_ambiguous_names_are_not_guessed(self):
        """Guessing scores the partner by the wrong rubric entirely."""
        for name in ("Larson & Associates", "Acme Holdings LLC", "Midwest Group"):
            _, confident = ingest_linkedin.infer_type(name)
            self.assertFalse(confident, name)


class LinkedInColumns(unittest.TestCase):
    """A loose "name" candidate matched "Last Name" and silently reduced
    every person to their surname."""

    def test_first_and_last_name_resolve_separately(self):
        cols = ingest_linkedin.resolve_columns(
            ["First Name", "Last Name", "Title", "Company", "Profile URL"])
        self.assertEqual(cols["first_name"], "First Name")
        self.assertEqual(cols["last_name"], "Last Name")
        self.assertNotIn("full_name", cols)

    def test_a_full_name_export_still_resolves(self):
        cols = ingest_linkedin.resolve_columns(["Full Name", "Title", "Company"])
        self.assertEqual(cols["full_name"], "Full Name")

    def test_no_two_fields_claim_the_same_column(self):
        cols = ingest_linkedin.resolve_columns(
            ["First Name", "Last Name", "Full Name", "Company", "Company Website"])
        self.assertEqual(len(set(cols.values())), len(cols))


class LinkedInLoad(unittest.TestCase):
    def _write(self, body):
        fh = tempfile.NamedTemporaryFile("w", suffix=".csv", delete=False,
                                         newline="")
        fh.write(body)
        fh.close()
        return fh.name

    def test_people_become_contacts_and_companies_become_partners(self):
        path = self._write(
            "First Name,Last Name,Title,Company,Profile URL\n"
            "Dana,Whitfield,CLO,Northgate Community Credit Union,https://a\n"
            "Pat,Larsen,CEO,Northgate Community Credit Union,https://b\n")
        partners, contacts, diag, _ = ingest_linkedin.load(path)
        self.assertEqual(len(partners), 1)
        self.assertEqual(len(contacts), 2)
        self.assertEqual(contacts[0]["name"], "Dana Whitfield")

    def test_untyped_companies_are_held_back_not_defaulted(self):
        path = self._write("First Name,Last Name,Company\n"
                           "Alex,Chen,Larson & Associates\n")
        partners, _, diag, unresolved = ingest_linkedin.load(path)
        self.assertEqual(partners, [])
        self.assertEqual(diag["unresolved_type"], 1)
        self.assertEqual(unresolved[0]["name"], "Larson & Associates")

    def test_an_explicit_default_type_adopts_the_untyped(self):
        path = self._write("First Name,Last Name,Company\n"
                           "Alex,Chen,Larson & Associates\n")
        partners, _, diag, _ = ingest_linkedin.load(
            path, default_type=PartnerType.CPA_FIRM.value)
        self.assertEqual(len(partners), 1)
        self.assertEqual(diag["unresolved_type"], 0)

    def test_duplicate_people_are_collapsed(self):
        path = self._write(
            "First Name,Last Name,Company\n"
            "Dana,Whitfield,Northgate Community Credit Union\n"
            "Dana,Whitfield,Northgate Community Credit Union\n")
        _, contacts, _, _ = ingest_linkedin.load(path)
        self.assertEqual(len(contacts), 1)

    def test_rows_with_no_company_are_skipped(self):
        path = self._write("First Name,Last Name,Company\nDana,Whitfield,\n")
        _, _, diag, _ = ingest_linkedin.load(path)
        self.assertEqual(diag["skipped_no_company"], 1)

    def test_a_file_with_no_company_column_fails_loudly(self):
        path = self._write("First Name,Last Name\nDana,Whitfield\n")
        with self.assertRaises(SystemExit):
            ingest_linkedin.load(path)


# ---------------------------------------------------------------------------
# Community banks
# ---------------------------------------------------------------------------

def _bank(name, assets, capital, cre, construction=None):
    return Partner(id=name, name=name,
                   partner_type=PartnerType.COMMUNITY_BANK.value,
                   total_assets=assets, risk_based_capital=capital,
                   cre_loans=cre, construction_loans=construction)


class BankScoring(unittest.TestCase):
    """Banks have no MBL cap -- that is a credit-union statutory construct.
    They are constrained by CRE concentration and a legal lending limit."""

    def test_concentration_math(self):
        self.assertAlmostEqual(
            cre_concentration(_bank("A", 418e6, 41.5e6, 137e6)), 3.301, places=2)

    def test_over_the_supervisory_trigger_scores_max_fit(self):
        partner = score_partner(_bank("A", 418e6, 41.5e6, 137e6))
        self.assertEqual(partner.fit_score, 40.0)
        self.assertTrue(any("300%" in r for r in partner.score_rationale))

    def test_low_concentration_scores_low_fit(self):
        self.assertEqual(score_partner(_bank("B", 418e6, 41.5e6, 18e6)).fit_score, 8.0)

    def test_construction_trigger_lifts_fit_on_its_own(self):
        """A bank can be over the 100% construction criterion while under 300%."""
        partner = score_partner(_bank("C", 418e6, 41.5e6, 60e6, 45e6))
        self.assertGreaterEqual(partner.fit_score, 36.0)

    def test_legal_lending_limit_is_fifteen_percent_of_capital(self):
        self.assertAlmostEqual(legal_lending_limit(_bank("A", 418e6, 41.5e6, 1e6)),
                               6.225e6)

    def test_banks_are_not_scored_on_the_credit_union_cap(self):
        """A bank with no net worth or MBL figures must still score on CRE."""
        partner = score_partner(_bank("A", 418e6, 41.5e6, 137e6))
        self.assertIsNone(cap_pressure(partner))
        self.assertEqual(partner.fit_score, 40.0)

    def test_access_falls_away_with_size(self):
        small = score_partner(_bank("s", 300e6, 30e6, 95e6))
        huge = score_partner(_bank("h", 20e9, 2e9, 6.2e9))
        self.assertGreater(small.access_score, huge.access_score)

    def test_products_differ_from_a_credit_union(self):
        bank = score_partner(_bank("A", 418e6, 41.5e6, 137e6))
        union = score_partner(cu(id="u", total_assets=486e6, net_worth=53e6,
                                 business_loans_outstanding=55e6))
        self.assertIn("real_estate", bank.products_matched)
        self.assertNotEqual(bank.products_matched, union.products_matched)


class FdicIngest(unittest.TestCase):
    def test_sample_file_loads_cleanly(self):
        partners, diag = ingest_fdic.load("data/sample_fdic_mn.csv")
        self.assertEqual(len(partners), 8)
        self.assertEqual(diag["columns_unmatched"], [])
        self.assertEqual(ingest_fdic.validate(partners), [])

    def test_thousands_scaled_file_is_caught(self):
        partners = [_bank("A", 418_000, 41_500, 137_000)]
        codes = {f["code"] for f in ingest_fdic.validate(partners)}
        self.assertIn("scale", codes)

    def test_capital_above_assets_is_an_error(self):
        codes = {f["code"] for f in ingest_fdic.validate([_bank("A", 41e6, 418e6, 10e6)])}
        self.assertIn("capital_exceeds_assets", codes)

    def test_cre_above_assets_is_an_error(self):
        codes = {f["code"] for f in ingest_fdic.validate([_bank("A", 100e6, 10e6, 400e6)])}
        self.assertIn("cre_exceeds_assets", codes)

    def test_missing_capital_blocks_the_import(self):
        partners = [_bank("A", 418e6, None, 137e6), _bank("B", 212e6, None, 31e6)]
        errors = {f["code"] for f in ingest_fdic.validate(partners)
                  if f["level"] == "error"}
        self.assertIn("missing_capital", errors)

    def test_rescale_restores_the_figures(self):
        partners = [_bank("A", 418_000, 41_500, 137_000)]
        ingest_fdic.rescale(partners, 1000.0)
        self.assertEqual(partners[0].total_assets, 418_000_000)
        self.assertEqual(ingest_fdic.validate(partners), [])


class ReconciliationRegression(unittest.TestCase):
    """Three bugs found by the end-to-end sweep after reconciliation landed.

    Merging two sources onto one record is where partial data does damage,
    and none of it raised: the contacts silently detached, the call-report
    figures silently blanked, and the tier silently dropped.
    """

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.conn = db.connect(Path(self.tmp.name) / "t.db")
        # As if imported from NCUA: full figures, no people.
        db.upsert(self.conn, score_partner(Partner(
            id="cu-90001", name="Northgate Community CU",
            partner_type="credit_union", total_assets=486e6, net_worth=53.4e6,
            business_loans_outstanding=55.9e6, business_loan_count=214)))
        self.conn.commit()

    def tearDown(self):
        self.conn.close()
        self.tmp.cleanup()

    def _linkedin_partner(self):
        return Partner(id="cu-northgate-community-credit-union",
                       name="Northgate Community Credit Union",
                       partner_type="credit_union")

    def test_reconcile_reports_the_id_mapping(self):
        """Callers need it to carry contacts across; a count is not enough."""
        _, remapped = db.reconcile_ids(self.conn, [self._linkedin_partner()])
        self.assertEqual(remapped,
                         {"cu-northgate-community-credit-union": "cu-90001"})

    def test_contacts_follow_the_remapped_id(self):
        contacts = [{"partner_id": "cu-northgate-community-credit-union",
                     "name": "Dana Whitfield"}]
        _, remapped = db.reconcile_ids(self.conn, [self._linkedin_partner()])
        for c in contacts:
            c["partner_id"] = remapped.get(c["partner_id"], c["partner_id"])
        self.assertEqual(contacts[0]["partner_id"], "cu-90001")

    def test_a_partial_source_does_not_blank_existing_figures(self):
        """A LinkedIn export has no call-report data and must not erase it."""
        incoming, _ = db.reconcile_ids(self.conn, [self._linkedin_partner()])
        db.upsert(self.conn, incoming[0])
        self.conn.commit()
        merged = db.get(self.conn, "cu-90001")
        self.assertEqual(merged.total_assets, 486e6)
        self.assertEqual(merged.net_worth, 53.4e6)
        self.assertEqual(merged.business_loan_count, 214)

    def test_scoring_after_the_merge_keeps_the_tier(self):
        """Scored before the merge, the partner lands at the bottom."""
        incoming, _ = db.reconcile_ids(self.conn, [self._linkedin_partner()])
        score_partner(incoming[0])
        self.assertEqual(incoming[0].tier, "D")      # scored from empty fields

        db.upsert(self.conn, incoming[0])
        self.conn.commit()
        merged = score_partner(db.get(self.conn, "cu-90001"))
        self.assertEqual(merged.tier, "A")           # scored from the merge

    def test_no_orphaned_contacts_after_a_merge(self):
        _, remapped = db.reconcile_ids(self.conn, [self._linkedin_partner()])
        target = remapped["cu-northgate-community-credit-union"]
        db.add_contact(self.conn, target, "Dana Whitfield")
        self.conn.commit()
        partner_ids = {p.id for p in db.all_partners(self.conn)}
        rows = self.conn.execute("SELECT partner_id FROM contacts")
        for row in rows:
            self.assertIn(row["partner_id"], partner_ids)
