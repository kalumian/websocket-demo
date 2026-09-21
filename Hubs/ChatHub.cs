using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

public interface IChatClient
{
    Task ReceiveMessage(string user, string message, string at);
    Task UserTyping(string user, bool isTyping);
    Task UserJoined(string user);
    Task UserLeft(string user);
}

[Authorize(AuthenticationSchemes = "Bearer")]
public class ChatHub(AppDbContext db) : Hub<IChatClient>
{
    // اتصالات مصرّح لها بدخول غرفة بعد التحقق من الكود/كلمة السر
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> Access = new();

    private string Username =>
        Context.User?.FindFirstValue(ClaimTypes.Name)
        ?? Context.User?.Identity?.Name
        ?? "زائر";

    public async Task<bool> JoinRoom(string roomName, string? code, string? password)
    {
        roomName = roomName?.Trim() ?? "";
        if (string.IsNullOrEmpty(roomName)) return false;

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Name == roomName);
        if (room is null) return false;

        if (!IsAuthorized(room, code, password))
            return false;

        var set = Access.GetOrAdd(Context.ConnectionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        set[roomName] = 0;

        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.OthersInGroup(roomName).UserJoined(Username);
        return true;
    }

    public async Task LeaveRoom(string roomName)
    {
        roomName = roomName.Trim();
        if (Access.TryGetValue(Context.ConnectionId, out var set))
            set.TryRemove(roomName, out _);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
        await Clients.OthersInGroup(roomName).UserLeft(Username);
    }

    public async Task SendMessage(string roomName, string message)
    {
        roomName = roomName.Trim();
        message = message?.Trim() ?? "";
        if (string.IsNullOrEmpty(roomName) || string.IsNullOrEmpty(message)) return;
        if (!HasAccess(roomName)) return;

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Name == roomName);
        if (room is null) return;

        var at = DateTimeOffset.UtcNow;
        db.Messages.Add(new ChatMessageEntity
        {
            RoomId = room.Id,
            Username = Username,
            Message = message,
            At = at
        });
        await db.SaveChangesAsync();

        await Clients.Group(roomName).ReceiveMessage(Username, message, at.ToString("o"));
        await Clients.OthersInGroup(roomName).UserTyping(Username, false);
    }

    public async Task Typing(string roomName, bool isTyping)
    {
        roomName = roomName.Trim();
        if (!HasAccess(roomName)) return;
        await Clients.OthersInGroup(roomName).UserTyping(Username, isTyping);
    }

    public async Task<List<object>> GetMessages(string roomName)
    {
        roomName = roomName.Trim();
        if (!HasAccess(roomName)) return [];

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Name == roomName);
        if (room is null) return [];

        return await db.Messages
            .AsNoTracking()
            .Where(m => m.RoomId == room.Id)
            .OrderBy(m => m.At)
            .Select(m => (object)new
            {
                user = m.Username,
                message = m.Message,
                at = m.At.ToString("o")
            })
            .ToListAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Access.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    private bool HasAccess(string roomName) =>
        Access.TryGetValue(Context.ConnectionId, out var set) && set.ContainsKey(roomName);

    private static bool IsAuthorized(RoomEntity room, string? code, string? password)
    {
        var codeOk = !string.IsNullOrWhiteSpace(code)
            && code.Trim().Equals(room.InviteCode, StringComparison.OrdinalIgnoreCase);

        var hasPassword = !string.IsNullOrEmpty(room.PasswordHash);
        if (!hasPassword)
            return codeOk;

        var passwordOk = !string.IsNullOrEmpty(password)
            && PasswordHelper.Verify(password, room.PasswordHash!);

        // الغرف المحمية تتطلب كود الدعوة + كلمة السر معاً
        return codeOk && passwordOk;
    }
}