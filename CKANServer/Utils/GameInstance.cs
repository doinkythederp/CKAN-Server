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
            StabilityTolerance = gameInstance.StabilityToleranceConfig.OverallStabilityTolerance.ToProto(),
        };

        foreach (var id in gameInstance.StabilityToleranceConfig.OverriddenModIdentifiers)
            buf.StabilityToleranceOverrides[id] = (gameInstance.StabilityToleranceConfig.ModStabilityTolerance(id) ??
                                                   ReleaseStatus.stable).ToProto();

        buf.VersionCompatibility = new Instance.Types.VersionCompatibility();
        if (gameInstance.GameVersionWhenCompatibleVersionsWereStored is { } version)
            buf.VersionCompatibility.GameVersionWhenLastUpdated =
                version.ToProto();
        buf.VersionCompatibility.CompatibleVersions.AddRange(
            gameInstance.GetCompatibleVersions()
                .Select(gameVersion => gameVersion.ToProto()));

        return buf;
    }
}