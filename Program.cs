using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Render / Railway / Fly تضع المنفذ في متغير PORT
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://*:{port}");
}

var jwtKey = Encoding.UTF8.GetBytes("SuperSecretKey_12345678901234567890"); // >= 32 bytes for HS256
var messages = new Dictionary<string, List<ChatMessage>>();
var RefreshTokens = new Dictionary<string, string>();


builder.Services.AddSignalR();
builder.Services.AddSingleton(messages);

builder.Services.AddAuthentication("Cookies").AddCookie("Cookies", opition => {
    opition.LoginPath = "/auth/login";
    opition.LogoutPath = "/auth/logout";

    opition.AccessDeniedPath = "/auth/access-denied";
    opition.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    opition.SlidingExpiration = true;
    opition.Cookie.HttpOnly = true;
    opition.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    opition.Cookie.SameSite = SameSiteMode.Lax;
    opition.Cookie.Name = "auth";
    

}).AddJwtBearer(options => {
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters{
        IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
        ValidIssuer = "MyServer",
        ValidAudience = "MyClients",
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // SignalR WebSocket
            var token = context.Request.Query["access_token"].ToString();

            // HttpOnly cookie
            if (string.IsNullOrEmpty(token))
                token = context.Request.Cookies["accessToken"] ?? "";

            if (!string.IsNullOrEmpty(token))
                context.Token = token;

            return Task.CompletedTask;
        }
    };

});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/auth/cookies/login", async context => {
    var claims = new List<Claim> {
        new(ClaimTypes.Name, "John Doe"),
        new(ClaimTypes.Email, "john.doe@example.com"),
        new(ClaimTypes.Role, "Admin"),
    };

    var identity = new ClaimsIdentity(claims, "Cookies");
    var principal = new ClaimsPrincipal(identity);

    await context.SignInAsync("Cookies", principal, new AuthenticationProperties { IsPersistent = true });

    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("{\"message\": \"تم تسجيل الدخول بنجاح\"}");
});

app.MapGet("/auth/cookies/logout", async context => {
    await context.SignOutAsync("Cookies");
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync("{\"message\": \"تم تسجيل الخروج بنجاح\"}");
});

string CreateAccessToken(string userId)
{
    var token = new JwtSecurityToken(
        issuer: "MyServer",
        audience: "MyClients",
        claims: [new Claim(ClaimTypes.Name, userId)],
        expires: DateTime.UtcNow.AddMinutes(10),
        signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(jwtKey),
            SecurityAlgorithms.HmacSha256)
    );
    return new JwtSecurityTokenHandler().WriteToken(token);
}

void SetAuthCookies(HttpContext context, string accessToken, string refreshToken)
{
    var cookieOptions = new CookieOptions
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/"
    };

    context.Response.Cookies.Append("accessToken", accessToken, new CookieOptions
    {
        HttpOnly = cookieOptions.HttpOnly,
        Secure = cookieOptions.Secure,
        SameSite = cookieOptions.SameSite,
        Path = cookieOptions.Path,
        Expires = DateTimeOffset.UtcNow.AddMinutes(1)
    });

    context.Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
    {
        HttpOnly = cookieOptions.HttpOnly,
        Secure = cookieOptions.Secure,
        SameSite = cookieOptions.SameSite,
        Path = cookieOptions.Path,
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });
}

void ClearAuthCookies(HttpContext context)
{
    context.Response.Cookies.Delete("accessToken");
    context.Response.Cookies.Delete("refreshToken");
}

app.MapGet("/auth/jwt/login", async context =>
{
    var userId = "011";
    var accessToken = CreateAccessToken(userId);
    var refreshToken = Guid.NewGuid().ToString();

    RefreshTokens[refreshToken] = userId;
    SetAuthCookies(context, accessToken, refreshToken);

    await context.Response.WriteAsJsonAsync(new { message = "تم تسجيل الدخول بـ JWT (HttpOnly cookies)" });
});

app.MapGet("/auth/jwt/refresh", async context =>
{
    var refreshToken = context.Request.Cookies["refreshToken"];

    if (string.IsNullOrEmpty(refreshToken) || !RefreshTokens.TryGetValue(refreshToken, out var userId))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Refresh token غير صالح" });
        return;
    }

    var accessToken = CreateAccessToken(userId);
    SetAuthCookies(context, accessToken, refreshToken);

    await context.Response.WriteAsJsonAsync(new { message = "تم تجديد access token" });
});

app.MapGet("/auth/jwt/logout", async context =>
{
    var refreshToken = context.Request.Cookies["refreshToken"];
    if (!string.IsNullOrEmpty(refreshToken))
        RefreshTokens.Remove(refreshToken);

    ClearAuthCookies(context);
    await context.Response.WriteAsJsonAsync(new { message = "تم تسجيل الخروج من JWT" });
});

app.MapGet("/auth/access-denied", async context => {
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("{\"message\": \"ليس لديك صلاحية للدخول إلى هذه الصفحة\"}");
});

app.MapHub<AppHub>("/appHub");
app.MapHub<ChatHub>("/chatHub");


app.Run();
