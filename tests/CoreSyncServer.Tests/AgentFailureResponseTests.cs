using CoreSync.Http;
using CoreSyncServer.Filters;
using CoreSyncServer.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace CoreSyncServer.Tests;

/// <summary>
/// An agent that answers a sync RPC with an error and an agent that does not answer at all have
/// always both been a 503 with the exception message as body. They still are - a server deployed
/// ahead of its clients must look identical to them - and the only thing that tells them apart is the
/// error code header a current client reads.
/// </summary>
public class AgentFailureResponseTests
{
    [Fact]
    public void AgentOperationFailed_IsStillAnAgentOfflineExceptionWithTheLegacyMessage()
    {
        var ex = new AgentOperationFailedException(12, "SyncApplyChanges", new HubException("FK violation on Posts"));

        // What SyncController catches, and the body text a 1.0.1 client has been receiving
        ex.Should().BeAssignableTo<AgentOfflineException>();
        ex.Message.Should().Be("Agent RPC 'SyncApplyChanges' failed for DataStore 12.");
        ex.DataStoreId.Should().Be(12);
        ex.InnerException.Should().BeOfType<HubException>();
    }

    [Fact]
    public void AgentFailure_Keeps503AndTellsTheTwoCasesApartByCode()
    {
        var reported = new DefaultHttpContext();
        var reportedResult = SyncErrorResponses.AgentFailure(reported, new AgentOperationFailedException(12, "SyncApplyChanges", new HubException("boom")));

        reportedResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        reported.Response.Headers[SyncHttpHeaders.ErrorCode].ToString().Should().Be(SyncHttpErrorCodes.AgentError);
        reported.Response.Headers[SyncHttpHeaders.TraceId].ToString().Should().NotBeNullOrWhiteSpace();
        reportedResult.Value.Should().BeOfType<SyncErrorDetails>()
            .Which.Message.Should().Be("Agent RPC 'SyncApplyChanges' failed for DataStore 12.");

        var offline = new DefaultHttpContext();
        var offlineResult = SyncErrorResponses.AgentFailure(offline, new AgentOfflineException(12, "No agent is currently connected for DataStore 12."));

        offlineResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        offline.Response.Headers[SyncHttpHeaders.ErrorCode].ToString().Should().Be(SyncHttpErrorCodes.AgentUnavailable);

        // Neither stops a client from retrying, which is what every client did with a 503
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.AgentError).Should().BeFalse();
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.AgentUnavailable).Should().BeFalse();
    }
}
