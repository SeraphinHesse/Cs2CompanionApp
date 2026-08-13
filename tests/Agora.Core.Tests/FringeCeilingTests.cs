using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Parties;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 15 — the water-filling arithmetic that imposes the fringe ceiling on one bloc's affinity
    /// row.
    ///
    /// <para>
    /// These tests are about the cap alone: they build affinity rows by hand and softmax them here, so
    /// nothing depends on the voter model, on tuning, or on a seed. The rule they are all circling is
    /// that a ceiling must hold <i>and</i> leave a valid distribution behind — a cap that produced
    /// shares summing to 0.97 would satisfy every naive assertion and be badly wrong.
    /// </para>
    /// </summary>
    public class FringeCeilingTests
    {
        private const double T = 0.35;   // affinity.softmaxTemperature, shipped

        // ------------------------------------------------------------------------------------------
        // Fixtures
        // ------------------------------------------------------------------------------------------

        private static BlocAffinity Cell(string partyId, double affinity)
        {
            return new BlocAffinity
            {
                DistrictId = "d-01",
                Bloc = new BlocKey(WealthTier.Middle, EducationTier.Educated, AgeBand.Adult),
                PartyId = partyId,
                Affinity = affinity
            };
        }

        /// <summary>
        /// A row where the two fringe parties are genuinely popular — closer to this bloc than one of
        /// the majors. That is the case the ceiling exists for; a row where they were already tiny
        /// would pass every test below without the cap doing anything.
        /// </summary>
        private static List<BlocAffinity> PopularFringeRow()
        {
            return new List<BlocAffinity>
            {
                Cell("party-01", 1.50),   // major
                Cell("party-02", 1.20),   // major
                Cell("party-03", 1.45),   // fringe, nearly leading
                Cell("party-04", 1.30)    // fringe
            };
        }

        private static FringeCeilings CeilingsFor(double value, params string[] partyIds)
        {
            var list = new List<PartyCeiling>();
            foreach (string id in partyIds) list.Add(new PartyCeiling(id, value));
            return FringeCeilings.FromList(list);
        }

        /// <summary>
        /// The same softmax the affinity packet and the FPTP packet both apply. Reimplemented here
        /// rather than called, so these tests pin the arithmetic the cap is aimed at instead of
        /// agreeing with whatever the engine currently does.
        /// </summary>
        private static double[] Softmax(IReadOnlyList<BlocAffinity> row, double temperature)
        {
            int n = row.Count;
            var w = new double[n];

            double max = double.NegativeInfinity;
            for (int i = 0; i < n; i++) if (row[i].Affinity > max) max = row[i].Affinity;

            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                w[i] = Math.Exp((row[i].Affinity - max) / temperature);
                sum += w[i];
            }

            for (int i = 0; i < n; i++) w[i] /= sum;
            return w;
        }

        private static double ShareOf(IReadOnlyList<BlocAffinity> row, double[] shares, string partyId)
        {
            for (int i = 0; i < row.Count; i++)
                if (row[i].PartyId == partyId) return shares[i];

            throw new Xunit.Sdk.XunitException("no party " + partyId + " in the row");
        }

        private static double Sum(double[] values)
        {
            double total = 0.0;
            for (int i = 0; i < values.Length; i++) total += values[i];
            return total;
        }

        // ------------------------------------------------------------------------------------------
        // The cap itself
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The premise. Without a ceiling the two minor parties in this row take roughly half the bloc
        /// between them — which is the bug: nothing in the voter model was holding them down.
        /// </summary>
        [Fact]
        public void WithoutACeiling_FringePartiesTakeAHugeShare()
        {
            List<BlocAffinity> row = PopularFringeRow();
            double[] shares = Softmax(row, T);

            double fringe = ShareOf(row, shares, "party-03") + ShareOf(row, shares, "party-04");
            Assert.True(fringe > 0.40, "fixture is not exercising the problem: fringe total was " + fringe);
        }

        [Fact]
        public void Ceiling_PinsEveryCappedPartyAtOrUnderItsCap()
        {
            List<BlocAffinity> row = PopularFringeRow();
            FringeCeiling.ApplyToRow(row, CeilingsFor(0.03, "party-03", "party-04"), T);

            double[] shares = Softmax(row, T);

            Assert.True(ShareOf(row, shares, "party-03") <= 0.03 + 1e-12);
            Assert.True(ShareOf(row, shares, "party-04") <= 0.03 + 1e-12);
        }

        /// <summary>
        /// The invariant that makes the cap safe rather than merely small. A capped row must still be
        /// a probability distribution; anything else corrupts every downstream vote count.
        /// </summary>
        [Fact]
        public void Ceiling_LeavesTheRowSummingToOne()
        {
            List<BlocAffinity> row = PopularFringeRow();
            FringeCeiling.ApplyToRow(row, CeilingsFor(0.03, "party-03", "party-04"), T);

            Assert.Equal(1.0, Sum(Softmax(row, T)), 12);
        }

        /// <summary>
        /// A binding ceiling should bind exactly, not overshoot. A cap that pushed a party to 0.1%
        /// when it was entitled to 3% would be suppressing far harder than the tuning says.
        /// </summary>
        [Fact]
        public void Ceiling_LandsExactlyOnTheCapWhenItBinds()
        {
            List<BlocAffinity> row = PopularFringeRow();
            FringeCeiling.ApplyToRow(row, CeilingsFor(0.03, "party-03", "party-04"), T);

            double[] shares = Softmax(row, T);
            Assert.Equal(0.03, ShareOf(row, shares, "party-03"), 12);
            Assert.Equal(0.03, ShareOf(row, shares, "party-04"), 12);
        }

        /// <summary>
        /// Where the surplus goes. It is never redistributed explicitly — shrinking only the capped
        /// weights means the softmax hands the freed mass to the uncapped parties in proportion to
        /// what they already had, so the ratio between the two majors is untouched.
        /// </summary>
        [Fact]
        public void Ceiling_RedistributesToMajorsInProportion_LeavingTheirRatioUnchanged()
        {
            List<BlocAffinity> before = PopularFringeRow();
            double[] sharesBefore = Softmax(before, T);
            double ratioBefore = ShareOf(before, sharesBefore, "party-01") /
                                 ShareOf(before, sharesBefore, "party-02");

            List<BlocAffinity> after = PopularFringeRow();
            FringeCeiling.ApplyToRow(after, CeilingsFor(0.03, "party-03", "party-04"), T);
            double[] sharesAfter = Softmax(after, T);
            double ratioAfter = ShareOf(after, sharesAfter, "party-01") /
                                ShareOf(after, sharesAfter, "party-02");

            Assert.Equal(ratioBefore, ratioAfter, 12);

            // And both majors are strictly better off, since the freed mass has to land somewhere.
            Assert.True(ShareOf(after, sharesAfter, "party-01") > ShareOf(before, sharesBefore, "party-01"));
            Assert.True(ShareOf(after, sharesAfter, "party-02") > ShareOf(before, sharesBefore, "party-02"));
        }

        /// <summary>
        /// A party already under its ceiling is not touched at all. The cap is a ceiling, not a target
        /// — it must never lift an unpopular party up to 3%.
        /// </summary>
        [Fact]
        public void Ceiling_LeavesAPartyAlreadyBelowItUntouched()
        {
            var row = new List<BlocAffinity>
            {
                Cell("party-01", 2.00),
                Cell("party-02", 1.90),
                Cell("party-03", 0.10)    // hopeless on its own merits
            };

            double[] before = Softmax(row, T);
            double fringeBefore = ShareOf(row, before, "party-03");
            Assert.True(fringeBefore < 0.03, "fixture should already be under the ceiling");

            FringeCeiling.ApplyToRow(row, CeilingsFor(0.03, "party-03"), T);

            Assert.Equal(0.0, row[2].CeilingComponent, 12);
            Assert.Equal(fringeBefore, ShareOf(row, Softmax(row, T), "party-03"), 12);
        }

        // ------------------------------------------------------------------------------------------
        // Water-filling: the case a single pass gets wrong
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Why the binding set is grown iteratively. Capping the loudest fringe party lifts everyone
        /// else, which can push a second fringe party that was <i>under</i> its ceiling back over it.
        /// A single-pass implementation would leave that second party above its cap.
        /// </summary>
        [Fact]
        public void Ceiling_HoldsWhenCappingOnePartyPushesAnotherOverItsOwnCap()
        {
            // party-04 sits just under 3% to begin with, and only crosses it once party-03's mass is
            // freed. It has to end up capped too.
            var row = new List<BlocAffinity>
            {
                Cell("party-01", 2.00),
                Cell("party-02", 1.95),
                Cell("party-03", 1.90),
                Cell("party-04", 0.72)
            };

            double before = ShareOf(row, Softmax(row, T), "party-04");
            Assert.True(before < 0.03, "fixture precondition: party-04 starts under the cap, was " + before);

            FringeCeiling.ApplyToRow(row, CeilingsFor(0.03, "party-03", "party-04"), T);
            double[] shares = Softmax(row, T);

            Assert.True(ShareOf(row, shares, "party-03") <= 0.03 + 1e-12);
            Assert.True(ShareOf(row, shares, "party-04") <= 0.03 + 1e-12);
            Assert.Equal(1.0, Sum(shares), 12);
        }

        [Fact]
        public void Ceiling_HoldsForEveryPartyWhenAllFringeCapsBind()
        {
            var row = new List<BlocAffinity>
            {
                Cell("party-01", 1.00),
                Cell("party-02", 1.00),
                Cell("party-03", 1.00),
                Cell("party-04", 1.00),
                Cell("party-05", 1.00)
            };

            FringeCeiling.ApplyToRow(row, CeilingsFor(0.03, "party-03", "party-04", "party-05"), T);
            double[] shares = Softmax(row, T);

            Assert.Equal(0.03, ShareOf(row, shares, "party-03"), 12);
            Assert.Equal(0.03, ShareOf(row, shares, "party-04"), 12);
            Assert.Equal(0.03, ShareOf(row, shares, "party-05"), 12);

            // The two majors split the remaining 91% evenly, since their affinities are equal.
            Assert.Equal(0.455, ShareOf(row, shares, "party-01"), 12);
            Assert.Equal(0.455, ShareOf(row, shares, "party-02"), 12);
            Assert.Equal(1.0, Sum(shares), 12);
        }

        // ------------------------------------------------------------------------------------------
        // Bookkeeping
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// <see cref="BlocAffinity.Affinity"/> is documented as the sum of its components, and the
        /// dashboard's "why" panel adds them up. The ceiling shift has to be recorded, not smuggled.
        /// </summary>
        [Fact]
        public void Ceiling_RecordsItsShiftAndOnlyEverLowersAffinity()
        {
            List<BlocAffinity> before = PopularFringeRow();
            List<BlocAffinity> after = PopularFringeRow();

            FringeCeiling.ApplyToRow(after, CeilingsFor(0.03, "party-03", "party-04"), T);

            for (int i = 0; i < after.Count; i++)
            {
                Assert.True(after[i].CeilingComponent <= 0.0, after[i].PartyId + " was raised by the ceiling");
                Assert.Equal(before[i].Affinity + after[i].CeilingComponent, after[i].Affinity, 12);
            }

            // Majors carry no suppression at all.
            Assert.Equal(0.0, after[0].CeilingComponent, 12);
            Assert.Equal(0.0, after[1].CeilingComponent, 12);
            Assert.True(after[2].CeilingComponent < 0.0);
            Assert.True(after[3].CeilingComponent < 0.0);
        }

        [Fact]
        public void Ceiling_IsDeterministic()
        {
            List<BlocAffinity> a = PopularFringeRow();
            List<BlocAffinity> b = PopularFringeRow();

            FringeCeiling.ApplyToRow(a, CeilingsFor(0.03, "party-03", "party-04"), T);
            FringeCeiling.ApplyToRow(b, CeilingsFor(0.03, "party-03", "party-04"), T);

            Assert.Equal(Describe(a), Describe(b));
        }

        /// <summary>Row order must not reach the result — the same rule as everywhere else in Core.</summary>
        [Fact]
        public void Ceiling_IsIndependentOfRowOrder()
        {
            List<BlocAffinity> natural = PopularFringeRow();
            List<BlocAffinity> shuffled = PopularFringeRow();
            shuffled.Reverse();

            FringeCeiling.ApplyToRow(natural, CeilingsFor(0.03, "party-03", "party-04"), T);
            FringeCeiling.ApplyToRow(shuffled, CeilingsFor(0.03, "party-03", "party-04"), T);
            shuffled.Reverse();

            Assert.Equal(Describe(natural), Describe(shuffled));
        }

        /// <summary>Applying a ceiling that already holds must not shift anything a second time.</summary>
        [Fact]
        public void Ceiling_IsIdempotent()
        {
            List<BlocAffinity> row = PopularFringeRow();
            FringeCeilings ceilings = CeilingsFor(0.03, "party-03", "party-04");

            FringeCeiling.ApplyToRow(row, ceilings, T);
            string once = Describe(row);

            FringeCeiling.ApplyToRow(row, ceilings, T);

            Assert.Equal(once, Describe(row));
        }

        // ------------------------------------------------------------------------------------------
        // Failing open
        // ------------------------------------------------------------------------------------------

        [Fact]
        public void NoCeilings_IsAPerfectNoOp()
        {
            List<BlocAffinity> row = PopularFringeRow();
            string before = Describe(row);

            FringeCeiling.ApplyToRow(row, FringeCeilings.None, T);

            Assert.Equal(before, Describe(row));
        }

        /// <summary>
        /// Ceilings that leave no room for anyone. Someone has to hold the rest of the vote, so the
        /// row is left alone rather than being driven to a distribution that cannot exist.
        /// </summary>
        [Fact]
        public void ImpossibleCeilings_LeaveTheRowUntouched()
        {
            List<BlocAffinity> row = PopularFringeRow();
            string before = Describe(row);

            // Four parties, every one capped at 10%: the row cannot sum to 1.
            FringeCeiling.ApplyToRow(row, CeilingsFor(0.10, "party-01", "party-02", "party-03", "party-04"), T);

            Assert.Equal(before, Describe(row));
        }

        [Fact]
        public void CeilingsThatConsumeTheWholeRow_LeaveItUntouched()
        {
            List<BlocAffinity> row = PopularFringeRow();
            string before = Describe(row);

            FringeCeiling.ApplyToRow(row, CeilingsFor(0.50, "party-01", "party-02", "party-03", "party-04"), T);

            Assert.Equal(before, Describe(row));
        }

        /// <summary>
        /// The same non-positive temperature the softmax itself treats as winner-take-all. Unreachable
        /// under shipped tuning; a tuning typo must degrade bluntly rather than divide by zero, and
        /// must not silently stop capping.
        /// </summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        public void DegenerateTemperature_DropsCappedPartiesToTheRowMinimum(double temperature)
        {
            List<BlocAffinity> row = PopularFringeRow();
            FringeCeiling.ApplyToRow(row, CeilingsFor(0.03, "party-03", "party-04"), temperature);

            double min = Math.Min(row[0].Affinity, row[1].Affinity);
            Assert.Equal(min, row[2].Affinity, 12);
            Assert.Equal(min, row[3].Affinity, 12);

            // Neither fringe party can be the unique maximum, so neither can take the bloc.
            Assert.True(row[0].Affinity >= row[2].Affinity);
            Assert.True(row[0].Affinity >= row[3].Affinity);
        }

        [Fact]
        public void EmptyOrNullRow_IsHandled()
        {
            FringeCeiling.ApplyToRow(null, CeilingsFor(0.03, "party-03"), T);
            FringeCeiling.ApplyToRow(new List<BlocAffinity>(), CeilingsFor(0.03, "party-03"), T);
        }

        // ------------------------------------------------------------------------------------------
        // The lookup table
        // ------------------------------------------------------------------------------------------

        [Fact]
        public void FromList_SortsByIdAndProbesCorrectly()
        {
            FringeCeilings c = FringeCeilings.FromList(new List<PartyCeiling>
            {
                new PartyCeiling("party-09", 0.05),
                new PartyCeiling("party-02", 0.03),
                new PartyCeiling("party-11", 0.07)
            });

            double v;
            Assert.True(c.TryGet("party-02", out v));
            Assert.Equal(0.03, v, 12);
            Assert.True(c.TryGet("party-09", out v));
            Assert.Equal(0.05, v, 12);
            Assert.True(c.TryGet("party-11", out v));
            Assert.Equal(0.07, v, 12);

            Assert.False(c.TryGet("party-01", out v));
            Assert.False(c.TryGet("", out v));
            Assert.False(c.TryGet(null, out v));
        }

        /// <summary>A duplicate keeps the stricter of the two, not whichever arrived last.</summary>
        [Fact]
        public void FromList_KeepsTheLowerCeilingForADuplicateId()
        {
            FringeCeilings c = FringeCeilings.FromList(new List<PartyCeiling>
            {
                new PartyCeiling("party-03", 0.30),
                new PartyCeiling("party-03", 0.03)
            });

            double v;
            Assert.True(c.TryGet("party-03", out v));
            Assert.Equal(0.03, v, 12);
            Assert.Equal(1, c.Count);
        }

        [Fact]
        public void FromList_ClampsAndDropsUnusableEntries()
        {
            FringeCeilings c = FringeCeilings.FromList(new List<PartyCeiling>
            {
                new PartyCeiling("party-01", -0.5),
                new PartyCeiling("party-02", 1.5),
                new PartyCeiling("party-03", double.NaN),
                new PartyCeiling("", 0.03)
            });

            double v;
            Assert.True(c.TryGet("party-01", out v));
            Assert.Equal(0.0, v, 12);
            Assert.True(c.TryGet("party-02", out v));
            Assert.Equal(1.0, v, 12);
            Assert.False(c.TryGet("party-03", out v));
            Assert.Equal(2, c.Count);
        }

        [Fact]
        public void FromList_OfNothing_IsEmpty()
        {
            Assert.True(FringeCeilings.FromList(null).IsEmpty);
            Assert.True(FringeCeilings.FromList(new List<PartyCeiling>()).IsEmpty);
            Assert.True(FringeCeilings.None.IsEmpty);
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        private static string Describe(IReadOnlyList<BlocAffinity> row)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < row.Count; i++)
            {
                sb.Append(row[i].PartyId).Append('=')
                  .Append(row[i].Affinity.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                  .Append(row[i].CeilingComponent.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }
            return sb.ToString();
        }
    }
}
