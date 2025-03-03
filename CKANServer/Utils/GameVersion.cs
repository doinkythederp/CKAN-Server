using CKAN.Versioning;

namespace CKANServer.Utils;

public static class GameVersionExtension
{
    public static Game.Types.Version ToProto(this GameVersion version)
    {
        var buf = new Game.Types.Version();
        if (version.IsMajorDefined) buf.Major = version.Major;
        if (version.IsMinorDefined) buf.Minor = version.Minor;
        if (version.IsPatchDefined) buf.Patch = version.Patch;
        if (version.IsBuildDefined) buf.Build = version.Build;
        return buf;
    }

    public static GameVersion ToCkan(this Game.Types.Version buf)
    {
        // Although invalid fields are stored internally as `-1`, passing in that value gives an exception.
        // Thus, we need to use the several different constructors of various lengths to avoid this.
        if (buf is { HasMajor: true, HasMinor: true, HasPatch: true, HasBuild: true })
        {
            return new GameVersion(buf.Major, buf.Minor, buf.Patch, buf.Build);
        }
        else if (buf is { HasMajor: true, HasMinor: true, HasPatch: true })
        {
            return new GameVersion(buf.Major, buf.Minor, buf.Patch);
        }
        else if (buf is { HasMajor: true, HasMinor: true })
        {
            return new GameVersion(buf.Major, buf.Minor);
        }
        else if (buf is { HasMajor: true })
        {
            return new GameVersion(buf.Major);
        }
        else
        {
            return new GameVersion();
        }
    }
}