using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class HistoricalEffectivenessWinLossService
{
    public const string CurrentMethodVersion = "historical-effectiveness-win-loss-v1";

    private const int MinimumPairedFights = 5;
    private const double Normal95CriticalValue = 1.96;
    private const double DirectionThreshold = 3.0;

    private readonly HistoricalEffectivenessService _historicalEffectiveness;
    private readonly FightCatalogService _fightCatalog;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, HistoricalEffectivenessWinLossSnapshotDto> _cache =
        new(StringComparer.Ordinal);
    private long _catalogVersion = -1;

    public HistoricalEffectivenessWinLossService(
        HistoricalEffectivenessService historicalEffectiveness,
        FightCatalogService fightCatalog)
    {
        _historicalEffectiveness = historicalEffectiveness;
        _fightCatalog = fightCatalog;
    }

    public HistoricalEffectivenessWinLossSnapshotDto BuildSnapshot(
        string? commander,
        string? startDate,
        string? endDate,
        string? squadIncludeClasses,
        string? squadExcludeClasses,
        string? enemyIncludeClasses,
        string? enemyExcludeClasses,
        string? patchScope,
        string? patchEraIds,
        string? fightAttributes)
    {
        string cacheKey = string.Join(
            '\u001F',
            new[]
            {
                commander,
                startDate,
                endDate,
                squadIncludeClasses,
                squadExcludeClasses,
                enemyIncludeClasses,
                enemyExcludeClasses,
                patchScope,
                patchEraIds,
                fightAttributes,
            }.Select(value => value?.Trim() ?? string.Empty));
        long catalogVersion = _fightCatalog.CacheVersion;
        lock (_cacheLock)
        {
            if (_catalogVersion != catalogVersion)
            {
                _cache.Clear();
                _catalogVersion = catalogVersion;
            }
            if (_cache.TryGetValue(cacheKey, out HistoricalEffectivenessWinLossSnapshotDto? cached))
            {
                return cached;
            }
        }

        HistoricalEffectivenessSnapshotDto wins = _historicalEffectiveness.BuildSnapshot(
            commander,
            startDate,
            endDate,
            outcomeCode: "squad",
            squadIncludeClasses,
            squadExcludeClasses,
            enemyIncludeClasses,
            enemyExcludeClasses,
            patchScope,
            patchEraIds,
            fightAttributes);
        HistoricalEffectivenessSnapshotDto losses = _historicalEffectiveness.BuildSnapshot(
            commander,
            startDate,
            endDate,
            outcomeCode: "enemy",
            squadIncludeClasses,
            squadExcludeClasses,
            enemyIncludeClasses,
            enemyExcludeClasses,
            patchScope,
            patchEraIds,
            fightAttributes);

        var lossReportLookup = losses.Reports.ToDictionary(report => report.Key, StringComparer.Ordinal);
        HistoricalEffectivenessWinLossReportDto[] reports = wins.Reports
            .Where(report => lossReportLookup.ContainsKey(report.Key))
            .Select(report => BuildReport(report, lossReportLookup[report.Key]))
            .ToArray();
        HistoricalEffectivenessWinLossSnapshotDto snapshot = new(
            MethodVersion: CurrentMethodVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
            Wins: wins.Scope,
            Losses: losses.Scope,
            Methodology: BuildMethodology(),
            AvailabilityNotes: wins.Scope.AvailabilityNotes
                .Concat(losses.Scope.AvailabilityNotes)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Reports: reports);

        long finalCatalogVersion = _fightCatalog.CacheVersion;
        lock (_cacheLock)
        {
            if (_catalogVersion != finalCatalogVersion)
            {
                _cache.Clear();
                _catalogVersion = finalCatalogVersion;
            }
            if (catalogVersion == finalCatalogVersion)
            {
                if (_cache.Count >= 12)
                {
                    _cache.Clear();
                }
                _cache[cacheKey] = snapshot;
            }
        }
        return snapshot;
    }

    private static HistoricalEffectivenessWinLossMethodologyDto BuildMethodology() =>
        new(
            Summary: "The same outcome-versus-baseline signal is calculated separately in wins and losses, then the two fight-weighted changes are compared.",
            Comparison: "For squad signals, a positive result edge means the signal was stronger in wins. For enemy signals, a positive result edge means the signal was stronger in squad losses.",
            Weighting: "Observations are averaged within each fight first and every fight receives equal weight in its win or loss cohort.",
            ConfidenceIntervals: "Result-edge intervals approximate the independent uncertainty of the win and loss within-fight difference estimates.",
            SeparationScore: "Separation combines the signed change in association strength with the weaker cohort's evidence. It ranks differences across unlike units without treating missing data as zero.",
            Interpretation: "These are observational differences, not proof that a signal caused the result. Filters other than the Outcome filter define both cohorts.",
            MinimumPairedFights: MinimumPairedFights);

    private static HistoricalEffectivenessWinLossReportDto BuildReport(
        HistoricalEffectivenessReportDto wins,
        HistoricalEffectivenessReportDto losses)
    {
        var lossMetricLookup = losses.Metrics.ToDictionary(metric => metric.Key, StringComparer.Ordinal);
        HistoricalEffectivenessWinLossMetricDto[] metrics = wins.Metrics
            .Where(metric => lossMetricLookup.ContainsKey(metric.Key))
            .Select(metric => BuildMetric(metric, lossMetricLookup[metric.Key], wins.PerspectiveSideId))
            .ToArray();
        metrics = RankMetrics(metrics);

        var lossEffectLookup = losses.NamedEffects.ToDictionary(effect => effect.Key, StringComparer.Ordinal);
        HistoricalEffectivenessWinLossNamedEffectDto[] effects = wins.NamedEffects
            .Where(effect => lossEffectLookup.ContainsKey(effect.Key))
            .Select(effect => BuildNamedEffect(
                effect,
                lossEffectLookup[effect.Key],
                wins.PerspectiveSideId))
            .ToArray();
        effects = RankNamedEffects(effects);

        double reportEvidence = metrics
            .Where(metric => metric.Available)
            .Select(metric => metric.EvidenceScore)
            .DefaultIfEmpty(0)
            .Average();
        return new HistoricalEffectivenessWinLossReportDto(
            Key: wins.Key,
            PerspectiveSideId: wins.PerspectiveSideId,
            OpposingSideId: wins.OpposingSideId,
            OutcomeFamily: wins.OutcomeFamily,
            OutcomeLabel: wins.OutcomeLabel,
            BaselineLabel: wins.BaselineLabel,
            WinPairedFightCount: wins.PairedFightCount,
            LossPairedFightCount: losses.PairedFightCount,
            ConfidenceLabel: GetConfidenceLabel(reportEvidence),
            Metrics: metrics,
            NamedEffects: effects);
    }

    private static HistoricalEffectivenessWinLossMetricDto BuildMetric(
        HistoricalEffectivenessMetricDto wins,
        HistoricalEffectivenessMetricDto losses,
        string perspectiveSideId)
    {
        bool available = wins.Available &&
            losses.Available &&
            wins.Difference.HasValue &&
            losses.Difference.HasValue;
        string? unavailableReason = !wins.Available
            ? wins.UnavailableReason
            : !losses.Available
                ? losses.UnavailableReason
                : !wins.Difference.HasValue || !losses.Difference.HasValue
                    ? "The filtered win or loss cohort has no comparable paired estimate."
                    : null;
        bool squadPerspective = string.Equals(
            perspectiveSideId,
            "squad",
            StringComparison.OrdinalIgnoreCase);
        double? resultEdge = available
            ? squadPerspective
                ? wins.Difference - losses.Difference
                : losses.Difference - wins.Difference
            : null;
        (double? lower, double? upper) = BuildResultEdgeInterval(
            wins.Difference,
            wins.Lower95Difference,
            wins.Upper95Difference,
            losses.Difference,
            losses.Lower95Difference,
            losses.Upper95Difference,
            squadPerspective);
        double evidence = available
            ? BuildEvidence(
                wins.EvidenceScore,
                losses.EvidenceScore,
                wins.PairedFightCount,
                losses.PairedFightCount)
            : 0;
        double? signedAssociationEdge = available
            ? BuildSignedAssociationEdge(
                wins.Difference,
                wins.AssociationScore,
                losses.Difference,
                losses.AssociationScore,
                squadPerspective)
            : null;
        double? separation = signedAssociationEdge.HasValue
            ? Math.Round(
                Math.Min(100.0, Math.Abs(signedAssociationEdge.Value)) * evidence / 100.0,
                3)
            : null;
        bool eligible = available &&
            wins.PairedFightCount >= MinimumPairedFights &&
            losses.PairedFightCount >= MinimumPairedFights;
        string direction = GetDirectionLabel(signedAssociationEdge, squadPerspective);

        return new HistoricalEffectivenessWinLossMetricDto(
            Rank: null,
            Key: wins.Key,
            Label: wins.Label,
            Group: wins.Group,
            Unit: wins.Unit,
            Available: available,
            UnavailableReason: unavailableReason,
            WinOutcomeAverage: wins.OutcomeAverage,
            WinBaselineAverage: wins.BaselineAverage,
            WinDifference: wins.Difference,
            WinPercentLift: wins.PercentLift,
            WinAssociationScore: wins.AssociationScore,
            WinEvidenceScore: wins.EvidenceScore,
            LossOutcomeAverage: losses.OutcomeAverage,
            LossBaselineAverage: losses.BaselineAverage,
            LossDifference: losses.Difference,
            LossPercentLift: losses.PercentLift,
            LossAssociationScore: losses.AssociationScore,
            LossEvidenceScore: losses.EvidenceScore,
            ResultEdge: resultEdge,
            Lower95ResultEdge: lower,
            Upper95ResultEdge: upper,
            SeparationScore: separation,
            EvidenceScore: evidence,
            ConfidenceLabel: GetConfidenceLabel(evidence),
            DirectionLabel: direction,
            RankingResult: eligible ? "Eligible for result-separation ranking." : "Insufficient paired win/loss evidence.",
            Detail: BuildDetail(
                wins.PairedFightCount,
                losses.PairedFightCount,
                resultEdge,
                lower,
                upper,
                wins.Unit,
                direction));
    }

    private static HistoricalEffectivenessWinLossNamedEffectDto BuildNamedEffect(
        HistoricalEffectivenessNamedEffectDto wins,
        HistoricalEffectivenessNamedEffectDto losses,
        string perspectiveSideId)
    {
        bool available = wins.Difference.HasValue && losses.Difference.HasValue;
        bool squadPerspective = string.Equals(
            perspectiveSideId,
            "squad",
            StringComparison.OrdinalIgnoreCase);
        double? resultEdge = available
            ? squadPerspective
                ? wins.Difference - losses.Difference
                : losses.Difference - wins.Difference
            : null;
        (double? lower, double? upper) = BuildResultEdgeInterval(
            wins.Difference,
            wins.Lower95Difference,
            wins.Upper95Difference,
            losses.Difference,
            losses.Lower95Difference,
            losses.Upper95Difference,
            squadPerspective);
        double evidence = available
            ? BuildEvidence(
                wins.EvidenceScore,
                losses.EvidenceScore,
                wins.PairedFightCount,
                losses.PairedFightCount)
            : 0;
        double? signedAssociationEdge = available
            ? BuildSignedAssociationEdge(
                wins.Difference,
                wins.AssociationScore,
                losses.Difference,
                losses.AssociationScore,
                squadPerspective)
            : null;
        double? separation = signedAssociationEdge.HasValue
            ? Math.Round(
                Math.Min(100.0, Math.Abs(signedAssociationEdge.Value)) * evidence / 100.0,
                3)
            : null;
        bool eligible = available &&
            wins.EligibleForRanking &&
            losses.EligibleForRanking &&
            wins.PairedFightCount >= MinimumPairedFights &&
            losses.PairedFightCount >= MinimumPairedFights;
        string direction = GetDirectionLabel(signedAssociationEdge, squadPerspective);

        return new HistoricalEffectivenessWinLossNamedEffectDto(
            Rank: null,
            Key: wins.Key,
            Name: wins.Name,
            EffectType: wins.EffectType,
            EffectId: wins.EffectId,
            Unit: wins.Unit,
            Available: available,
            UnavailableReason: available ? null : "The effect has no comparable estimate in the filtered win or loss cohort.",
            WinOutcomeAverage: wins.OutcomeAverage,
            WinBaselineAverage: wins.BaselineAverage,
            WinDifference: wins.Difference,
            WinPercentLift: wins.PercentLift,
            WinAssociationScore: wins.AssociationScore,
            WinEvidenceScore: wins.EvidenceScore,
            LossOutcomeAverage: losses.OutcomeAverage,
            LossBaselineAverage: losses.BaselineAverage,
            LossDifference: losses.Difference,
            LossPercentLift: losses.PercentLift,
            LossAssociationScore: losses.AssociationScore,
            LossEvidenceScore: losses.EvidenceScore,
            ResultEdge: resultEdge,
            Lower95ResultEdge: lower,
            Upper95ResultEdge: upper,
            SeparationScore: separation,
            EvidenceScore: evidence,
            ConfidenceLabel: GetConfidenceLabel(evidence),
            DirectionLabel: direction,
            RankingResult: eligible ? "Eligible for result-separation ranking." : "Insufficient paired win/loss evidence.",
            Detail: BuildDetail(
                wins.PairedFightCount,
                losses.PairedFightCount,
                resultEdge,
                lower,
                upper,
                wins.Unit,
                direction));
    }

    private static HistoricalEffectivenessWinLossMetricDto[] RankMetrics(
        IReadOnlyList<HistoricalEffectivenessWinLossMetricDto> metrics)
    {
        var ranks = metrics
            .Where(metric =>
                metric.Available &&
                metric.SeparationScore.HasValue &&
                !string.Equals(
                    metric.RankingResult,
                    "Insufficient paired win/loss evidence.",
                    StringComparison.Ordinal))
            .OrderByDescending(metric => metric.SeparationScore)
            .ThenByDescending(metric => metric.EvidenceScore)
            .Select((metric, index) => (metric.Key, Rank: index + 1))
            .ToDictionary(item => item.Key, item => item.Rank, StringComparer.Ordinal);
        return metrics
            .Select(metric =>
            {
                bool ranked = ranks.TryGetValue(metric.Key, out int rank);
                return metric with
                {
                    Rank = ranked ? rank : null,
                    RankingResult = ranked
                        ? "Ranked by result-separation score."
                        : metric.RankingResult
                };
            })
            .OrderBy(metric => metric.Rank ?? int.MaxValue)
            .ThenBy(metric => metric.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HistoricalEffectivenessWinLossNamedEffectDto[] RankNamedEffects(
        IReadOnlyList<HistoricalEffectivenessWinLossNamedEffectDto> effects)
    {
        var ranks = effects
            .Where(effect =>
                effect.Available &&
                effect.SeparationScore.HasValue &&
                !string.Equals(
                    effect.RankingResult,
                    "Insufficient paired win/loss evidence.",
                    StringComparison.Ordinal))
            .OrderByDescending(effect => effect.SeparationScore)
            .ThenByDescending(effect => effect.EvidenceScore)
            .Select((effect, index) => (effect.Key, Rank: index + 1))
            .ToDictionary(item => item.Key, item => item.Rank, StringComparer.Ordinal);
        return effects
            .Select(effect =>
            {
                bool ranked = ranks.TryGetValue(effect.Key, out int rank);
                return effect with
                {
                    Rank = ranked ? rank : null,
                    RankingResult = ranked
                        ? "Ranked by result-separation score."
                        : effect.RankingResult
                };
            })
            .OrderBy(effect => effect.Rank ?? int.MaxValue)
            .ThenBy(effect => effect.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static double BuildEvidence(
        double winEvidence,
        double lossEvidence,
        int winPairedFights,
        int lossPairedFights)
    {
        double sourceEvidence = Math.Min(winEvidence, lossEvidence);
        int largerCohort = Math.Max(winPairedFights, lossPairedFights);
        double balance = largerCohort > 0
            ? Math.Min(winPairedFights, lossPairedFights) / (double)largerCohort
            : 0;
        double support = Math.Min(
            1.0,
            Math.Min(winPairedFights, lossPairedFights) / 20.0);
        return Math.Round(
            Math.Clamp(sourceEvidence * (0.65 + 0.2 * balance + 0.15 * support), 0, 100),
            3);
    }

    private static double BuildSignedAssociationEdge(
        double? winDifference,
        double winAssociation,
        double? lossDifference,
        double lossAssociation,
        bool squadPerspective)
    {
        double signedWin = Math.Sign(winDifference ?? 0) * winAssociation;
        double signedLoss = Math.Sign(lossDifference ?? 0) * lossAssociation;
        return squadPerspective ? signedWin - signedLoss : signedLoss - signedWin;
    }

    private static (double? Lower, double? Upper) BuildResultEdgeInterval(
        double? winDifference,
        double? winLower,
        double? winUpper,
        double? lossDifference,
        double? lossLower,
        double? lossUpper,
        bool squadPerspective)
    {
        if (!winDifference.HasValue ||
            !winLower.HasValue ||
            !winUpper.HasValue ||
            !lossDifference.HasValue ||
            !lossLower.HasValue ||
            !lossUpper.HasValue)
        {
            return (null, null);
        }

        double winStandardError =
            Math.Max(0, winUpper.Value - winLower.Value) / (2 * Normal95CriticalValue);
        double lossStandardError =
            Math.Max(0, lossUpper.Value - lossLower.Value) / (2 * Normal95CriticalValue);
        double edge = squadPerspective
            ? winDifference.Value - lossDifference.Value
            : lossDifference.Value - winDifference.Value;
        double margin = Normal95CriticalValue *
            Math.Sqrt(winStandardError * winStandardError + lossStandardError * lossStandardError);
        return (Math.Round(edge - margin, 6), Math.Round(edge + margin, 6));
    }

    private static string GetDirectionLabel(double? signedAssociationEdge, bool squadPerspective)
    {
        if (!signedAssociationEdge.HasValue ||
            Math.Abs(signedAssociationEdge.Value) < DirectionThreshold)
        {
            return "Similar in wins and losses";
        }
        if (signedAssociationEdge.Value > 0)
        {
            return squadPerspective ? "Stronger in wins" : "Stronger in squad losses";
        }
        return squadPerspective ? "Stronger in losses" : "Stronger in squad wins";
    }

    private static string GetConfidenceLabel(double evidence) => evidence switch
    {
        >= 70 => "High",
        >= 45 => "Medium",
        _ => "Limited",
    };

    private static string BuildDetail(
        int winPairedFights,
        int lossPairedFights,
        double? resultEdge,
        double? lower,
        double? upper,
        string unit,
        string direction)
    {
        string interval = lower.HasValue && upper.HasValue
            ? $" Approximate 95% result-edge interval: {lower.Value:N3} to {upper.Value:N3} {unit}."
            : string.Empty;
        return $"{winPairedFights:N0} paired win fights and {lossPairedFights:N0} paired loss fights. {direction}. Result edge: {(resultEdge.HasValue ? resultEdge.Value.ToString("N3") : "n/a")} {unit}.{interval}";
    }
}
