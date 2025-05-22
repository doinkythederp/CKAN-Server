using System.Threading.Channels;
using CKAN;
using CKANServer.Services.Action;
using Grpc.Core;

namespace CKANServer.Services;

public interface ICkanManager
{
    public Task RunAction(ClientHandle handle, CancellationToken token);
}

public readonly struct ClientHandle
{
    public required IAsyncStreamReader<ActionMessage> Reader { get; init; }
    public required IAsyncStreamWriter<ActionReply> Writer { get; init; }
}

public class CkanManager : ICkanManager
{
    private readonly ChannelWriter<CkanAction.ActionContext> _writer;

    public CkanManager(ILogger<CkanManager> logger)
    {
        var channel = Channel.CreateUnbounded<CkanAction.ActionContext>();
        _writer = channel.Writer;
        var processor = new ActionProcessor(channel.Reader, logger);
        Task.Run(() => processor.RunAsync());
    }

    public async Task RunAction(ClientHandle handle, CancellationToken token)
    {
        var completionSource = new TaskCompletionSource();
        var actionCtx = new CkanAction.ActionContext
        {
            Client = handle,
            CancellationToken = token,
            CompletionSource = completionSource,
        };
        await _writer.WriteAsync(actionCtx, token);
        await completionSource.Task;
    }

    private class ActionProcessor(ChannelReader<CkanAction.ActionContext> reader, ILogger logger)
    {
        private readonly GameInstanceManager _instanceManager = new(new NullUser());
        private readonly RepositoryDataManager _repoMgr = new();

        public async Task RunAsync()
        {
            await foreach (var actionCtx in reader.ReadAllAsync())
            {
                var action = new CkanAction(logger)
                {
                    Context = actionCtx,
                    InstanceManager = _instanceManager,
                    RepoManager = _repoMgr,
                    User = new NullUser(),
                };

                try
                {
                    await ProcessOne(action);
                }
                catch (OperationCanceledException)
                {
                    logger.LogDebug("Client disconnected before finishing request");
                }
            }
        }

        private async Task ProcessOne(CkanAction action)
        {
            var request = await action.ReadMessageAsync();
            if (request == null) return;

            try
            {
                action.SetupInstances();

                switch (request.RequestCase)
                {
                    // # Game Instance Requests
                    case ActionMessage.RequestOneofCase.InstancesListRequest:
                        await action.ListInstances();
                        break;
                    case ActionMessage.RequestOneofCase.InstanceAddRequest:
                        await action.AddInstance(request.InstanceAddRequest);
                        break;
                    case ActionMessage.RequestOneofCase.InstanceForgetRequest:
                        await action.ForgetInstance(request.InstanceForgetRequest);
                        break;
                    case ActionMessage.RequestOneofCase.InstanceRenameRequest:
                        await action.RenameInstance(request.InstanceRenameRequest);
                        break;
                    case ActionMessage.RequestOneofCase.InstanceSetDefaultRequest:
                        await action.SetDefaultInstance(request.InstanceSetDefaultRequest);
                        break;
                    case ActionMessage.RequestOneofCase.InstanceFakeRequest:
                        await action.FakeNewInstance(request.InstanceFakeRequest);
                        break;
                    case ActionMessage.RequestOneofCase.InstanceCloneRequest:
                        await action.CloneInstance(request.InstanceCloneRequest);
                        break;

                    case ActionMessage.RequestOneofCase.RegistryPrepopulateRequest:
                        await action.PrepopulateRegistry(request.RegistryPrepopulateRequest);
                        break;
                    case ActionMessage.RequestOneofCase.RegistryAvailableModulesRequest:
                        await action.AvailableModules(request.RegistryAvailableModulesRequest);
                        break;
                    case ActionMessage.RequestOneofCase.RegistryModuleStatesRequest:
                        await action.ModuleStates(request.RegistryModuleStatesRequest);
                        break;
                    case ActionMessage.RequestOneofCase.RegistryCompatibleModuleReleases:
                        await action.CompatibleModuleReleases(request.RegistryCompatibleModuleReleases);
                        break;
                    case ActionMessage.RequestOneofCase.RegistryOptionalDependenciesRequest:
                        await action.OptionalDependencies(request.RegistryOptionalDependenciesRequest);
                        break;
                    case ActionMessage.RequestOneofCase.RegistryPerformInstallRequest:
                        await action.PerformInstall(request.RegistryPerformInstallRequest);
                        break;
                    case ActionMessage.RequestOneofCase.RegistryUpdateRequest:
                        await action.RefreshRegistry(request.RegistryUpdateRequest);
                        break;

                    case ActionMessage.RequestOneofCase.ContinueRequest:
                        await action.FailAsync("A continuation request cannot be the first message sent");
                        break;
                    case ActionMessage.RequestOneofCase.None:
                    default:
                        await action.FailAsync("Unknown message type");
                        break;
                }
            }
            catch (Exception err)
            {
                logger.LogError("ERROR! {Error}", err);
                await action.FailAsync(err.ToString());
            }

            action.Context.CompletionSource.SetResult();
        }
    }
}

public class InvalidMessageKraken(string expected, string actual)
    : Kraken($"Expected a {expected} message, got a {actual} message");
