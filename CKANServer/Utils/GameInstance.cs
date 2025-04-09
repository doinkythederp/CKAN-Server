using CKAN;

namespace CKANServer.Utils;

public static class GameInstanceExtensions
{
    public static Instance ToProto(this GameInstance gameInstance, GameInstanceManager manager)
    {
        var autoStartInstance = manager.AutoStartInstance;

        var buf = new Instance
        {
            // Not sure if there's an actual "ID", but this seems constant enough
            GameId = gameInstance.game.ShortName,
            Directory = gameInstance.GameDir(),
            Name = gameInstance.Name,
            GameVersion = gameInstance.Version()?.ToProto(),
            IsDefault = autoStartInstance == gameInstance.Name,
            CompatOptions = new Instance.Types.CompatOptions
            {
                StabilityTolerance = gameInstance.StabilityToleranceConfig.OverallStabilityTolerance.ToProto(),
            },
        };

        foreach (var id in gameInstance.StabilityToleranceConfig.OverriddenModIdentifiers)
            buf.CompatOptions.StabilityToleranceOverrides[id] = (gameInstance.StabilityToleranceConfig.ModStabilityTolerance(id) ??
                                                                ReleaseStatus.stable).ToProto();

        buf.CompatOptions.VersionCompatibility = new Instance.Types.VersionCompatibility();
        if (gameInstance.GameVersionWhenCompatibleVersionsWereStored is { } version)
            buf.CompatOptions.VersionCompatibility.GameVersionWhenLastUpdated =
                version.ToProto();
        buf.CompatOptions.VersionCompatibility.CompatibleVersions.AddRange(
            gameInstance.GetCompatibleVersions()
                .Select(gameVersion => gameVersion.ToProto()));

        return buf;
    }
}