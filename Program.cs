var builder = WebApplication.CreateBuilder(args);

// Render / Railway / Fly تضع المنفذ في متغير PORT
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://*:{port}");
}

var messages = new Dictionary<string, List<ChatMessage>>();

builder.Services.AddSignalR();
builder.Services.AddSingleton(messages);
builder.Services.Configure<Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<AppHub>("/appHub");
app.MapHub<ChatHub>("/chatHub");
app.Run();
