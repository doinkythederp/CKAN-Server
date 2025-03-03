using CKAN;

namespace CKANServer.Services.Action;


public partial class CkanAction(ILogger logger)
{
    public class ActionContext
    {
        public required ClientHandle Client { get; init; }
        public required CancellationToken CancellationToken { get; init; }
        public required TaskCompletionSource CompletionSource { get; init; }
    }
    
    public required ActionContext Context { get; init; }
    public required GameInstanceManager InstanceManager { get; init; }
    public required IUser User { get; init; }

    public async Task<ActionMessage?> ReadMessageAsync()
    {
        var hasNext = await Context.Client.Reader.MoveNext(Context.CancellationToken);
        return hasNext ? Context.Client.Reader.Current : null;
    }

    public Task WriteMessageAsync(ActionReply message)
    {
        return Context.Client.Writer.WriteAsync(message, Context.CancellationToken);
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
            }, Context.CancellationToken)
            .GetAwaiter()
            .GetResult();
        
        if (message?.RequestCase != ActionMessage.RequestOneofCase.ContinueRequest)
        {
            throw new InvalidMessageKraken("ContinueRequest", message?.RequestCase.ToString() ?? "client disconnected");
        }

        return message.ContinueRequest;
    }
}