using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class FightTeamScorecardBlueprintFactory
{
    public TeamFightScorecardBlueprintDto CreateV1()
    {
        return new TeamFightScorecardBlueprintDto(
            Name: "Fight Execution Score",
            Summary: "A contextual one-fight scorecard for organized-vs-organized WvW that keeps context and outcome separate from scored execution.",
            NonScoredContext:
            [
                "Squad player count",
                "Enemy player count",
                "Friendly non-squad count",
                "Fight duration",
                "Data confidence"
            ],
            NonScoredOutcomeHeadline:
            [
                "Squad downs and enemy downs",
                "Squad kills and enemy kills",
                "Squad deaths and enemy deaths",
                "Enemy down conversion rate",
                "Squad recovery rate",
                "Incoming and outgoing CC",
                "Wipe / no wipe"
            ],
            PrimaryPillars:
            [
                new ScorecardPillarDto(
                    Name: "Cohesion & Positioning",
                    WeightPercent: 25,
                    Summary: "Measures commander-relative discipline and formation stability while the squad is engaged.",
                    Metrics:
                    [
                        new ScorecardMetricDto("In-position rate", "higher", "eligible replay samples", true, true),
                        new ScorecardMetricDto("Pressure positioning", "higher", "eligible pressure samples", true, true),
                        new ScorecardMetricDto("Too-far rate", "lower", "eligible replay samples", true, true),
                        new ScorecardMetricDto("Lateral-risk rate", "lower", "eligible replay samples", true, true),
                        new ScorecardMetricDto("Overextended rate", "lower", "eligible replay samples", true, true)
                    ]),
                new ScorecardPillarDto(
                    Name: "Pressure & Burst",
                    WeightPercent: 25,
                    Summary: "Focuses on whether offensive pressure creates meaningful enemy failures instead of just large raw totals.",
                    Metrics:
                    [
                        new ScorecardMetricDto("Downs per active player", "higher", "active player", true, true),
                        new ScorecardMetricDto("Burst pressure yield", "higher", "burst window", true, true),
                        new ScorecardMetricDto("Strips per active player per minute", "higher", "active player minute", true, true)
                    ]),
                new ScorecardPillarDto(
                    Name: "Downstate Control",
                    WeightPercent: 25,
                    Summary: "Tracks how cleanly the squad converts enemy downs and how effectively it recovers its own.",
                    Metrics:
                    [
                        new ScorecardMetricDto("Enemy down conversion rate", "higher", "enemy downs", true, true),
                        new ScorecardMetricDto("Enemy average down-to-kill time", "lower", "enemy kill conversion", true, true),
                        new ScorecardMetricDto("Own recovery rate", "higher", "own downs", true, true),
                        new ScorecardMetricDto("Own average down-to-recover time", "lower", "own recoveries", true, true)
                    ]),
                new ScorecardPillarDto(
                    Name: "Support & Mitigation",
                    WeightPercent: 25,
                    Summary: "Measures how well support tools answer incoming pressure without treating final deaths as a separate score.",
                    Metrics:
                    [
                        new ScorecardMetricDto("Healing coverage", "higher", "health damage", true, true),
                        new ScorecardMetricDto("Cleanse pressure response", "higher", "condition pressure", true, true),
                        new ScorecardMetricDto("Weighted support boon coverage", "higher", "threatened boon coverage", true, true),
                        new ScorecardMetricDto("Prevention value", "higher", "incoming pressure", true, true),
                        new ScorecardMetricDto("Saved-player balance", "higher", "squad downs", true, true)
                    ])
            ],
            SupportingExplanationMetrics:
            [
                "Support delivery",
                "Opponent difficulty",
                "Commander context",
                "Fight geometry",
                "Capability matchup",
                "Mechanics drilldown"
            ]);
    }
}
