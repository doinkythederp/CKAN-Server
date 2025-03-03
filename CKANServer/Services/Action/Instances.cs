using CKAN;
using CKAN.DLC;
using CKAN.Games;
using CKAN.Games.KerbalSpaceProgram.DLC;
using CKAN.Versioning;
using CKANServer.Utils;

namespace CKANServer.Services.Action;

public partial class CkanAction
{
    private async Task WriteInstanceOpReply(InstanceOperationResult instanceOperationResult, string? errorDetails = null)
    {
        var instanceOperationReply = new InstanceOperationReply
        {
            Result = instanceOperationResult,
        };
        if (errorDetails != null)
        {
            instanceOperationReply.ErrorDetails = errorDetails;
        }
        
        await WriteMessageAsync(new ActionReply
        {
            InstanceOperationReply = instanceOperationReply,
        });
    }

    public void SetupInstances()
    {
        if (InstanceManager.Instances.Count == 0)
        {
            InstanceManager.FindAndRegisterDefaultInstances();
        }
    }

    public async Task ListInstances()
    {
        logger.LogInformation("Listing {NumInstances} instances", InstanceManager.Instances.Count);
        var autoStartInstance = InstanceManager.AutoStartInstance;
        var instances = InstanceManager.Instances.Select(item => new Instance
        {
            // Not sure if there's an actual "ID", but this seems constant enough
            GameId = item.Value.game.ShortName,
            Directory = item.Value.GameDir(),
            Name = item.Value.Name,
            GameVersion = item.Value.Version()?.ToProto(),
            IsDefault = autoStartInstance == item.Value.Name,
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
        logger.LogInformation("Adding instance {Name}", request.Name);
        if (InstanceManager.HasInstance(request.Name))
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorDuplicateInstance);
            return;
        }

        try
        {
            InstanceManager.AddInstance(request.Directory, request.Name, User);
        }
        catch (NotKSPDirKraken ex)
        {
            logger.LogError("Add instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorNotAnInstance, ex.path);
            return;
        }

        await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
    }

    public async Task ForgetInstance(InstanceForgetRequest request)
    {
        logger.LogInformation("Forgetting instance {Name}", request.Name);
        if (!InstanceManager.HasInstance(request.Name))
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorInstanceNotFound);
            return;
        }

        InstanceManager.RemoveInstance(request.Name);
        await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
    }

    public async Task RenameInstance(InstanceRenameRequest request)
    {
        logger.LogInformation("Renaming instance {Name} to {NewName}", request.OldName, request.NewName);
        if (!InstanceManager.HasInstance(request.OldName))
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorInstanceNotFound);
            return;
        }

        InstanceManager.RenameInstance(request.OldName, request.NewName);
        await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
    }

    public async Task SetDefaultInstance(InstanceSetDefaultRequest request)
    {
        logger.LogInformation("Setting default instance {Name}", request.Name);
        try
        {
            InstanceManager.SetAutoStart(request.Name);
        }
        catch (InvalidKSPInstanceKraken ex)
        {
            logger.LogError("Set default instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorInstanceNotFound);
            return;
        }
        catch (NotKSPDirKraken ex)
        {
            logger.LogError("Set default instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorNotAnInstance, ex.path);
            return;
        }

        await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
    }

    public async Task FakeNewInstance(InstanceFakeRequest request)
    {
        logger.LogInformation("Creating fake instance {Name}", request.Name);
        var game = KnownGames.GameByShortName(request.GameId);
        if (game == null)
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorFakerUnknownGame);
            return;
        }

        // If the request has a DLC's version, it implies the user wants that DLC.

        var dlcs = new Dictionary<IDlcDetector, GameVersion>();
        if (request.MakingHistoryVersion != null)
        {
            dlcs.Add(new MakingHistoryDlcDetector(), request.MakingHistoryVersion.ToCkan());
        }

        if (request.BreakingGroundVersion != null)
        {
            dlcs.Add(new BreakingGroundDlcDetector(), request.BreakingGroundVersion.ToCkan());
        }

        var version = request.Version.ToCkan();
        // Check if we have enough info about the version - we need the first 3 components, but the build is optional.
        if (version is not { IsMajorDefined: true, IsMinorDefined: true, IsPatchDefined: true })
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorFakerUnknownVersion,
                "Not enough details about the game version. Major, minor, and patch must be defined.");
            return;
        }

        try
        {
            InstanceManager.FakeInstance(game, request.Name, request.Directory, version, dlcs);
            if (request.UseAsNewDefault && InstanceManager.HasInstance(request.Name))
            {
                InstanceManager.SetAutoStart(request.Name);
            }
        }
        catch (InstanceNameTakenKraken ex)
        {
            logger.LogError("Create fake instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorDuplicateInstance, ex.instName);
            return;
        }
        catch (BadInstallLocationKraken ex)
        {
            // The folder exists and is not empty.
            logger.LogError("Create fake instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorNewInstanceDirExists, ex.Message);
            return;
        }
        catch (WrongGameVersionKraken ex)
        {
            // Thrown because the specified game instance is too old for one of the selected DLCs.
            logger.LogError("Create fake instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorFakerVersionTooOld, ex.Message);
            return;
        }
        catch (NotKSPDirKraken ex)
        {
            // Something went wrong adding the new instance to the registry,
            // most likely because the newly created directory is somehow not valid.
            logger.LogError("Create fake instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorFakerFailed, ex.path);
            return;
        }
        catch (BadGameVersionKraken ex)
        {
            // Thrown if the specified game version isn't known to CKAN.
            logger.LogError("Create fake instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorFakerUnknownVersion, ex.Message);
            return;
        }

        if (!InstanceManager.HasInstance(request.Name))
        {
            logger.LogError("Create fake instance failed because the instance was ultimately not added");
            await WriteInstanceOpReply(InstanceOperationResult.IorFakerFailed);
            return;
        }

        await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
    }

    public async Task CloneInstance(InstanceCloneRequest request)
    {
        logger.LogInformation("Cloning instance {Name} to {NewName}", request.Name, request.NewName);
        if (!InstanceManager.HasInstance(request.Name))
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorInstanceNotFound);
            return;
        }

        var existingInstance = InstanceManager.Instances[request.Name];

        try
        {
            InstanceManager.CloneInstance(
                existingInstance,
                request.NewName,
                request.NewDirectory,
                shareStockFolders: request.CreateSymlinksForStockDirs);
        }
        catch (NotKSPDirKraken ex)
        {
            logger.LogError("Clone instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorNotAnInstance, ex.path);
            return;
        }
        catch (PathErrorKraken ex)
        {
            logger.LogError("Clone instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorNewInstanceDirExists, ex.Message + ex.path);
            return;
        }
        catch (IOException ex)
        {
            logger.LogError("Clone instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorCloneFailed, ex.ToString());
            return;
        }
        catch (InstanceNameTakenKraken ex)
        {
            logger.LogError("Clone instance failed: {Error}", ex);
            await WriteInstanceOpReply(InstanceOperationResult.IorDuplicateInstance, ex.instName);
            return;
        }

        await WriteInstanceOpReply(InstanceOperationResult.IorSuccess);
    }
}