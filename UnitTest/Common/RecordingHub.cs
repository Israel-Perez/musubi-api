using Microsoft.AspNetCore.SignalR;
using Musubi.Api.Modules.Chat;

namespace UnitTest.Common;

/// <summary>Hub falso del chat: registra cada evento (metodo, a quien y la carga).</summary>
internal sealed class RecordingHub : IHubContext<ChatHub>, IHubClients, IClientProxy
{
    private IReadOnlyList<string> users = [];

    public List<(string Method, IReadOnlyList<string> Users, object? Payload)> Sent { get; } = [];

    IHubClients IHubContext<ChatHub>.Clients => this;

    IGroupManager IHubContext<ChatHub>.Groups => throw new NotSupportedException();

    public IClientProxy All => throw new NotSupportedException();

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

    public IClientProxy Client(string connectionId) => throw new NotSupportedException();

    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

    public IClientProxy Group(string groupName) => throw new NotSupportedException();

    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

    public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

    public IClientProxy User(string userId) => throw new NotSupportedException();

    public IClientProxy Users(IReadOnlyList<string> userIds)
    {
        users = userIds;
        return this;
    }

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        Sent.Add((method, users, args is [var payload, ..] ? payload : null));
        return Task.CompletedTask;
    }
}
