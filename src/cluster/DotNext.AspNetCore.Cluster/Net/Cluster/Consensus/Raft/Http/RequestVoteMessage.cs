﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using static System.Globalization.CultureInfo;

namespace DotNext.Net.Cluster.Consensus.Raft.Http;

internal sealed class RequestVoteMessage : RaftHttpMessage, IHttpMessageReader<Result<bool>>
{
    internal const string RecordIndexHeader = "X-Raft-Record-Index";
    internal const string RecordTermHeader = "X-Raft-Record-Term";
    internal new const string MessageType = "RequestVote";

    internal readonly long LastLogIndex;
    internal readonly long LastLogTerm;

    internal RequestVoteMessage(in ClusterMemberId sender, long term, long lastLogIndex, long lastLogTerm)
        : base(MessageType, sender, term)
    {
        LastLogIndex = lastLogIndex;
        LastLogTerm = lastLogTerm;
    }

    private RequestVoteMessage(IDictionary<string, StringValues> headers)
        : base(headers)
    {
        LastLogIndex = ParseHeader(headers, RecordIndexHeader, Int64Parser);
        LastLogTerm = ParseHeader(headers, RecordTermHeader, Int64Parser);
    }

    internal RequestVoteMessage(HttpRequest request)
        : this(request.Headers)
    {
    }

    internal override void PrepareRequest(HttpRequestMessage request)
    {
        request.Headers.Add(RecordIndexHeader, LastLogIndex.ToString(InvariantCulture));
        request.Headers.Add(RecordTermHeader, LastLogTerm.ToString(InvariantCulture));
        base.PrepareRequest(request);
    }

    Task<Result<bool>> IHttpMessageReader<Result<bool>>.ParseResponseAsync(HttpResponseMessage response, CancellationToken token) => ParseBoolResponseAsync(response, token);

    internal static Task SaveResponseAsync(HttpResponse response, Result<bool> result, CancellationToken token) => RaftHttpMessage.SaveResponseAsync(response, result, token);
}