namespace WvWAnalyst.Contracts;

public sealed record CompHelperConfigDto(
    string SchemaVersion,
    string UpdatedAtUtc,
    IReadOnlyList<CompHelperLaneTargetDto> LaneTargets,
    IReadOnlyList<CompHelperPackageTargetDto> PackageTargets);

public sealed record CompHelperLaneTargetDto(
    string Key,
    string Label,
    double Floor,
    double Target,
    double Weight);

public sealed record CompHelperPackageTargetDto(
    string Key,
    string Label,
    double Floor,
    double Target,
    double Weight,
    bool Mandatory,
    bool AllowOvercap);
