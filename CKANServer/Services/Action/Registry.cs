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
        if (errorDetails != null)
        {
            reply.ErrorDetails = errorDetails;
        }

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
                await PrepopulateRegistry(request, isRetry: true);
                return;
            }

            await WriteRegistryOpReply(RegistryOperationResult.RorRegistryInUse);
        }
        catch (NotKSPDirKraken ex)
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorNotAnInstance, ex.path);
            return;
        }

        await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
    }

    public async Task CompatibleModules(RegistryCompatibleModulesRequest request)
    {
        logger.LogInformation("Fetching modules compatible with instance {Name}", request.InstanceName);
        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        var regMgr = RegistryManagerFor(instance);

        var modules = regMgr.registry.CompatibleModules(instance.StabilityToleranceConfig, instance.VersionCriteria());
        
        var reply = new RegistryCompatibleModulesReply();
        reply.Modules.AddRange(modules.Select(m => m.ToProto()));
        
        await WriteMessageAsync(new ActionReply
        {
            RegistryOperationReply = new RegistryOperationReply
            {
                Result = RegistryOperationResult.RorSuccess,
                CompatibleModules = reply,
            },
        });
    }
}