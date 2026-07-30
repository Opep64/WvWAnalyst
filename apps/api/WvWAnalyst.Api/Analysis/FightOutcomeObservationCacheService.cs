using System.IO.Compression;
using System.Text.Json;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class FightOutcomeObservationCacheService
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentFeatureVersion = "outcome-observations-v1";
    public const string CacheRelativePath = "derived/outcome-observations.json.gz";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public FightOutcomeObservationCacheDto? TryBuild(string analystJsonPath)
    {
        if (string.IsNullOrWhiteSpace(analystJsonPath) || !File.Exists(analystJsonPath))
        {
            return null;
        }

        try
        {
            using Stream fileStream = File.OpenRead(analystJsonPath);
            using Stream payloadStream = analystJsonPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(fileStream, CompressionMode.Decompress)
                : fileStream;
            WvWAnalystFightPayloadDto? payload =
                JsonSerializer.Deserialize<WvWAnalystFightPayloadDto>(payloadStream, ReadOptions);
            return payload?.OutcomeAnalysis is null || !IsTimelineValid(payload.OutcomeAnalysis.Timeline)
                ? null
                : Build(payload);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<string?> WriteAsync(
        string fightDirectoryPath,
        FightOutcomeObservationCacheDto? cache,
        string fightId,
        string sourceFileName,
        string sourceFileSha256,
        CancellationToken cancellationToken)
    {
        string cachePath = Path.Combine(fightDirectoryPath, CacheRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (cache is null)
        {
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }
            return null;
        }

        cache.FightId = fightId;
        cache.SourceFileName = sourceFileName;
        cache.SourceFileSha256 = sourceFileSha256;
        cache.GeneratedAtUtc = DateTime.UtcNow.ToString("O");

        string directoryPath = Path.GetDirectoryName(cachePath)!;
        Directory.CreateDirectory(directoryPath);
        string temporaryPath = Path.Combine(directoryPath, $".{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var fileStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var stream = new GZipStream(fileStream, CompressionLevel.Fastest))
            {
                await JsonSerializer.SerializeAsync(stream, cache, WriteOptions, cancellationToken);
            }
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return CacheRelativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    public FightOutcomeObservationCacheDto? TryRead(string cachePath)
    {
        if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            using Stream fileStream = File.OpenRead(cachePath);
            using Stream payloadStream = cachePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(fileStream, CompressionMode.Decompress)
                : fileStream;
            return JsonSerializer.Deserialize<FightOutcomeObservationCacheDto>(payloadStream, ReadOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FightOutcomeObservationCacheDto Build(WvWAnalystFightPayloadDto payload)
    {
        WvWAnalystOutcomeAnalysisDto analysis = payload.OutcomeAnalysis!;
        long competitiveEnd = analysis.CompetitiveEndTimeMs
            ?? (analysis.Timeline.TimesMs.Length > 0 ? analysis.Timeline.TimesMs[^1] : 0);
        var observations = new List<FightOutcomeObservationDto>();
        observations.AddRange(BuildDownOutcomeObservations(analysis, competitiveEnd));
        observations.AddRange(BuildOrdinaryPressureObservations(analysis, "squad", "enemy", competitiveEnd));
        observations.AddRange(BuildOrdinaryPressureObservations(analysis, "enemy", "squad", competitiveEnd));
        observations.AddRange(BuildResolutionObservations(analysis, competitiveEnd));
        observations.Sort((left, right) =>
        {
            int timeComparison = left.ReferenceTimeMs.CompareTo(right.ReferenceTimeMs);
            return timeComparison != 0
                ? timeComparison
                : string.Compare(left.ObservationId, right.ObservationId, StringComparison.Ordinal);
        });

        return new FightOutcomeObservationCacheDto
        {
            SchemaVersion = CurrentSchemaVersion,
            FeatureVersion = CurrentFeatureVersion,
            AnalystSchemaVersion = payload.Meta.SchemaVersion,
            OutcomeMethodVersion = analysis.MethodVersion,
            ParserVersion = payload.Meta.ParserVersion,
            GameBuild = payload.Fight.GameBuild,
            ArcVersion = payload.Fight.ArcVersion,
            PressureWindowMs = analysis.PressureWindowMs,
            ControlSeparationMs = analysis.PressureWindowMs,
            CompetitiveEndTimeMs = analysis.CompetitiveEndTimeMs,
            Availability = analysis.Availability,
            Summary = BuildSummary(observations),
            Observations = observations,
        };
    }

    private static IEnumerable<FightOutcomeObservationDto> BuildDownOutcomeObservations(
        WvWAnalystOutcomeAnalysisDto analysis,
        long competitiveEnd)
    {
        return analysis.Events
            .Where(evt =>
                string.Equals(evt.EventType, "down", StringComparison.OrdinalIgnoreCase) &&
                evt.TimeMs <= competitiveEnd)
            .GroupBy(evt => evt.EngagementId, StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group =>
            {
                WvWAnalystOutcomeEventDto[] events = group.OrderBy(evt => evt.TimeMs).ToArray();
                WvWAnalystOutcomeEventDto first = events[0];
                long windowStart = Math.Max(0, first.TimeMs - analysis.PressureWindowMs);
                return BuildObservation(
                    analysis,
                    observationId: $"down:{group.Key}",
                    outcomeFamily: "down-pressure",
                    featureView: "pressure",
                    perspectiveSideId: first.ActingSideId,
                    opposingSideId: first.AffectedSideId,
                    outcomeCode: "down",
                    isOutcome: true,
                    succeeded: true,
                    engagementId: group.Key,
                    referenceTimeMs: first.TimeMs,
                    windowStartMs: windowStart,
                    windowEndMs: first.TimeMs,
                    outcomeCount: events.Length,
                    sourceEvents: events,
                    pressureEvent: null,
                    includeNamedEffects: true);
            });
    }

    private static IEnumerable<FightOutcomeObservationDto> BuildOrdinaryPressureObservations(
        WvWAnalystOutcomeAnalysisDto analysis,
        string perspectiveSideId,
        string opposingSideId,
        long competitiveEnd)
    {
        WvWAnalystOutcomeTimelineDto timeline = analysis.Timeline;
        WvWAnalystOutcomeSideTimelineDto side = GetSideTimeline(timeline, perspectiveSideId);
        long[] outcomeTimes = analysis.Events
            .Where(evt =>
                string.Equals(evt.EventType, "down", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(evt.ActingSideId, perspectiveSideId, StringComparison.OrdinalIgnoreCase) &&
                evt.TimeMs <= competitiveEnd)
            .Select(evt => evt.TimeMs)
            .ToArray();
        long? lastControlTime = null;

        for (int index = 0; index < timeline.TimesMs.Length; index++)
        {
            long time = timeline.TimesMs[index];
            if (time > competitiveEnd)
            {
                yield break;
            }
            if (GetValue(side.Damage, index) <= 0)
            {
                continue;
            }
            if (outcomeTimes.Any(outcomeTime => Math.Abs(time - outcomeTime) <= 5000))
            {
                continue;
            }
            if (lastControlTime.HasValue && time - lastControlTime.Value < analysis.PressureWindowMs)
            {
                continue;
            }

            lastControlTime = time;
            yield return BuildObservation(
                analysis,
                observationId: $"control:{perspectiveSideId}:{time}",
                outcomeFamily: "down-pressure",
                featureView: "pressure",
                perspectiveSideId: perspectiveSideId,
                opposingSideId: opposingSideId,
                outcomeCode: "ordinary-pressure",
                isOutcome: false,
                succeeded: false,
                engagementId: string.Empty,
                referenceTimeMs: time,
                windowStartMs: Math.Max(0, time - analysis.PressureWindowMs),
                windowEndMs: time,
                outcomeCount: 0,
                sourceEvents: [],
                pressureEvent: null,
                includeNamedEffects: true);
        }
    }

    private static IEnumerable<FightOutcomeObservationDto> BuildResolutionObservations(
        WvWAnalystOutcomeAnalysisDto analysis,
        long competitiveEnd)
    {
        foreach (WvWAnalystOutcomeEventDto evt in analysis.Events.Where(evt =>
            (string.Equals(evt.EventType, "kill", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(evt.EventType, "recovery", StringComparison.OrdinalIgnoreCase)) &&
            evt.DownTimeMs <= competitiveEnd))
        {
            bool killed = string.Equals(evt.EventType, "kill", StringComparison.OrdinalIgnoreCase);
            yield return BuildObservation(
                analysis,
                observationId: $"conversion:{evt.EventId}",
                outcomeFamily: "down-conversion",
                featureView: "pressure",
                perspectiveSideId: evt.ActingSideId,
                opposingSideId: evt.AffectedSideId,
                outcomeCode: killed ? "kill" : "recovery",
                isOutcome: true,
                succeeded: killed,
                engagementId: evt.EngagementId,
                referenceTimeMs: evt.TimeMs,
                windowStartMs: evt.DownTimeMs,
                windowEndMs: evt.TimeMs,
                outcomeCount: 1,
                sourceEvents: [evt],
                pressureEvent: evt,
                includeNamedEffects: true);

            yield return BuildObservation(
                analysis,
                observationId: $"recovery:{evt.EventId}",
                outcomeFamily: "down-recovery",
                featureView: "recovery",
                perspectiveSideId: evt.AffectedSideId,
                opposingSideId: evt.ActingSideId,
                outcomeCode: killed ? "death" : "recovery",
                isOutcome: true,
                succeeded: !killed,
                engagementId: evt.EngagementId,
                referenceTimeMs: evt.TimeMs,
                windowStartMs: evt.DownTimeMs,
                windowEndMs: evt.TimeMs,
                outcomeCount: 1,
                sourceEvents: [evt],
                pressureEvent: null,
                includeNamedEffects: false);
        }
    }

    private static FightOutcomeObservationDto BuildObservation(
        WvWAnalystOutcomeAnalysisDto analysis,
        string observationId,
        string outcomeFamily,
        string featureView,
        string perspectiveSideId,
        string opposingSideId,
        string outcomeCode,
        bool isOutcome,
        bool succeeded,
        string engagementId,
        long referenceTimeMs,
        long windowStartMs,
        long windowEndMs,
        int outcomeCount,
        IReadOnlyList<WvWAnalystOutcomeEventDto> sourceEvents,
        WvWAnalystOutcomeEventDto? pressureEvent,
        bool includeNamedEffects)
    {
        IReadOnlyList<FightOutcomeConditionFeatureDto> conditions = includeNamedEffects
            ? BuildConditionFeatures(analysis, perspectiveSideId, windowStartMs, windowEndMs, sourceEvents)
            : [];
        IReadOnlyList<FightOutcomeCrowdControlFeatureDto> crowdControl = includeNamedEffects
            ? BuildCrowdControlFeatures(analysis, perspectiveSideId, windowStartMs, windowEndMs)
            : [];
        double vulnerabilityBonusDamage = includeNamedEffects
            ? analysis.ConditionEvents
                .Where(evt =>
                    string.Equals(evt.ActingSideId, perspectiveSideId, StringComparison.OrdinalIgnoreCase) &&
                    evt.TimeMs >= windowStartMs &&
                    evt.TimeMs <= windowEndMs)
                .Sum(evt => evt.VulnerabilityBonusDamage)
            : 0.0;

        return new FightOutcomeObservationDto
        {
            ObservationId = observationId,
            OutcomeFamily = outcomeFamily,
            FeatureView = featureView,
            PerspectiveSideId = perspectiveSideId,
            OpposingSideId = opposingSideId,
            OutcomeCode = outcomeCode,
            IsOutcome = isOutcome,
            Succeeded = succeeded,
            EngagementId = engagementId,
            ReferenceTimeMs = referenceTimeMs,
            WindowStartMs = windowStartMs,
            WindowEndMs = windowEndMs,
            OutcomeCount = outcomeCount,
            SourceEventIds = sourceEvents.Select(evt => evt.EventId).ToArray(),
            OutcomeActorIds = sourceEvents.Select(evt => evt.ActorId).Distinct().ToArray(),
            Features = BuildFeatureVector(
                analysis,
                perspectiveSideId,
                opposingSideId,
                referenceTimeMs,
                pressureEvent,
                sourceEvents,
                sourceEvents.FirstOrDefault(),
                crowdControl,
                vulnerabilityBonusDamage),
            Conditions = conditions,
            CrowdControl = crowdControl,
        };
    }

    private static FightOutcomeFeatureVectorDto BuildFeatureVector(
        WvWAnalystOutcomeAnalysisDto analysis,
        string perspectiveSideId,
        string opposingSideId,
        long referenceTimeMs,
        WvWAnalystOutcomeEventDto? pressureEvent,
        IReadOnlyList<WvWAnalystOutcomeEventDto> sourceEvents,
        WvWAnalystOutcomeEventDto? supportEvent,
        IReadOnlyList<FightOutcomeCrowdControlFeatureDto> crowdControl,
        double vulnerabilityBonusDamage)
    {
        int index = GetTimelineIndex(analysis.Timeline.TimesMs, referenceTimeMs);
        WvWAnalystOutcomeSideTimelineDto perspective = GetSideTimeline(analysis.Timeline, perspectiveSideId);
        WvWAnalystOutcomeSideTimelineDto opposing = GetSideTimeline(analysis.Timeline, opposingSideId);
        int activePerspective = GetValue(perspective.State.Active, index);
        int activeOpposing = GetValue(opposing.State.Active, index);
        long pressureDamage = GetValue(perspective.Damage, index);
        int strips = GetValue(perspective.Strips, index);
        int corrupts = GetValue(perspective.Corrupts, index);
        long healing = GetValue(perspective.Healing, index);
        long barrier = GetValue(perspective.Barrier, index);
        int cleanses = GetValue(perspective.Cleanses, index);
        WvWAnalystOutcomeEventDto[] outcomeWindowEvents = pressureEvent is not null
            ? [pressureEvent]
            : sourceEvents
                .Where(evt => string.Equals(evt.EventType, "down", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        bool positioningAvailable = GetValue(analysis.Timeline.SquadPositioning.Available, index);
        double inPositionRate = GetValue(analysis.Timeline.SquadPositioning.InPositionRate, index);

        return new FightOutcomeFeatureVectorDto
        {
            ActivePerspectivePlayers = activePerspective,
            ObservedPerspectivePlayers = GetValue(perspective.State.Observed, index),
            ActiveOpposingPlayers = activeOpposing,
            ObservedOpposingPlayers = GetValue(opposing.State.Observed, index),
            PressureDamage = pressureDamage,
            PressureDamagePerActivePlayer = Normalize(pressureDamage, activePerspective),
            OutcomeWindowDamage = outcomeWindowEvents.Sum(evt => evt.TotalDamage),
            OutcomeWindowStrikeDamage = outcomeWindowEvents.Sum(evt => evt.StrikeDamage),
            OutcomeWindowConditionDamage = outcomeWindowEvents.Sum(evt => evt.ConditionDamage),
            OutcomeWindowBarrierDamage = outcomeWindowEvents.Sum(evt => evt.BarrierDamage),
            Strips = strips,
            Corrupts = corrupts,
            BoonRemovalPerActivePlayer = Normalize(strips + corrupts, activePerspective),
            Healing = healing,
            HealingPerActivePlayer = Normalize(healing, activePerspective),
            Barrier = barrier,
            BarrierPerActivePlayer = Normalize(barrier, activePerspective),
            Cleanses = cleanses,
            CleansesPerActivePlayer = Normalize(cleanses, activePerspective),
            TopTargetShare = GetValue(perspective.TopTargetShare, index),
            TopThreeTargetShare = GetValue(perspective.TopThreeTargetShare, index),
            TopTargetContributors = GetValue(perspective.TopTargetContributors, index),
            Focused = GetValue(perspective.Focused, index),
            StripSynced = GetValue(perspective.StripSynced, index),
            TargetSaturationCount = GetValue(perspective.TargetSaturationCount, index),
            CrowdControlEvents = crowdControl.Sum(effect => effect.EventCount),
            EffectiveCrowdControlEvents = crowdControl.Sum(effect => effect.EffectiveCount),
            CrowdControlDurationSeconds = Math.Round(crowdControl.Sum(effect => effect.DurationSeconds), 3),
            VulnerabilityBonusDamage = Math.Round(vulnerabilityBonusDamage, 3),
            DownedHealing = supportEvent?.DownedHealing ?? 0,
            DownedHealingEvents = supportEvent?.DownedHealingEvents ?? 0,
            ResurrectionCasts = supportEvent?.ResurrectionCasts ?? 0,
            ResurrectionCastDurationSeconds = supportEvent?.ResurrectionCastDurationSeconds ?? 0.0,
            SupportContributors = supportEvent?.SupportContributors ?? 0,
            ClassRecoveryActions = supportEvent?.ClassRecoveryActions ?? 0,
            SquadPositioningAvailable = positioningAvailable,
            SquadInPositionRate = inPositionRate,
            SquadPositioningRiskRate = positioningAvailable ? Math.Round(Math.Max(0, 100.0 - inPositionRate), 3) : 0.0,
        };
    }

    private static IReadOnlyList<FightOutcomeConditionFeatureDto> BuildConditionFeatures(
        WvWAnalystOutcomeAnalysisDto analysis,
        string perspectiveSideId,
        long windowStartMs,
        long windowEndMs,
        IReadOnlyList<WvWAnalystOutcomeEventDto> sourceEvents)
    {
        var values = new Dictionary<(long BuffId, string Name), FightOutcomeConditionFeatureDto>();
        foreach (WvWAnalystConditionEventDto bucket in analysis.ConditionEvents.Where(evt =>
            string.Equals(evt.ActingSideId, perspectiveSideId, StringComparison.OrdinalIgnoreCase) &&
            evt.TimeMs >= windowStartMs &&
            evt.TimeMs <= windowEndMs))
        {
            foreach (WvWAnalystConditionEffectDto effect in bucket.Effects)
            {
                var key = BuildConditionKey(effect.BuffId, effect.Name);
                if (!values.TryGetValue(key, out FightOutcomeConditionFeatureDto? value))
                {
                    value = new FightOutcomeConditionFeatureDto
                    {
                        BuffId = effect.BuffId,
                        Name = effect.Name,
                    };
                    values[key] = value;
                }
                value.ApplyCount += effect.ApplyCount;
                value.ExtensionCount += effect.ExtensionCount;
            }
        }

        foreach (WvWAnalystOutcomeConditionDamageDto effect in sourceEvents.SelectMany(evt => evt.ConditionDamageByEffect))
        {
            long buffId = effect.BuffId ?? 0;
            string name = ResolveConditionName(analysis, buffId, effect.Name);
            var key = BuildConditionKey(buffId, name);
            if (!values.TryGetValue(key, out FightOutcomeConditionFeatureDto? value))
            {
                value = new FightOutcomeConditionFeatureDto
                {
                    BuffId = buffId,
                    Name = name,
                };
                values[key] = value;
            }
            value.DirectDamage += effect.Damage;
        }

        return values.Values
            .Where(value => value.ApplyCount > 0 || value.ExtensionCount > 0 || value.DirectDamage > 0)
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.BuffId)
            .ToArray();
    }

    private static IReadOnlyList<FightOutcomeCrowdControlFeatureDto> BuildCrowdControlFeatures(
        WvWAnalystOutcomeAnalysisDto analysis,
        string perspectiveSideId,
        long windowStartMs,
        long windowEndMs)
    {
        return analysis.CrowdControlEvents
            .Where(evt =>
                string.Equals(evt.ActingSideId, perspectiveSideId, StringComparison.OrdinalIgnoreCase) &&
                evt.TimeMs >= windowStartMs &&
                evt.TimeMs <= windowEndMs)
            .SelectMany(evt => evt.Effects)
            .GroupBy(effect => (effect.SkillId, Name: NormalizeEffectName(effect.Name)))
            .Select(group => new FightOutcomeCrowdControlFeatureDto
            {
                SkillId = group.Key.SkillId,
                Name = group.Key.Name,
                EventCount = group.Sum(effect => effect.EventCount),
                EffectiveCount = group.Sum(effect => effect.EffectiveCount),
                DurationSeconds = Math.Round(group.Sum(effect => effect.DurationSeconds), 3),
            })
            .Where(effect => effect.EventCount > 0 || effect.EffectiveCount > 0 || effect.DurationSeconds > 0)
            .OrderBy(effect => effect.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(effect => effect.SkillId)
            .ToArray();
    }

    private static FightOutcomeObservationSummaryDto BuildSummary(
        IReadOnlyList<FightOutcomeObservationDto> observations)
    {
        return new FightOutcomeObservationSummaryDto
        {
            ObservationCount = observations.Count,
            SquadPerspectiveCount = observations.Count(item =>
                string.Equals(item.PerspectiveSideId, "squad", StringComparison.OrdinalIgnoreCase)),
            EnemyPerspectiveCount = observations.Count(item =>
                string.Equals(item.PerspectiveSideId, "enemy", StringComparison.OrdinalIgnoreCase)),
            DownOutcomeCount = observations.Count(item =>
                string.Equals(item.OutcomeFamily, "down-pressure", StringComparison.Ordinal) && item.IsOutcome),
            OrdinaryPressureCount = observations.Count(item =>
                string.Equals(item.OutcomeCode, "ordinary-pressure", StringComparison.Ordinal)),
            ConversionCount = observations.Count(item =>
                string.Equals(item.OutcomeFamily, "down-conversion", StringComparison.Ordinal)),
            RecoveryCount = observations.Count(item =>
                string.Equals(item.OutcomeFamily, "down-recovery", StringComparison.Ordinal)),
            NamedConditionCount = observations
                .SelectMany(item => item.Conditions)
                .Select(effect => (effect.BuffId, effect.Name))
                .Distinct()
                .Count(),
            NamedCrowdControlCount = observations
                .SelectMany(item => item.CrowdControl)
                .Select(effect => (effect.SkillId, effect.Name))
                .Distinct()
                .Count(),
        };
    }

    private static bool IsTimelineValid(WvWAnalystOutcomeTimelineDto timeline)
    {
        int count = timeline.SampleCount;
        return count > 0 &&
            timeline.TimesMs.Length == count &&
            timeline.Squad.Damage.Length == count &&
            timeline.Enemy.Damage.Length == count &&
            timeline.Squad.State.Active.Length == count &&
            timeline.Enemy.State.Active.Length == count &&
            timeline.SquadPositioning.Available.Length == count;
    }

    private static WvWAnalystOutcomeSideTimelineDto GetSideTimeline(
        WvWAnalystOutcomeTimelineDto timeline,
        string sideId)
    {
        return string.Equals(sideId, "enemy", StringComparison.OrdinalIgnoreCase)
            ? timeline.Enemy
            : timeline.Squad;
    }

    private static int GetTimelineIndex(long[] times, long time)
    {
        int index = Array.BinarySearch(times, time);
        if (index >= 0)
        {
            return index;
        }
        return Math.Clamp(~index - 1, 0, times.Length - 1);
    }

    private static double Normalize(double value, int denominator)
    {
        return denominator > 0 ? Math.Round(value / denominator, 3) : 0.0;
    }

    private static long GetValue(long[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : 0;

    private static int GetValue(int[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : 0;

    private static double GetValue(double[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : 0.0;

    private static bool GetValue(bool[] values, int index) =>
        index >= 0 && index < values.Length && values[index];

    private static (long BuffId, string Name) BuildConditionKey(long buffId, string name) =>
        buffId > 0 ? (buffId, string.Empty) : (0, name);

    private static string ResolveConditionName(
        WvWAnalystOutcomeAnalysisDto analysis,
        long buffId,
        string fallback)
    {
        if (buffId <= 0)
        {
            return fallback;
        }

        return analysis.ConditionEvents
            .SelectMany(evt => evt.Effects)
            .FirstOrDefault(effect => effect.BuffId == buffId)
            ?.Name ?? fallback;
    }

    private static string NormalizeEffectName(string name)
    {
        int separatorIndex = name.IndexOf('-', StringComparison.Ordinal);
        return separatorIndex > 0 && name[..separatorIndex].All(char.IsDigit)
            ? name[(separatorIndex + 1)..]
            : name;
    }
}
