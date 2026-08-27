"""Self-tests. Run: python3 -m unittest test_prospector -v"""

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
