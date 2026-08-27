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
