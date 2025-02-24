using System.Collections.Concurrent;
using System.Threading.Channels;
using CKAN;
using CKAN.Configuration;
using CKAN.Versioning;
using Grpc.Core;
using log4net;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

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
    private readonly ChannelWriter<Action> _writer;
    private readonly ILogger<CkanManager> _logger;

    public CkanManager(ILogger<CkanManager> logger)
    {
        _logger = logger;
        
        var channel = Channel.CreateUnbounded<Action>();
        _writer = channel.Writer;
        var processor = new ActionProcessor(channel.Reader, _logger);
        Task.Run(() => processor.RunAsync());
    }
    
    public async Task RunAction(ClientHandle handle, CancellationToken token)
    {
        var messageChannel = Channel.CreateUnbounded<ActionMessage>();
        var replyChannel = Channel.CreateUnbounded<ActionReply>();
        var completionSource = new TaskCompletionSource();
        var action = new Action(_logger)
        {
            Client = handle,
            CancellationToken = token,
            CompletionSource = completionSource,
        };
        await _writer.WriteAsync(action, token);
        await completionSource.Task;
    }

    private class ActionProcessor(ChannelReader<Action> reader, ILogger logger)
    {
        private readonly GameInstanceManager _instanceManager = new(new NullUser());
        private Action? _currentAction = null;

        public async Task RunAsync()
        {
            await foreach (var action in reader.ReadAllAsync())
            {
                _currentAction = action;
                try
                {
                    await ProcessOne(action);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private async Task ProcessOne(Action action)
        {
            var request = await action.ReadMessageAsync();
            if (request == null) return;
            
            try
            {
                switch (request.RequestCase)
                {
                    case ActionMessage.RequestOneofCase.InstancesListRequest:
                        await action.InstancesList(_instanceManager);
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
            catch (Kraken err)
            {
                logger.LogError("{Error}", err);
                await action.FailAsync(err.ToString());
            }
            
            action.CompletionSource.SetResult();
        }
    }

    private class Action(ILogger logger)
    {
        public required ClientHandle Client { get; init; }
        public required CancellationToken CancellationToken { get; init; }
        public required TaskCompletionSource CompletionSource { get; init; }

        public async Task<ActionMessage?> ReadMessageAsync()
        {
            var hasNext = await Client.Reader.MoveNext(CancellationToken);
            return hasNext ? Client.Reader.Current : null;
        }

        public Task WriteMessageAsync(ActionReply message)
        {
            return Client.Writer.WriteAsync(message, CancellationToken);
        }

        public async Task FailAsync(string message)
        {
            var reply = new ActionReply
            {
                Failure = new FailureMessage
                {
                    Message = message,
                },
            };
            await WriteMessageAsync(reply);
        }

        public ContinueRequest WaitForContinue(ActionReply reply)
        {
            var message = Task.Run(async () =>
                {
                    await WriteMessageAsync(reply);
                    return await ReadMessageAsync();
                }, CancellationToken)
                .GetAwaiter()
                .GetResult();
            
            if (message?.RequestCase != ActionMessage.RequestOneofCase.ContinueRequest)
            {
                throw new InvalidMessageKraken("ContinueRequest", message?.RequestCase.ToString() ?? "client disconnected");
            }

            return message.ContinueRequest;
        }

        public async Task InstancesList(GameInstanceManager manager)
        {
            if (manager.Instances.Count == 0)
            {
                manager.FindAndRegisterDefaultInstances();
            }
            logger.LogInformation("There are {NumInstances} instances", manager.Instances.Count);
            var instances = manager.Instances.Select(item => new Instance
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
    }
}

public class InvalidMessageKraken(string expected, string actual)
    : Kraken($"Expected a {expected} message, got a {actual} message");

public static class GameVersionExtension
{
    public static Game.Types.Version ToProto(this GameVersion version)
    {
        return new Game.Types.Version
        {
            Major = version.IsMajorDefined ? version.Major : null,
            Minor = version.IsMinorDefined ? version.Minor : null,
            Patch = version.IsPatchDefined ? version.Patch : null,
            Build = version.IsBuildDefined ? version.Build : null,
        };
    }
}