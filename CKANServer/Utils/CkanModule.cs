using CKAN;
using CKAN.Versioning;
using Google.Protobuf.WellKnownTypes;

namespace CKANServer.Utils;

public static class CkanModuleExtension
{
    public static Module ToProto(
        this CkanModule module,
        RepositoryDataManager repositoryDataManager,
        IRegistryQuerier querier
    )
    {
        var buf = new Module();

        buf.Abstract = module.@abstract;

        if (module.description != null) buf.Description = module.description;

        buf.Kind = Kind(module.kind);
        buf.Authors.AddRange(module.author);

        if (module.conflicts != null) buf.Conflicts.AddRange(module.conflicts.Select(r => r.ToProto()));
        if (module.depends != null) buf.Depends.AddRange(module.depends.Select(r => r.ToProto()));
        if (module.replaced_by != null) buf.ReplacedBy = module.replaced_by.ToProto();
        if (module.download != null) buf.DownloadUris.AddRange(module.download.Select(u => u.ToString()));

        buf.DownloadSizeBytes = (ulong)module.download_size;
        buf.InstallSizeBytes = (ulong)module.install_size;
        buf.Identifier = module.identifier;

        if (module.ksp_version != null) buf.KspVersion = module.ksp_version.ToProto();
        if (module.ksp_version_max != null) buf.KspVersionMax = module.ksp_version_max.ToProto();
        if (module.ksp_version_min != null) buf.KspVersionMin = module.ksp_version_min.ToProto();

        if (module.ksp_version_strict != null) buf.KspVersionStrict = module.ksp_version_strict.Value;
        buf.Licenses.AddRange(module.license.Select(l => l.ToString()));
        buf.Name = module.name;

        if (module.provides != null) buf.Provides.AddRange(module.provides);
        if (module.recommends != null) buf.Recommends.AddRange(module.recommends.Select(r => r.ToProto()));

        buf.ReleaseStatus = ReleaseStatus(module.release_status ?? CKAN.ReleaseStatus.stable);
        if (module.resources != null) buf.Resources = module.resources.ToProto();

        if (module.suggests != null) buf.Suggests.AddRange(module.suggests.Select(r => r.ToProto()));

        buf.Version = module.version.ToString();

        if (module.supports != null) buf.Supports.AddRange(module.supports.Select(r => r.ToProto()));
        if (module.localizations != null) buf.Localizations.AddRange(module.localizations);

        if (module.Tags != null) buf.Tags.AddRange(module.Tags);
        if (module.release_date != null) buf.ReleaseDate = Timestamp.FromDateTime(module.release_date.Value);

        var downloads = repositoryDataManager.GetDownloadCount(querier.Repositories.Values, module.identifier);
        if (downloads != null) buf.DownloadCount = (ulong)downloads;

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

    private static Module.Types.ReleaseStatus ReleaseStatus(ReleaseStatus? value)
    {
        return value switch
        {
            CKAN.ReleaseStatus.stable => Module.Types.ReleaseStatus.MrsStable,
            CKAN.ReleaseStatus.testing => Module.Types.ReleaseStatus.MrsTesting,
            CKAN.ReleaseStatus.development => Module.Types.ReleaseStatus.MrsDevelopment,
            _ => Module.Types.ReleaseStatus.MrsUnknown,
        };
    }
}

public static class RelationshipDescriptorExtension
{
    public static Module.Types.Relationship ToProto(this RelationshipDescriptor relationship)
    {
        var buf = new Module.Types.Relationship
        {
            SuppressRecommendations = relationship.suppress_recommendations,
        };

        if (relationship.choice_help_text != null)
        {
            buf.ChoiceHelpText = relationship.choice_help_text;
        }

        switch (relationship)
        {
            case ModuleRelationshipDescriptor modRelationship:
            {
                buf.Direct = new Module.Types.Relationship.Types.DirectRelationship
                {
                    Name = modRelationship.name,
                };
                if (modRelationship.version != null) buf.Direct.Version = modRelationship.version.ToString();
                if (modRelationship.min_version != null) buf.Direct.MinVersion = modRelationship.min_version.ToString();
                if (modRelationship.max_version != null) buf.Direct.MaxVersion = modRelationship.max_version.ToString();
                break;
            }
            case AnyOfRelationshipDescriptor anyOfRelationship:
            {
                buf.AnyOf = new Module.Types.Relationship.Types.AnyOfRelationship();
                if (anyOfRelationship.any_of != null)
                {
                    buf.AnyOf.AllowedModules.AddRange(anyOfRelationship.any_of.Select(r => r.ToProto()));
                }

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