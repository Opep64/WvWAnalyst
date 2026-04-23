using System.Text.Json;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Bridge;

public sealed class EliteInsightsFightIndexer
{
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public FightIndexDto? TryIndexFight(string? analystJsonPath, string? eliteInsightsJsonPath)
    {
        var analystIndex = TryIndexAnalystFight(analystJsonPath);
        if (analystIndex is not null)
        {
            return analystIndex;
        }

        return TryIndexEliteInsightsFight(eliteInsightsJsonPath);
    }

    public FightIndexDto? TryIndexFight(string jsonPath)
    {
        return TryIndexFight(analystJsonPath: null, eliteInsightsJsonPath: jsonPath);
    }

    private FightIndexDto? TryIndexAnalystFight(string? analystJsonPath)
    {
        if (string.IsNullOrWhiteSpace(analystJsonPath) || !File.Exists(analystJsonPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(analystJsonPath);
            var payload = JsonSerializer.Deserialize<WvWAnalystFightPayloadDto>(stream, PayloadSerializerOptions);
            if (payload is null)
            {
                return null;
            }

            var fightName = BuildFightName(payload);
            if (string.IsNullOrWhiteSpace(fightName))
            {
                return null;
            }

            var squad = payload.Sides?.Squad ?? new WvWAnalystSideDto();
            var enemy = payload.Sides?.Enemy ?? new WvWAnalystSideDto();
            var commanderDisplayNames = BuildCommanderDisplayNames(payload);
            var outcome = BuildOutcomeFromAnalystPayload(payload);
            var execution = BuildExecutionFromAnalystPayload(payload);

            return new FightIndexDto(
                FightName: fightName,
                EncounterName: NullIfWhiteSpace(payload.Fight.EncounterLabel),
                EliteInsightsVersion: NullIfWhiteSpace(payload.Meta.ParserVersion),
                AnalystSchemaVersion: NullIfWhiteSpace(payload.Meta.SchemaVersion),
                IndexedFrom: "analysis-json",
                TriggerId: 0,
                MapId: null,
                DetailedWvW: string.Equals(payload.Fight.Mode, "wvw_detailed", StringComparison.OrdinalIgnoreCase),
                Success: string.Equals(outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase),
                Outcome: outcome,
                SquadSide: BuildSideFromAnalystPayload(squad),
                EnemySide: BuildSideFromAnalystPayload(enemy),
                CommanderSummary: BuildCommanderSummaryFromAnalystPayload(payload.CommanderSummary),
                ThreatBoons: BuildThreatBoonsFromAnalystPayload(payload.ThreatBoons),
                Players: BuildPlayersFromAnalystPayload(payload.Players),
                Execution: execution,
                Duration: BuildDurationLabel(payload.Fight.DurationMs),
                DurationMilliseconds: payload.Fight.DurationMs,
                TimeStart: payload.Fight.StartTimeUtc ?? string.Empty,
                TimeEnd: payload.Fight.EndTimeUtc ?? string.Empty,
                TimeStartStandard: NullIfWhiteSpace(payload.Fight.StartTimeUtc),
                TimeEndStandard: NullIfWhiteSpace(payload.Fight.EndTimeUtc),
                PlayerCount: Math.Max(0, squad.PlayerCount + squad.FriendlyNonSquadCount),
                SquadPlayerCount: squad.PlayerCount,
                FriendlyNonSquadCount: squad.FriendlyNonSquadCount,
                EnemyTargetCount: enemy.PlayerCount,
                EnemyPlayerCount: enemy.PlayerCount,
                CommanderCount: commanderDisplayNames.Count,
                FriendlyTeamId: null,
                EnemyTeamIds: Array.Empty<int>(),
                CommanderDisplayNames: commanderDisplayNames,
                ActiveExtensions: Array.Empty<string>(),
                ArcVersion: null,
                GW2Build: null,
                RecordedBy: null,
                RecordedAccountBy: null);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private FightIndexDto? TryIndexEliteInsightsFight(string? eliteInsightsJsonPath)
    {
        if (string.IsNullOrWhiteSpace(eliteInsightsJsonPath) || !File.Exists(eliteInsightsJsonPath))
        {
            return null;
        }

        using var stream = File.OpenRead(eliteInsightsJsonPath);
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        var fightName = GetString(root, "fightName") ?? GetString(root, "name");
        if (string.IsNullOrWhiteSpace(fightName))
        {
            return null;
        }

        var playerCount = 0;
        var squadPlayerCount = 0;
        var friendlyNonSquadCount = 0;
        var commanderCount = 0;
        int? friendlyTeamId = null;
        var commanderNames = new List<string>();

        if (TryGetArray(root, "players", out var players))
        {
            foreach (var player in players.EnumerateArray())
            {
                playerCount++;

                if (friendlyTeamId is null && TryGetInt(player, "teamID", out var playerTeamId))
                {
                    friendlyTeamId = playerTeamId;
                }

                if (GetBool(player, "notInSquad") == true)
                {
                    friendlyNonSquadCount++;
                }
                else
                {
                    squadPlayerCount++;
                }

                if (GetBool(player, "hasCommanderTag") == true)
                {
                    commanderCount++;
                    commanderNames.Add(BuildCommanderDisplayName(player));
                }
            }
        }

        var enemyTargetCount = 0;
        var enemyPlayerCount = 0;
        var enemyTeamIds = new SortedSet<int>();

        if (TryGetArray(root, "targets", out var targets))
        {
            foreach (var target in targets.EnumerateArray())
            {
                enemyTargetCount++;

                if (GetBool(target, "enemyPlayer") == true)
                {
                    enemyPlayerCount++;
                }

                if (TryGetInt(target, "teamID", out var targetTeamId) &&
                    targetTeamId != 0 &&
                    (!friendlyTeamId.HasValue || targetTeamId != friendlyTeamId.Value))
                {
                    enemyTeamIds.Add(targetTeamId);
                }
            }
        }

        var activeExtensions = new List<string>();
        if (TryGetArray(root, "usedExtensions", out var extensions))
        {
            foreach (var extension in extensions.EnumerateArray())
            {
                var extensionName = GetString(extension, "name");
                if (!string.IsNullOrWhiteSpace(extensionName))
                {
                    activeExtensions.Add(extensionName);
                }
            }
        }

        var detailedWvw = GetBool(root, "detailedWvW") ?? false;

        return new FightIndexDto(
            FightName: fightName,
            EncounterName: GetString(root, "encounterName") ?? GetString(root, "name"),
            EliteInsightsVersion: GetString(root, "eliteInsightsVersion"),
            AnalystSchemaVersion: null,
            IndexedFrom: "elite-insights-json",
            TriggerId: GetInt(root, "triggerID") ?? 0,
            MapId: GetInt(root, "mapID"),
            DetailedWvW: detailedWvw,
            Success: GetBool(root, "success") ?? false,
            Outcome: BuildUnavailableOutcome(
                detailedWvw
                    ? "Current EI JSON does not export the WvW summary winner. Outcome will become available once the parser emits analysis.json."
                    : "This indexed fight does not have an analyst outcome payload yet."),
            SquadSide: null,
            EnemySide: null,
            CommanderSummary: null,
            ThreatBoons: Array.Empty<FightThreatBoonIndexDto>(),
            Players: Array.Empty<FightPlayerIndexDto>(),
            Execution: null,
            Duration: GetString(root, "duration") ?? "Unknown",
            DurationMilliseconds: GetLong(root, "durationMS"),
            TimeStart: GetString(root, "timeStart") ?? string.Empty,
            TimeEnd: GetString(root, "timeEnd") ?? string.Empty,
            TimeStartStandard: GetString(root, "timeStartStd"),
            TimeEndStandard: GetString(root, "timeEndStd"),
            PlayerCount: playerCount,
            SquadPlayerCount: squadPlayerCount,
            FriendlyNonSquadCount: friendlyNonSquadCount,
            EnemyTargetCount: enemyTargetCount,
            EnemyPlayerCount: enemyPlayerCount,
            CommanderCount: commanderCount,
            FriendlyTeamId: friendlyTeamId,
            EnemyTeamIds: enemyTeamIds.ToArray(),
            CommanderDisplayNames: commanderNames,
            ActiveExtensions: activeExtensions,
            ArcVersion: GetString(root, "arcVersion"),
            GW2Build: GetInt(root, "gW2Build"),
            RecordedBy: GetString(root, "recordedBy"),
            RecordedAccountBy: GetString(root, "recordedAccountBy"));
    }

    private static string BuildFightName(WvWAnalystFightPayloadDto payload)
    {
        var encounterLabel = NullIfWhiteSpace(payload.Fight.EncounterLabel);
        var mapLabel = NullIfWhiteSpace(payload.Fight.MapLabel);

        if (!string.IsNullOrWhiteSpace(encounterLabel) && !string.IsNullOrWhiteSpace(mapLabel) &&
            !encounterLabel.Contains(mapLabel, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(encounterLabel, mapLabel, StringComparison.OrdinalIgnoreCase))
        {
            return $"{encounterLabel} - {mapLabel}";
        }

        return encounterLabel
            ?? mapLabel
            ?? NullIfWhiteSpace(payload.Fight.FightId)
            ?? string.Empty;
    }

    private static IReadOnlyList<string> BuildCommanderDisplayNames(WvWAnalystFightPayloadDto payload)
    {
        var commander = payload.Sides?.Squad?.Commander;
        if (commander is null)
        {
            return Array.Empty<string>();
        }

        var character = NullIfWhiteSpace(commander.Character);
        var account = NullIfWhiteSpace(commander.Account);

        if (character is null && account is null)
        {
            return Array.Empty<string>();
        }

        if (character is not null && account is not null)
        {
            return [$"{character} ({account})"];
        }

        return [character ?? account!];
    }

    private static FightOutcomeDto BuildOutcomeFromAnalystPayload(WvWAnalystFightPayloadDto payload)
    {
        var outcomeCode = NormalizeOutcomeCode(payload.Outcome.OutcomeCode);
        var winnerSideId = NormalizeWinnerSideId(payload.Outcome.WinnerSideId, outcomeCode);
        var decidedBy = NormalizeDecider(payload.Outcome.DecidedBy);
        var displayLabel = BuildOutcomeDisplayLabel(outcomeCode, winnerSideId, payload);
        var detail = BuildOutcomeDetailFromAnalystPayload(payload, outcomeCode, decidedBy);

        return new FightOutcomeDto(
            OutcomeCode: outcomeCode,
            WinnerSideId: winnerSideId,
            DisplayLabel: displayLabel,
            DecidedBy: decidedBy,
            Source: "analysis-json",
            Detail: detail);
    }

    private static FightSideIndexDto? BuildSideFromAnalystPayload(WvWAnalystSideDto? side)
    {
        if (side is null)
        {
            return null;
        }

        return new FightSideIndexDto(
            SideId: NullIfWhiteSpace(side.SideId) ?? string.Empty,
            DisplayLabel: NullIfWhiteSpace(side.DisplayLabel) ?? string.Empty,
            PlayerCount: side.PlayerCount,
            FriendlyNonSquadCount: side.FriendlyNonSquadCount,
            EffectiveAlliedPlayerCount: side.EffectiveAlliedPlayerCount,
            Dps: side.Totals?.Dps ?? 0,
            Downs: side.Totals?.Downs ?? 0,
            Kills: side.Totals?.Kills ?? 0,
            DownKillConversionRate: side.Totals?.DownKillConversionRate ?? 0,
            Cleanses: side.Totals?.Cleanses ?? 0,
            Resurrects: side.Totals?.Resurrects ?? 0,
            Deaths: side.Totals?.Deaths ?? 0,
            Damage: side.Totals?.Damage ?? 0,
            DamageTaken: side.Totals?.DamageTaken ?? 0,
            Strips: side.Totals?.Strips ?? 0,
            ReceivedCrowdControl: side.Totals?.ReceivedCrowdControl ?? 0,
            StripsPerMinute: side.Totals?.StripsPerMinute ?? 0,
            CleansesPerMinute: side.Totals?.CleansesPerMinute ?? 0);
    }

    private static FightCommanderIndexDto? BuildCommanderSummaryFromAnalystPayload(WvWAnalystCommanderSummaryDto? summary)
    {
        if (summary is null || (!summary.Available && summary.ActorId == 0))
        {
            return null;
        }

        return new FightCommanderIndexDto(
            ActorId: summary.ActorId,
            Available: summary.Available,
            SquadPositioningSamples: summary.SquadPositioningSamples,
            SquadInPositionRate: summary.SquadInPositionRate,
            SquadTooFarRate: summary.SquadTooFarRate,
            SquadOverextendedRate: summary.SquadOverextendedRate,
            SquadLateralRiskRate: summary.SquadLateralRiskRate,
            CohesionPillarScore: summary.CohesionPillarScore,
            CohesionPillarSummary: NullIfWhiteSpace(summary.CohesionPillarSummary),
            FitSummary: NullIfWhiteSpace(summary.FitSummary),
            DemandFitSummary: NullIfWhiteSpace(summary.DemandFitSummary),
            ContributionProfile: NullIfWhiteSpace(summary.ContributionProfile),
            KeyContributionSummary: NullIfWhiteSpace(summary.KeyContributionSummary),
            EvaluationConfidenceLabel: NullIfWhiteSpace(summary.EvaluationConfidenceLabel),
            EvaluationConfidenceDetail: NullIfWhiteSpace(summary.EvaluationConfidenceDetail),
            EvaluationCaveats: summary.EvaluationCaveats?
                .Select(NullIfWhiteSpace)
                .Where(value => value is not null)
                .Select(value => value!)
                .ToArray()
                ?? Array.Empty<string>());
    }

    private static IReadOnlyList<FightPlayerIndexDto> BuildPlayersFromAnalystPayload(IReadOnlyList<WvWAnalystPlayerSummaryDto>? players)
    {
        if (players is null || players.Count == 0)
        {
            return Array.Empty<FightPlayerIndexDto>();
        }

        return players
            .Where(player => player.ActorId != 0 || !string.IsNullOrWhiteSpace(player.Character) || !string.IsNullOrWhiteSpace(player.Account))
            .Select(player => new FightPlayerIndexDto(
                ActorId: player.ActorId,
                Account: NullIfWhiteSpace(player.Account),
                Character: NullIfWhiteSpace(player.Character),
                Profession: NullIfWhiteSpace(player.Profession),
                EliteSpec: NullIfWhiteSpace(player.EliteSpec),
                Icon: NullIfWhiteSpace(player.Icon),
                Group: player.Group,
                IsCommander: player.IsCommander,
                ActiveSeconds: player.ActiveSeconds,
                CombatSeconds: player.CombatSeconds,
                Damage: player.Damage,
                Downs: player.Downs,
                Kills: player.Kills,
                Strips: player.Strips,
                OutgoingCleanses: player.OutgoingCleanses,
                Healing: player.Healing,
                Barrier: player.Barrier,
                Resurrects: player.Resurrects,
                Deaths: player.Deaths,
                Recoveries: player.Recoveries,
                DamageTaken: player.DamageTaken,
                ReceivedCrowdControl: player.ReceivedCrowdControl,
                HasPositioningData: player.HasPositioningData,
                PositioningSamples: player.PositioningSamples,
                InPositionRate: player.InPositionRate,
                TooFarRate: player.TooFarRate,
                OverextendedRate: player.OverextendedRate,
                LateralRiskRate: player.LateralRiskRate,
                FitSummary: NullIfWhiteSpace(player.FitSummary),
                DemandFitSummary: NullIfWhiteSpace(player.DemandFitSummary),
                ContributionProfile: NullIfWhiteSpace(player.ContributionProfile),
                KeyContributionSummary: NullIfWhiteSpace(player.KeyContributionSummary),
                EvaluationConfidenceLabel: NullIfWhiteSpace(player.EvaluationConfidenceLabel),
                EvaluationConfidenceDetail: NullIfWhiteSpace(player.EvaluationConfidenceDetail),
                EvaluationCaveats: player.EvaluationCaveats?
                    .Select(NullIfWhiteSpace)
                    .Where(value => value is not null)
                    .Select(value => value!)
                    .ToArray()
                    ?? Array.Empty<string>(),
                EvidenceSnapshot: player.EvidenceSnapshot?
                    .Select(NullIfWhiteSpace)
                    .Where(value => value is not null)
                    .Select(value => value!)
                    .ToArray()
                    ?? Array.Empty<string>(),
                RoleMix: player.RoleMix?
                    .Where(role => !string.IsNullOrWhiteSpace(role.Label))
                    .Select(role => new FightPlayerRoleMixIndexDto(
                        Label: role.Label.Trim(),
                        Percent: role.Percent))
                    .ToArray()
                    ?? Array.Empty<FightPlayerRoleMixIndexDto>(),
                Lanes: player.Lanes?
                    .Where(lane => !string.IsNullOrWhiteSpace(lane.Label))
                    .Select(lane => new FightPlayerLaneIndexDto(
                        Key: NullIfWhiteSpace(lane.Key) ?? string.Empty,
                        Label: lane.Label.Trim(),
                        StrengthPercent: lane.StrengthPercent,
                        SharePercent: lane.SharePercent,
                        WindowsHit: lane.WindowsHit,
                        WindowsTotal: lane.WindowsTotal,
                        WindowLabel: NullIfWhiteSpace(lane.WindowLabel),
                        RateBand: NullIfWhiteSpace(lane.RateBand),
                        EvidenceLine: NullIfWhiteSpace(lane.EvidenceLine),
                        Metrics: lane.Metrics?
                            .Where(metric => !string.IsNullOrWhiteSpace(metric.Label))
                            .Select(metric => new FightPlayerLaneMetricIndexDto(
                                Key: NullIfWhiteSpace(metric.Key) ?? string.Empty,
                                Label: metric.Label.Trim(),
                                Value: metric.Value,
                                Unit: NullIfWhiteSpace(metric.Unit),
                                Aggregation: NullIfWhiteSpace(metric.Aggregation)))
                            .ToArray()
                            ?? Array.Empty<FightPlayerLaneMetricIndexDto>()))
                    .ToArray()
                    ?? Array.Empty<FightPlayerLaneIndexDto>(),
                ThreatBoons: player.ThreatBoons?
                    .Where(boon => !string.IsNullOrWhiteSpace(boon.Name))
                    .Select(boon => new FightPlayerThreatBoonIndexDto(
                        Id: boon.Id,
                        Name: boon.Name.Trim(),
                        Icon: NullIfWhiteSpace(boon.Icon),
                        StackBased: boon.StackBased,
                        TracksOverapplication: boon.TracksOverapplication,
                        ThreatenedSamples: boon.ThreatenedSamples,
                        ActiveThreatSamples: boon.ActiveThreatSamples,
                        ThreatStackTotal: boon.ThreatStackTotal,
                        OverappliedThreatSamples: boon.OverappliedThreatSamples,
                        Coverage: boon.Coverage,
                        AverageStacks: boon.AverageStacks,
                        Overapplication: boon.Overapplication))
                    .ToArray()
                    ?? Array.Empty<FightPlayerThreatBoonIndexDto>(),
                ProvidedBoons: player.ProvidedBoons?
                    .Where(boon => !string.IsNullOrWhiteSpace(boon.Name))
                    .Select(boon => new FightPlayerProvidedBoonIndexDto(
                        Id: boon.Id,
                        Name: boon.Name.Trim(),
                        Icon: NullIfWhiteSpace(boon.Icon),
                        StackBased: boon.StackBased,
                        Generation: boon.Generation,
                        GenerationPresence: boon.GenerationPresence,
                        Overstack: boon.Overstack))
                    .ToArray()
                    ?? Array.Empty<FightPlayerProvidedBoonIndexDto>()))
            .ToArray();
    }

    private static IReadOnlyList<FightThreatBoonIndexDto> BuildThreatBoonsFromAnalystPayload(IReadOnlyList<WvWAnalystThreatBoonSummaryDto>? boons)
    {
        if (boons is null || boons.Count == 0)
        {
            return Array.Empty<FightThreatBoonIndexDto>();
        }

        return boons
            .Where(boon => !string.IsNullOrWhiteSpace(boon.Name))
            .Select(boon => new FightThreatBoonIndexDto(
                Id: boon.Id,
                Name: boon.Name.Trim(),
                Icon: NullIfWhiteSpace(boon.Icon),
                StackBased: boon.StackBased,
                TracksOverapplication: boon.TracksOverapplication,
                Coverage: boon.Coverage,
                AverageStacks: boon.AverageStacks,
                Overapplication: boon.Overapplication))
            .ToArray();
    }

    private static FightOutcomeDto BuildUnavailableOutcome(string detail)
    {
        return new FightOutcomeDto(
            OutcomeCode: "unknown",
            WinnerSideId: null,
            DisplayLabel: "Unavailable",
            DecidedBy: "none",
            Source: "elite-insights-json",
            Detail: detail);
    }

    private static FightExecutionIndexDto? BuildExecutionFromAnalystPayload(WvWAnalystFightPayloadDto payload)
    {
        if (payload.Execution is null)
        {
            return null;
        }

        return new FightExecutionIndexDto(
            ScoreAvailable: payload.Execution.ScoreAvailable,
            OverallScore: payload.Execution.OverallScore,
            Grade: NullIfWhiteSpace(payload.Execution.Grade),
            ConfidenceLabel: NullIfWhiteSpace(payload.Execution.Confidence?.Label),
            ConfidenceNotes: payload.Execution.Confidence?.Notes?
                .Select(NullIfWhiteSpace)
                .Where(note => note is not null)
                .Select(note => note!)
                .ToArray()
                ?? Array.Empty<string>(),
            Summary: NullIfWhiteSpace(payload.Execution.Summary),
            Detail: NullIfWhiteSpace(payload.Execution.Detail),
            StrongestPillarLabel: NullIfWhiteSpace(payload.Execution.StrongestPillarLabel),
            StrongestPillarSummary: NullIfWhiteSpace(payload.Execution.StrongestPillarSummary),
            WeakestPillarLabel: NullIfWhiteSpace(payload.Execution.WeakestPillarLabel),
            WeakestPillarSummary: NullIfWhiteSpace(payload.Execution.WeakestPillarSummary),
            Context: BuildExecutionContextFromAnalystPayload(payload.Execution.Context),
            Outcome: BuildExecutionOutcomeFromAnalystPayload(payload.Execution.Outcome),
            Pillars: payload.Execution.Pillars?
                .Select(pillar => new FightExecutionPillarIndexDto(
                    PillarId: NullIfWhiteSpace(pillar.PillarId) ?? string.Empty,
                    Label: NullIfWhiteSpace(pillar.Label) ?? string.Empty,
                    Score: pillar.Score,
                    Grade: NullIfWhiteSpace(pillar.Grade),
                    AdjustedScore: pillar.AdjustedScore,
                    AdjustedGrade: NullIfWhiteSpace(pillar.AdjustedGrade),
                    AdjustmentApplied: pillar.AdjustmentApplied,
                    AdjustmentDetail: NullIfWhiteSpace(pillar.AdjustmentDetail),
                    Summary: NullIfWhiteSpace(pillar.Summary),
                    Detail: NullIfWhiteSpace(pillar.Detail),
                    AvailableMetricCount: pillar.AvailableMetricCount,
                    MetricCount: pillar.MetricCount,
                    Metrics: pillar.Metrics?
                        .Select(metric => new FightExecutionMetricIndexDto(
                            Label: NullIfWhiteSpace(metric.Label) ?? string.Empty,
                            Value: NullIfWhiteSpace(metric.Value),
                            Note: NullIfWhiteSpace(metric.Note),
                            Available: metric.Available,
                            Score: metric.Score))
                        .ToArray()
                        ?? Array.Empty<FightExecutionMetricIndexDto>()))
                .ToArray()
                ?? Array.Empty<FightExecutionPillarIndexDto>());
    }

    private static FightExecutionContextIndexDto? BuildExecutionContextFromAnalystPayload(WvWAnalystExecutionContextDto? context)
    {
        if (context is null)
        {
            return null;
        }

        return new FightExecutionContextIndexDto(
            SquadPlayerCount: context.SquadPlayerCount,
            EnemyPlayerCount: context.EnemyPlayerCount,
            FriendlyNonSquadCount: context.FriendlyNonSquadCount,
            PhaseDurationLabel: NullIfWhiteSpace(context.PhaseDurationLabel),
            EnemyFormationStyleLabel: NullIfWhiteSpace(context.EnemyFormationStyleLabel),
            EnemyFormationStyleDetail: NullIfWhiteSpace(context.EnemyFormationStyleDetail),
            DataConfidenceLabel: NullIfWhiteSpace(context.DataConfidenceLabel),
            DataConfidenceDetail: NullIfWhiteSpace(context.DataConfidenceDetail));
    }

    private static FightExecutionOutcomeIndexDto? BuildExecutionOutcomeFromAnalystPayload(WvWAnalystExecutionOutcomeDto? outcome)
    {
        if (outcome is null)
        {
            return null;
        }

        return new FightExecutionOutcomeIndexDto(
            SquadDowns: outcome.SquadDowns,
            EnemyDowns: outcome.EnemyDowns,
            SquadKills: outcome.SquadKills,
            EnemyKills: outcome.EnemyKills,
            SquadDeaths: outcome.SquadDeaths,
            EnemyDeaths: outcome.EnemyDeaths,
            EnemyDownConversionRate: outcome.EnemyDownConversionRate,
            SquadRecoveryRate: outcome.SquadRecoveryRate,
            WipeLabel: NullIfWhiteSpace(outcome.WipeLabel));
    }

    private static string BuildOutcomeDetailFromAnalystPayload(
        WvWAnalystFightPayloadDto payload,
        string outcomeCode,
        string decidedBy)
    {
        var outcome = payload.Execution?.Outcome;
        var scoreSummary = outcome is null
            ? null
            : $"Scoreboard {outcome.SquadKills}-{outcome.EnemyKills} kills, {outcome.SquadDowns}-{outcome.EnemyDowns} downs, and {outcome.SquadDeaths}-{outcome.EnemyDeaths} deaths.";
        var wipeSummary = NullIfWhiteSpace(outcome?.WipeLabel);

        var lead = outcomeCode switch
        {
            "draw" => "Analyst JSON marked the fight as a draw.",
            "unknown" => "Analyst JSON marked the fight outcome as unknown.",
            _ when decidedBy != "none" => $"Analyst JSON awarded the fight to {BuildOutcomeDisplayLabel(outcomeCode, NormalizeWinnerSideId(payload.Outcome.WinnerSideId, outcomeCode), payload)} by {decidedBy}.",
            _ => "Outcome exported by analyst JSON."
        };

        return string.Join(
            " ",
            new[] { lead, scoreSummary, wipeSummary }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string NormalizeOutcomeCode(string? outcomeCode)
    {
        var normalized = (outcomeCode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "squad" => "squad",
            "enemy" => "enemy",
            "draw" => "draw",
            _ => "unknown"
        };
    }

    private static string? NormalizeWinnerSideId(string? winnerSideId, string outcomeCode)
    {
        var normalized = (winnerSideId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "squad" or "enemy")
        {
            return normalized;
        }

        return outcomeCode switch
        {
            "squad" => "squad",
            "enemy" => "enemy",
            _ => null
        };
    }

    private static string NormalizeDecider(string? decidedBy)
    {
        var normalized = (decidedBy ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "kills" => "kills",
            "downs" => "downs",
            "deaths" => "deaths",
            "damage" => "damage",
            _ => "none"
        };
    }

    private static string BuildOutcomeDisplayLabel(string outcomeCode, string? winnerSideId, WvWAnalystFightPayloadDto payload)
    {
        var exportedLabel = NullIfWhiteSpace(payload.Outcome.DisplayLabel);
        if (exportedLabel is not null)
        {
            return exportedLabel;
        }

        return winnerSideId switch
        {
            "squad" => NullIfWhiteSpace(payload.Sides?.Squad?.DisplayLabel) ?? "Our Squad",
            "enemy" => NullIfWhiteSpace(payload.Sides?.Enemy?.DisplayLabel) ?? "Enemy Team",
            _ when outcomeCode == "draw" => "Draw",
            _ => "Unavailable"
        };
    }

    private static string BuildCommanderDisplayName(JsonElement player)
    {
        var name = GetString(player, "name");
        var account = GetString(player, "account");

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(account))
        {
            return $"{name} ({account})";
        }

        return name ?? account ?? "Unknown commander";
    }

    private static string BuildDurationLabel(long? durationMilliseconds)
    {
        if (durationMilliseconds is null || durationMilliseconds <= 0)
        {
            return "Unknown";
        }

        var duration = TimeSpan.FromMilliseconds(durationMilliseconds.Value);
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        }

        return duration.Minutes > 0
            ? $"{duration.Minutes}m {duration.Seconds}s"
            : $"{duration.Seconds}s";
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement array)
    {
        array = default;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        array = property;
        return true;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return property.TryGetInt32(out value);
    }

    private static long? GetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return value;
        }

        return null;
    }
}
