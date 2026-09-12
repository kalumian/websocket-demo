using Microsoft.AspNetCore.SignalR;

public interface IChatHub
{
    Task ReceiveMessage(string user, string message);
}

public record ChatMessage(string User, string Message);

public class ChatHub(Dictionary<string, List<ChatMessage>> messages) : Hub<IChatHub>
{
    private readonly Dictionary<string, List<ChatMessage>> _messages = messages;

    public async Task JoinGroup(string groupName)
    {
        if (!_messages.ContainsKey(groupName))
            _messages[groupName] = [];

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        await Clients.Group(groupName).ReceiveMessage(Context.ConnectionId, " joined the group successfully");
        Console.WriteLine($"{Context.ConnectionId} joined the group {groupName}");
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        await Clients.Group(groupName).ReceiveMessage(Context.ConnectionId, " left the group successfully");
        Console.WriteLine($"{Context.ConnectionId} left the group {groupName}");
    }

    public async Task SendMessage(string groupName, string message)
    {
        if (!_messages.ContainsKey(groupName))
            _messages[groupName] = [];

        var user = Context.ConnectionId[..Math.Min(6, Context.ConnectionId.Length)];
        var chatMessage = new ChatMessage(user, message);
        _messages[groupName].Add(chatMessage);

        await Clients.Group(groupName).ReceiveMessage(user, message);
    }

    public Task<List<ChatMessage>> GetMessages(string groupName)
    {
        if (!_messages.TryGetValue(groupName, out var list))
            return Task.FromResult(new List<ChatMessage>());

        return Task.FromResult(list);
    }
}
