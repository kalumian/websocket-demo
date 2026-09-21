using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

public interface IAppHub
{
    Task SendMessage(string message);
    Task YesIheardYou(string message);
}

[Authorize(AuthenticationSchemes = "Bearer")]
public class AppHub : Hub<IAppHub>
{
    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"Client connected: {Context.ConnectionId}");
        await Clients.All.SendMessage($"Client connected: {Context.ConnectionId}");

        await Clients.Client(Context.ConnectionId)
            .YesIheardYou("Hello from the server I'm here " + Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Console.WriteLine($"Client disconnected: {Context.ConnectionId}");
        return base.OnDisconnectedAsync(exception);
    }

    public async Task Ping(bool multiple = false)
    {
        var random = new Random();
        Console.WriteLine(random.Next(1, 100));

        if (multiple)
        {
            await Clients.All.SendMessage(random.Next(1, 100).ToString());
        }
        else
        {
            await Clients.Client(Context.ConnectionId)
                .SendMessage(random.Next(1, 100).ToString());
        }
    }
}
