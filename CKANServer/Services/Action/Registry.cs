using CKAN;
using CKANServer.Utils;

namespace CKANServer.Services.Action;

public partial class CkanAction
{
    private async Task WriteRegistryOpReply(RegistryOperationResult result, string? errorDetails = null)
    {
        var reply = new RegistryOperationReply
        {
            Result = result,
        };
        if (errorDetails != null) reply.ErrorDetails = errorDetails;

        await WriteMessageAsync(new ActionReply
        {
            RegistryOperationReply = reply,
        });
    }

    public async Task PrepopulateRegistry(RegistryPrepopulateRequest request, bool isRetry = false)
    {
        logger.LogInformation("Prepopulating registry for instance {Name}", request.InstanceName);
        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        var regMgr = RegistryManagerFor(instance);

        try
        {
            RepoManager.Prepopulate(regMgr.registry.Repositories.Values.ToList(), Progress);
        }
        catch (RegistryInUseKraken ex)
        {
            if (!isRetry && request.ForceLock)
            {
                File.Delete(ex.lockfilePath);
                await PrepopulateRegistry(request, true);
                return;
            }

            await WriteRegistryOpReply(RegistryOperationResult.RorRegistryInUse);
        }
        catch (NotKSPDirKraken ex)
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorNotAnInstance, ex.path);
            return;
        }

        await WriteRegistryOpReply(RegistryOperationResult.RorSuccess);
    }

    public async Task AvailableModules(RegistryAvailableModulesRequest request)
    {
        logger.LogInformation("Fetching modules available to instance {Name}", request.InstanceName);
        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        var regMgr = RegistryManagerFor(instance);
        var modules = RepoManager.GetAllAvailableModules(regMgr.registry.Repositories.Values);

        var reply = new RegistryAvailableModulesReply();
        reply.Modules.AddRange(modules
            .Select(m => m.ToProto(RepoManager, regMgr.registry))
            .OfType<Module>());

        await WriteMessageAsync(new ActionReply
        {
            RegistryOperationReply = new RegistryOperationReply
            {
                Result = RegistryOperationResult.RorSuccess,
                AvailableModules = reply,
            },
        });
    }

    public async Task ModuleCategories(RegistryModuleCategoriesRequest request)
    {
        logger.LogInformation("Sorting modules into categories relevant to instance {Name}", request.InstanceName);
        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        ApplyCompatOptions(request, instance);

        var regMgr = RegistryManagerFor(instance);
        var sorter =
            regMgr.registry.SetCompatibleVersion(instance.StabilityToleranceConfig, instance.VersionCriteria());

        var reply = new RegistryModuleCategoriesReply();
        reply.LatestCompatibleReleases.Add(ConvertToDictionary(sorter.LatestCompatible));
        reply.LatestIncompatibleReleases.Add(ConvertToDictionary(sorter.LatestIncompatible));

        await WriteMessageAsync(new ActionReply
        {
            RegistryOperationReply = new RegistryOperationReply
            {
                Result = RegistryOperationResult.RorSuccess,
                ModuleCategories = reply,
            },
        });

        return;

        Dictionary<string, string> ConvertToDictionary(ICollection<CkanModule> collection) =>
            collection
                .Select(module => KeyValuePair.Create(module.identifier, module.version.ToString()))
                .ToDictionary();
    }

    private static void ApplyCompatOptions(RegistryModuleCategoriesRequest request, GameInstance instance)
    {
        var stabilityTolerance = instance.StabilityToleranceConfig;
        if (request.CompatOptions == null) return;

        stabilityTolerance.OverallStabilityTolerance =
            request.CompatOptions.StabilityTolerance.FromProto() ?? ReleaseStatus.stable;

        var overrides = request.CompatOptions.StabilityToleranceOverrides;
        var overriddenIds = stabilityTolerance.OverriddenModIdentifiers
            .Union(overrides.Keys);

        foreach (var id in overriddenIds)
        {
            ReleaseStatus? tolerance = null;
            if (overrides.ContainsKey(id))
            {
                tolerance = request.CompatOptions.StabilityToleranceOverrides[id].FromProto();
            }

            stabilityTolerance.SetModStabilityTolerance(id, tolerance);
        }

        stabilityTolerance.Save();

        if (request.CompatOptions.VersionCompatibility is { } versionCompat)
        {
            instance.SetCompatibleVersions(versionCompat
                .CompatibleVersions
                .Select(version => version.ToCkan())
                .ToList());
        }
    }
}