using CKAN;

namespace CKANServer.Services.Action;

public partial class CkanAction
{
    
    public async Task ListInstances()
    {
        if (InstanceManager.Instances.Count == 0)
        {
            InstanceManager.FindAndRegisterDefaultInstances();
        }
        
        logger.LogInformation("There are {NumInstances} instances", InstanceManager.Instances.Count);
        var instances = InstanceManager.Instances.Select(item => new Instance
        {
            // Not sure if there's an actual "ID", but this seems constant enough
            GameId = item.Value.game.ShortName,
            Directory = item.Value.GameDir(),
            Name = item.Value.Name,
            GameVersion = item.Value.Version()?.ToProto(),
        });
        
        var reply = new InstancesListReply();
        reply.Instances.Add(instances);
        await WriteMessageAsync(new ActionReply
        {
            InstancesListReply = reply,
        });
    }

    public async Task AddInstance(InstanceAddRequest request)
    {
        if (InstanceManager.HasInstance(request.Name))
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorDuplicateInstance);
            return;
        }
        
        try
        {
            InstanceManager.AddInstance(request.Directory, request.Name, User);
            await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
        }
        catch (NotKSPDirKraken ex)
        {
            logger.LogError("Add instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorNotAnInstance);
        }
    }

    public async Task ForgetInstance(InstanceForgetRequest request)
    {
        if (!InstanceManager.HasInstance(request.Name))
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorInstanceNotFound);
            return;
        }
        
        InstanceManager.RemoveInstance(request.Name);
        await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
    }

    private async Task WriteInstanceOpReply(InstanceOperationResult instanceOperationResult)
    {
        await WriteMessageAsync(new ActionReply
        {
            InstanceOperationReply = new InstanceOperationReply
            {
                Result = instanceOperationResult,
            },
        });
    }
}