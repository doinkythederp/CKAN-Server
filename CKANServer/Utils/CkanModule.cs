using CKAN;
using Google.Protobuf.WellKnownTypes;

namespace CKANServer.Utils;

public static class AvailableModuleExtensions
{
    public static Module? ToProto(
        this AvailableModule module,
        RepositoryDataManager repositoryDataManager,
        IRegistryQuerier registry)
    {
        var modules = module.AllAvailable().ToList();
        if (modules.Count == 0) return null;

        var buf = new Module
        {
            Identifier = modules.First().identifier,
        };
        buf.Releases.AddRange(modules.Select(m => m.ToProto(repositoryDataManager, registry)));

        return buf;
    }
}

public static class CkanModuleExtension
{
    public static ModuleReleaseRef ToRef(this CkanModule module)
    {
        return new ModuleReleaseRef
        {
            Id = module.identifier,
            Version = module.version.ToString(),
        };
    }
    
    public static Module.Types.Release ToProto(
        this CkanModule module,
        RepositoryDataManager repositoryDataManager,
        IRegistryQuerier registry)
    {
        var buf = new Module.Types.Release
        {
            Name = module.name,
            Version = module.version.ToString(),
            ReleaseStatus = ToProto(module.release_status ?? ReleaseStatus.stable),
            Abstract = module.@abstract,
        };

        if (module.description != null) buf.Description = module.description;

        buf.Kind = Kind(module.kind);
        buf.Authors.AddRange(module.author);

        buf.Licenses.AddRange(module.license.Select(l => l.ToString()));
        if (module.Tags != null) buf.Tags.AddRange(module.Tags);
        if (module.localizations != null) buf.Localizations.AddRange(module.localizations);
        if (module.release_date != null) buf.ReleaseDate = Timestamp.FromDateTime(module.release_date.Value);
        if (module.resources != null) buf.Resources = module.resources.ToProto();

        if (module.conflicts != null) buf.Conflicts.AddRange(module.conflicts.Select(r => r.ToProto()));
        if (module.depends != null) buf.Depends.AddRange(module.depends.Select(r => r.ToProto()));
        if (module.recommends != null) buf.Recommends.AddRange(module.recommends.Select(r => r.ToProto()));
        if (module.suggests != null) buf.Suggests.AddRange(module.suggests.Select(r => r.ToProto()));
        if (module.supports != null) buf.Supports.AddRange(module.supports.Select(r => r.ToProto()));
        if (module.replaced_by != null) buf.ReplacedBy = module.replaced_by.ToProtoDirect();
        if (module.provides != null) buf.Provides.AddRange(module.provides);

        if (module.download != null) buf.DownloadUris.AddRange(module.download.Select(u => u.ToString()));
        buf.DownloadSizeBytes = (ulong)module.download_size;
        buf.InstallSizeBytes = (ulong)module.install_size;

        var downloads = repositoryDataManager.GetDownloadCount(registry.Repositories.Values, module.identifier);
        if (downloads != null) buf.DownloadCount = (ulong)downloads;

        if (module.ksp_version != null) buf.KspVersion = module.ksp_version.ToProto();
        if (module.ksp_version_max != null) buf.KspVersionMax = module.ksp_version_max.ToProto();
        if (module.ksp_version_min != null) buf.KspVersionMin = module.ksp_version_min.ToProto();

        if (module.ksp_version_strict != null) buf.KspVersionStrict = module.ksp_version_strict.Value;

        return buf;
    }

    private static Module.Types.Kind Kind(string? value)
    {
        return value switch
        {
            "package" or null => Module.Types.Kind.ModulePackage,
            "metapackage" => Module.Types.Kind.ModuleMetapackage,
            "dlc" => Module.Types.Kind.ModuleDlc,
            _ => Module.Types.Kind.ModuleUnknown,
        };
    }

    public static Module.Types.ReleaseStatus ToProto(this ReleaseStatus value)
    {
        return value switch
        {
            ReleaseStatus.stable => Module.Types.ReleaseStatus.MrsStable,
            ReleaseStatus.testing => Module.Types.ReleaseStatus.MrsTesting,
            ReleaseStatus.development => Module.Types.ReleaseStatus.MrsDevelopment,
            _ => Module.Types.ReleaseStatus.MrsUnknown,
        };
    }

    public static ReleaseStatus? FromProto(this Module.Types.ReleaseStatus value)
    {
        return value switch
        {
            Module.Types.ReleaseStatus.MrsStable => ReleaseStatus.stable,
            Module.Types.ReleaseStatus.MrsTesting => ReleaseStatus.testing,
            Module.Types.ReleaseStatus.MrsDevelopment => ReleaseStatus.development,
            _ => null,
        };
    }
}

public static class RelationshipDescriptorExtension
{
    public static Module.Types.Relationship.Types.DirectRelationship ToProtoDirect(
        this ModuleRelationshipDescriptor descriptor)
    {
        var direct = new Module.Types.Relationship.Types.DirectRelationship
        {
            Name = descriptor.name,
        };
        if (descriptor.version != null) direct.Version = descriptor.version.ToString();
        if (descriptor.min_version != null) direct.MinVersion = descriptor.min_version.ToString();
        if (descriptor.max_version != null) direct.MaxVersion = descriptor.max_version.ToString();
        return direct;
    }
    
    public static Module.Types.Relationship ToProto(this RelationshipDescriptor relationship)
    {
        var buf = new Module.Types.Relationship
        {
            SuppressRecommendations = relationship.suppress_recommendations,
        };

        if (relationship.choice_help_text != null) buf.ChoiceHelpText = relationship.choice_help_text;

        switch (relationship)
        {
            case ModuleRelationshipDescriptor modRelationship:
            {
                buf.Direct = modRelationship.ToProtoDirect();
                break;
            }
            case AnyOfRelationshipDescriptor anyOfRelationship:
            {
                buf.AnyOf = new Module.Types.Relationship.Types.AnyOfRelationship();
                if (anyOfRelationship.any_of != null)
                    buf.AnyOf.AllowedModules.AddRange(anyOfRelationship.any_of.Select(r => r.ToProto()));

                break;
            }
        }

        return buf;
    }
}

public static class ResourceDescriptorExtension
{
    public static Module.Types.Resources ToProto(this ResourcesDescriptor resources)
    {
        var buf = new Module.Types.Resources();
        if (resources.homepage != null) buf.Homepage = resources.homepage.ToString();
        if (resources.spacedock != null) buf.Spacedock = resources.spacedock.ToString();
        if (resources.curse != null) buf.Curse = resources.curse.ToString();
        if (resources.repository != null) buf.Repository = resources.repository.ToString();
        if (resources.bugtracker != null) buf.Bugtracker = resources.bugtracker.ToString();
        if (resources.discussions != null) buf.Discussions = resources.discussions.ToString();
        if (resources.ci != null) buf.Ci = resources.ci.ToString();
        if (resources.license != null) buf.License = resources.license.ToString();
        if (resources.manual != null) buf.Manual = resources.manual.ToString();
        if (resources.metanetkan != null) buf.Metanetkan = resources.metanetkan.ToString();
        if (resources.remoteAvc != null) buf.RemoteAvc = resources.remoteAvc.ToString();
        if (resources.remoteSWInfo != null) buf.RemoteSwinfo = resources.remoteSWInfo.ToString();
        if (resources.store != null) buf.Store = resources.store.ToString();
        if (resources.steamstore != null) buf.SteamStore = resources.steamstore.ToString();
        return buf;
    }
}