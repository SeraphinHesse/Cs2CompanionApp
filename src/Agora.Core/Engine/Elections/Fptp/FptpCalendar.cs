using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Elections.Fptp
{
    /// <summary>
    /// The NA term calendar: when the next election falls and when the campaign opens.
    /// </summary>
    /// <remarks>
    /// Pure <see cref="SimDate"/> arithmetic. Non-negotiable #8 — nothing here reads a clock; the
    /// caller supplies every date, and <c>AgoraTimeService</c> in <c>Agora.Mod</c> is the only thing
    /// that ever asks the game what day it is.
    /// </remarks>
    public static class FptpCalendar
    {
        /// <summary>The next scheduled council election after <paramref name="previousElection"/>.</summary>
        public static SimDate NextElection(SimDate previousElection, EngineTuning tuning)
        {
            int years = tuning.ElectionsFptp.TermYears;
            if (years < 1) years = 1;   // a zero-length term would schedule an election on its own date
            return previousElection.AddYears(years);
        }

        /// <summary>
        /// When a mayor inaugurated on <paramref name="inauguration"/> reaches the end of the term.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="NextElection"/> because <c>mayorTermYears</c> is a separate key:
        /// they ship equal (1 and 1, so the ticket is elected together) but a future theme could
        /// stagger them, and reading one key for both would silently ignore the change.
        /// </remarks>
        public static SimDate MayorTermEnd(SimDate inauguration, EngineTuning tuning)
        {
            int years = tuning.ElectionsFptp.MayorTermYears;
            if (years < 1) years = 1;
            return inauguration.AddYears(years);
        }

        /// <summary>The date campaign season opens for an election on <paramref name="electionDate"/>.</summary>
        public static SimDate CampaignStart(SimDate electionDate, EngineTuning tuning)
        {
            int months = tuning.ElectionsFptp.CampaignMonths;
            if (months < 0) months = 0;
            return electionDate.AddMonths(-months);
        }

        /// <summary>
        /// True when <paramref name="now"/> falls inside the campaign window for
        /// <paramref name="electionDate"/>. A null election date is never campaign season.
        /// </summary>
        public static bool IsCampaignSeason(SimDate now, SimDate? electionDate, EngineTuning tuning)
        {
            if (!electionDate.HasValue) return false;

            SimDate election = electionDate.Value;
            return now >= CampaignStart(election, tuning) && now <= election;
        }
    }
}
