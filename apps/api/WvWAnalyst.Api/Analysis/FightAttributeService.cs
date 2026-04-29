using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class FightAttributeService
{
    private static readonly FightAttributeDefinitionDto[] AttributeDefinitions =
    [
        new("even-numbers", "Even Numbers", "Numbers", "Squad and enemy player counts were close enough to treat the fight as even."),
        new("enemy-larger", "Enemy Larger", "Numbers", "Enemy player count was materially higher than the squad side."),
        new("enemy-much-larger", "Enemy Much Larger", "Numbers", "Enemy player count was heavily higher than the squad side."),
        new("squad-larger", "Squad Larger", "Numbers", "Squad-side player count was materially higher than the enemy side."),
        new("squad-much-larger", "Squad Much Larger", "Numbers", "Squad-side player count was heavily higher than the enemy side."),
        new("successful-bomb", "Successful Bomb", "Bombs", "Squad burst pressure produced downs or kills."),
        new("failed-bomb", "Failed Bomb", "Bombs", "Squad burst pressure or strips landed without meaningful down or kill conversion."),
        new("enemy-bomb-landed", "Enemy Bomb Landed", "Bombs", "Enemy pressure produced a meaningful squad down or death spike."),
        new("enemy-bomb-survived", "Enemy Bomb Survived", "Bombs", "The squad absorbed a meaningful enemy down spike without collapsing."),
        new("high-conversion", "High Conversion", "Conversion", "Enemy downs converted into kills at a high rate."),
        new("low-conversion", "Low Conversion", "Conversion", "Enemy downs did not convert into kills at a healthy rate."),
        new("high-recovery", "High Recovery", "Conversion", "Squad downs recovered at a high rate."),
        new("poor-recovery", "Poor Recovery", "Conversion", "Squad downs recovered at a low rate."),
        new("sustain-grind", "Sustain Grind", "Fight Shape", "Long fight with extended pressure and stabilization demands."),
        new("fast-collapse", "Fast Collapse", "Fight Shape", "Short fight where the squad lost players quickly."),
        new("cleanup", "Cleanup", "Fight Shape", "Squad-favored fight with strong kill control and limited squad losses."),
        new("no-decision", "No Decision", "Fight Shape", "Fight ended without a decisive down, kill, or winner signal."),
        new("three-way", "3-Way", "Fight Shape", "Two distinct enemy teams were detected in the same fight."),
        new("organized-enemy", "Organized Enemy", "Fight Shape", "Enemy movement tightness scored in the organized range or better."),
        new("elite-tight-enemy", "Elite Tight Enemy", "Fight Shape", "Enemy movement tightness scored in the elite range."),
        new("tight-enemy", "Tight Enemy", "Fight Shape", "Enemy movement tightness scored in the tight range."),
        new("cloudy-fight", "Cloudy Fight", "Fight Shape", "Enemy formation was detected as loose or cloud-like."),
        new("stack-clash", "Stack Clash", "Fight Shape", "Enemy formation was detected as stack-like or ball-like."),
        new("low-confidence", "Low Confidence", "Data Quality", "Important fight signals were missing or exported with low confidence.")
    ];

    private static readonly IReadOnlyDictionary<string, FightAttributeDefinitionDto> DefinitionLookup =
        AttributeDefinitions.ToDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FightAttributeDefinitionDto> GetDefinitions() => AttributeDefinitions;

    public IReadOnlyList<FightAttributeDto> BuildAttributes(FightIndexDto? fightIndex)
    {
        if (fightIndex is null)
        {
            return [Create("low-confidence", 1.0, 3, "No compact fight index is available.")];
        }

        var attributes = new List<FightAttributeDto>();
        AddNumbersAttributes(attributes, fightIndex);
        AddBombAttributes(attributes, fightIndex);
        AddConversionAttributes(attributes, fightIndex);
        AddFightShapeAttributes(attributes, fightIndex);
        AddDataQualityAttributes(attributes, fightIndex);

        return attributes
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(attribute => attribute.Confidence).ThenByDescending(attribute => attribute.Severity).First())
            .OrderBy(attribute => DefinitionLookup.TryGetValue(attribute.Key, out var definition) ? definition.Group : attribute.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(attribute => attribute.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddNumbersAttributes(List<FightAttributeDto> attributes, FightIndexDto fightIndex)
    {
        var squadSize = GetSquadSize(fightIndex);
        var enemySize = GetEnemySize(fightIndex);
        if (squadSize <= 0 || enemySize <= 0)
        {
            attributes.Add(Create("low-confidence", 0.8, 2, "Missing squad or enemy player count for number context."));
            return;
        }

        var larger = Math.Max(squadSize, enemySize);
        var evenTolerance = Math.Max(3.0, larger * 0.15);
        var muchLargerThreshold = Math.Max(8.0, larger * 0.35);
        var difference = squadSize - enemySize;
        var detail = $"{Math.Round(squadSize, 1)} squad-side vs {Math.Round(enemySize, 1)} enemy players.";

        if (Math.Abs(difference) <= evenTolerance)
        {
            attributes.Add(Create("even-numbers", 0.88, 1, detail));
            return;
        }

        if (difference < 0)
        {
            attributes.Add(Math.Abs(difference) >= muchLargerThreshold
                ? Create("enemy-much-larger", 0.88, 3, detail)
                : Create("enemy-larger", 0.84, 2, detail));
            return;
        }

        attributes.Add(difference >= muchLargerThreshold
            ? Create("squad-much-larger", 0.88, 3, detail)
            : Create("squad-larger", 0.84, 2, detail));
    }

    private static void AddBombAttributes(List<FightAttributeDto> attributes, FightIndexDto fightIndex)
    {
        var outcome = fightIndex.Execution?.Outcome;
        var topBursts = fightIndex.TopBursts ?? Array.Empty<FightTopBurstIndexDto>();
        var enemySize = Math.Max(1.0, GetEnemySize(fightIndex));
        var squadSize = Math.Max(1.0, GetSquadSize(fightIndex));
        var pressureScore = GetPillarScore(fightIndex, "pressure-burst");
        var bestBurst = topBursts
            .OrderByDescending(burst => burst.Downs * 4 + burst.Kills * 6)
            .ThenByDescending(burst => burst.Damage)
            .FirstOrDefault();

        var enemyDowns = outcome?.EnemyDowns ?? fightIndex.EnemySide?.Downs ?? 0;
        var enemyKills = outcome?.EnemyKills ?? fightIndex.EnemySide?.Kills ?? 0;
        var squadDowns = outcome?.SquadDowns ?? fightIndex.SquadSide?.Downs ?? 0;
        var squadDeaths = outcome?.SquadDeaths ?? fightIndex.SquadSide?.Deaths ?? 0;

        var successfulBombDownThreshold = Math.Max(3, (int)Math.Ceiling(enemySize * 0.10));
        if (bestBurst is not null && (bestBurst.Downs >= 3 || bestBurst.Kills >= 2 || bestBurst.Downs + bestBurst.Kills >= 4))
        {
            attributes.Add(Create("successful-bomb", 0.85, 3, $"Top retained burst produced {bestBurst.Downs} downs and {bestBurst.Kills} kills."));
        }
        else if (enemyDowns >= successfulBombDownThreshold && pressureScore >= 60)
        {
            attributes.Add(Create("successful-bomb", 0.72, 2, $"{enemyDowns} enemy downs with pressure score {pressureScore}."));
        }

        var burstDamageThreshold = Math.Max(120_000, enemySize * 6_000);
        var failedBurst = topBursts.FirstOrDefault(burst =>
            burst.Downs == 0
            && burst.Kills == 0
            && (burst.Damage >= burstDamageThreshold || burst.Strips >= 12));
        if (failedBurst is not null)
        {
            attributes.Add(Create("failed-bomb", 0.76, 2, $"Retained burst did {failedBurst.Damage:n0} damage and {failedBurst.Strips:n0} strips without downs or kills."));
        }
        else if (pressureScore >= 65 && enemyDowns >= 3 && GetEnemyDownConversionRate(fightIndex) <= 35)
        {
            attributes.Add(Create("failed-bomb", 0.68, 2, $"Pressure score {pressureScore} with {enemyDowns} enemy downs but low conversion."));
        }

        var enemyBombDownThreshold = Math.Max(3, (int)Math.Ceiling(squadSize * 0.10));
        if (squadDowns >= enemyBombDownThreshold && squadDeaths >= Math.Max(2, squadDowns / 2))
        {
            attributes.Add(Create("enemy-bomb-landed", 0.78, 3, $"{squadDowns} squad downs and {squadDeaths} deaths under enemy pressure."));
        }

        var recoveryRate = outcome?.SquadRecoveryRate ?? 0;
        if (squadDowns >= enemyBombDownThreshold && recoveryRate >= 65 && squadDeaths <= Math.Max(2, (int)Math.Ceiling(squadDowns * 0.35)))
        {
            attributes.Add(Create("enemy-bomb-survived", 0.78, 2, $"{squadDowns} squad downs with {recoveryRate:n1}% recovery."));
        }

        if (enemyDowns > 0 && enemyKills > 0 && enemyKills >= enemyDowns && pressureScore >= 60)
        {
            attributes.Add(Create("successful-bomb", 0.68, 2, $"{enemyKills} enemy kills from {enemyDowns} downs."));
        }
    }

    private static void AddConversionAttributes(List<FightAttributeDto> attributes, FightIndexDto fightIndex)
    {
        var outcome = fightIndex.Execution?.Outcome;
        var enemyDowns = outcome?.EnemyDowns ?? fightIndex.EnemySide?.Downs ?? 0;
        var squadDowns = outcome?.SquadDowns ?? fightIndex.SquadSide?.Downs ?? 0;
        var enemyConversion = GetEnemyDownConversionRate(fightIndex);
        var squadRecovery = outcome?.SquadRecoveryRate ?? 0;

        if (enemyDowns >= 3 && enemyConversion >= 75)
        {
            attributes.Add(Create("high-conversion", 0.86, 3, $"{enemyConversion:n1}% enemy down conversion from {enemyDowns} downs."));
        }
        else if (enemyDowns >= 3 && enemyConversion <= 35)
        {
            attributes.Add(Create("low-conversion", 0.82, 2, $"{enemyConversion:n1}% enemy down conversion from {enemyDowns} downs."));
        }

        if (squadDowns >= 3 && squadRecovery >= 70)
        {
            attributes.Add(Create("high-recovery", 0.84, 2, $"{squadRecovery:n1}% squad recovery from {squadDowns} downs."));
        }
        else if (squadDowns >= 3 && squadRecovery <= 35)
        {
            attributes.Add(Create("poor-recovery", 0.82, 3, $"{squadRecovery:n1}% squad recovery from {squadDowns} downs."));
        }
    }

    private static void AddFightShapeAttributes(List<FightAttributeDto> attributes, FightIndexDto fightIndex)
    {
        var outcome = fightIndex.Execution?.Outcome;
        var durationSeconds = (fightIndex.DurationMilliseconds ?? 0) / 1000.0;
        var totalKills = (outcome?.EnemyKills ?? fightIndex.EnemySide?.Kills ?? 0)
            + (outcome?.SquadDeaths ?? fightIndex.SquadSide?.Deaths ?? 0);
        var squadDeaths = outcome?.SquadDeaths ?? fightIndex.SquadSide?.Deaths ?? 0;
        var enemyKills = outcome?.EnemyKills ?? fightIndex.EnemySide?.Kills ?? 0;
        var squadSize = Math.Max(1.0, GetSquadSize(fightIndex));

        if (durationSeconds >= 180)
        {
            attributes.Add(Create("sustain-grind", 0.74, 1, $"Fight lasted {Math.Round(durationSeconds)} seconds."));
        }

        if (durationSeconds > 0
            && durationSeconds <= 75
            && string.Equals(fightIndex.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase)
            && squadDeaths >= Math.Max(3, (int)Math.Ceiling(squadSize * 0.15)))
        {
            attributes.Add(Create("fast-collapse", 0.8, 3, $"{squadDeaths} squad deaths in {Math.Round(durationSeconds)} seconds."));
        }

        if (string.Equals(fightIndex.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase)
            && enemyKills >= 5
            && squadDeaths <= Math.Max(1, (int)Math.Ceiling(squadSize * 0.05)))
        {
            attributes.Add(Create("cleanup", 0.72, 2, $"{enemyKills} enemy kills with {squadDeaths} squad deaths."));
        }

        if ((string.Equals(fightIndex.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fightIndex.Outcome.OutcomeCode, "unknown", StringComparison.OrdinalIgnoreCase))
            && totalKills <= 2)
        {
            attributes.Add(Create("no-decision", 0.82, 1, "No decisive winner and very low kill volume."));
        }

        var context = fightIndex.Execution?.Context;
        if (context?.ThreeWayDetected == true)
        {
            attributes.Add(Create("three-way", 0.86, 1, context.ThreeWayDetail ?? "Two distinct enemy teams were detected in the same fight."));
        }

        if (context?.EnemyMovementScore is int enemyMovementScore)
        {
            string enemyMovementDetail = context.EnemyMovementScoreDetail
                ?? $"Enemy movement scored {enemyMovementScore}/100.";
            if (enemyMovementScore >= 72)
            {
                attributes.Add(Create("organized-enemy", 0.84, 1, enemyMovementDetail));
            }

            if (enemyMovementScore >= 92)
            {
                attributes.Add(Create("elite-tight-enemy", 0.9, 2, enemyMovementDetail));
            }
            else if (enemyMovementScore >= 84)
            {
                attributes.Add(Create("tight-enemy", 0.86, 1, enemyMovementDetail));
            }
        }

        var formation = fightIndex.Execution?.Context?.EnemyFormationStyleLabel ?? string.Empty;
        if (formation.Contains("cloud", StringComparison.OrdinalIgnoreCase)
            || formation.Contains("loose", StringComparison.OrdinalIgnoreCase))
        {
            attributes.Add(Create("cloudy-fight", 0.72, 1, $"Enemy formation: {formation}."));
        }
        else if (formation.Contains("stack", StringComparison.OrdinalIgnoreCase)
            || formation.Contains("ball", StringComparison.OrdinalIgnoreCase)
            || formation.Contains("tight", StringComparison.OrdinalIgnoreCase))
        {
            attributes.Add(Create("stack-clash", 0.72, 1, $"Enemy formation: {formation}."));
        }
    }

    private static void AddDataQualityAttributes(List<FightAttributeDto> attributes, FightIndexDto fightIndex)
    {
        if (!fightIndex.DetailedWvW || fightIndex.Execution?.ScoreAvailable != true)
        {
            attributes.Add(Create("low-confidence", 0.9, 3, "Detailed WvW scoring data is unavailable."));
            return;
        }

        var confidenceLabel = fightIndex.Execution.ConfidenceLabel ?? fightIndex.Execution.Context?.DataConfidenceLabel;
        if (confidenceLabel?.Contains("low", StringComparison.OrdinalIgnoreCase) == true)
        {
            attributes.Add(Create("low-confidence", 0.82, 2, $"Execution confidence: {confidenceLabel}."));
        }
    }

    private static double GetSquadSize(FightIndexDto fightIndex)
    {
        var effectiveAlliedCount = fightIndex.SquadSide?.EffectiveAlliedPlayerCount ?? 0;
        if (effectiveAlliedCount > 0)
        {
            return effectiveAlliedCount;
        }

        var directCount = fightIndex.SquadSide?.PlayerCount ?? fightIndex.SquadPlayerCount;
        return directCount + Math.Max(0, fightIndex.FriendlyNonSquadCount);
    }

    private static double GetEnemySize(FightIndexDto fightIndex)
    {
        if ((fightIndex.EnemySide?.PlayerCount ?? 0) > 0)
        {
            return fightIndex.EnemySide!.PlayerCount;
        }

        return fightIndex.EnemyPlayerCount > 0
            ? fightIndex.EnemyPlayerCount
            : fightIndex.EnemyTargetCount;
    }

    private static int GetPillarScore(FightIndexDto fightIndex, string pillarId)
    {
        return (fightIndex.Execution?.Pillars ?? Array.Empty<FightExecutionPillarIndexDto>())
            .FirstOrDefault(pillar => string.Equals(pillar.PillarId, pillarId, StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;
    }

    private static double GetEnemyDownConversionRate(FightIndexDto fightIndex)
    {
        if (fightIndex.Execution?.Outcome is { } outcome)
        {
            return outcome.EnemyDownConversionRate;
        }

        return fightIndex.SquadSide?.DownKillConversionRate ?? 0;
    }

    private static FightAttributeDto Create(string key, double confidence, int severity, string detail)
    {
        var definition = DefinitionLookup[key];
        return new FightAttributeDto(
            Key: definition.Key,
            Label: definition.Label,
            Group: definition.Group,
            Confidence: Math.Round(Math.Clamp(confidence, 0, 1), 2),
            Severity: Math.Clamp(severity, 1, 3),
            Detail: detail);
    }
}
