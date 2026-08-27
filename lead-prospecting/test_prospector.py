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
