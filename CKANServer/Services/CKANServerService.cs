using CKAN;
using Grpc.Core;
using CKANServer;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace CKANServer.Services;

public class CKANServerService(ILogger<CKANServerService> logger, ICkanManager manager) : CKANServer.CKANServerBase
{
    public override Task<GetVersionReply> GetVersion(GetVersionRequest request, ServerCallContext context)
    {
        logger.LogInformation("GetVersion called");
        return Task.FromResult(new GetVersionReply
        {
            Version = Meta.GetVersion(VersionFormat.Full),
            ProductName = Meta.ProductName,
        });
    }

    public override async Task ProcessAction(IAsyncStreamReader<ActionMessage> requestStream,
        IServerStreamWriter<ActionReply> responseStream, ServerCallContext context)
    {
        await manager.RunAction(new ClientHandle
        {
            Reader = requestStream,
            Writer = responseStream,
        }, context.CancellationToken);
    }
}