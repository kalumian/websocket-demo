using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://*:{port}");

var jwtKey = Encoding.UTF8.GetBytes("SuperSecretKey_12345678901234567890");
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSignalR();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
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
                var token = context.Request.Query["access_token"].ToString();
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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // جداول جديدة إن لم تكن موجودة
    await db.Database.EnsureCreatedAsync();

    // ترقية أعمدة الخصوصية إن كان الجدول قديماً
    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "InviteCode" character varying(16) NOT NULL DEFAULT '';
        ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "PasswordHash" character varying(128) NULL;
        ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "CreatedBy" character varying(64) NOT NULL DEFAULT '';
        """);

    if (!await db.Users.AnyAsync())
    {
        db.Users.AddRange(
            new UserEntity { Username = "أحمد", PasswordHash = PasswordHelper.Hash("123456") },
            new UserEntity { Username = "سارة", PasswordHash = PasswordHelper.Hash("123456") });
    }

    // إصلاح الغرف بدون كود + بذور أولية
    var rooms = await db.Rooms.ToListAsync();
    foreach (var room in rooms.Where(r => string.IsNullOrWhiteSpace(r.InviteCode)))
        room.InviteCode = await UniqueCodeAsync(db);

    if (!rooms.Any())
    {
        db.Rooms.AddRange(
            new RoomEntity
            {
                Name = "عام",
                InviteCode = await UniqueCodeAsync(db),
                CreatedBy = "النظام"
            },
            new RoomEntity
            {
                Name = "تقنية",
                InviteCode = await UniqueCodeAsync(db),
                PasswordHash = PasswordHelper.Hash("1234"),
                CreatedBy = "النظام"
            });
    }

    await db.SaveChangesAsync();
}

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

string CreateAccessToken(string username)
{
    var token = new JwtSecurityToken(
        issuer: "MyServer",
        audience: "MyClients",
        claims: [new Claim(ClaimTypes.Name, username)],
        expires: DateTime.UtcNow.AddHours(2),
        signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(jwtKey),
            SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

void SetAuthCookies(HttpContext context, string accessToken, string refreshToken)
{
    var secure = context.Request.IsHttps;
    context.Response.Cookies.Append("accessToken", accessToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddHours(2)
    });
    context.Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });
}

void ClearAuthCookies(HttpContext context)
{
    context.Response.Cookies.Delete("accessToken");
    context.Response.Cookies.Delete("refreshToken");
}

async Task IssueTokensAsync(HttpContext context, AppDbContext db, string username)
{
    var accessToken = CreateAccessToken(username);
    var refreshToken = Guid.NewGuid().ToString("N");

    db.RefreshTokens.Add(new RefreshTokenEntity
    {
        Token = refreshToken,
        Username = username,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
    });
    await db.SaveChangesAsync();
    SetAuthCookies(context, accessToken, refreshToken);
}

async Task<string> UniqueCodeAsync(AppDbContext db)
{
    for (var i = 0; i < 20; i++)
    {
        var code = RoomCodeHelper.Generate();
        if (!await db.Rooms.AnyAsync(r => r.InviteCode == code))
            return code;
    }
    return RoomCodeHelper.Generate(8);
}

app.MapPost("/auth/register", async (AuthRequest req, HttpContext context, AppDbContext db) =>
{
    var username = req.Username?.Trim() ?? "";
    var password = req.Password ?? "";

    if (username.Length < 2 || password.Length < 4)
        return Results.BadRequest(new { message = "اسم المستخدم قصير أو كلمة المرور ضعيفة" });

    if (await db.Users.AnyAsync(u => u.Username == username))
        return Results.Conflict(new { message = "هذا الاسم مستخدم مسبقاً" });

    db.Users.Add(new UserEntity
    {
        Username = username,
        PasswordHash = PasswordHelper.Hash(password)
    });
    await db.SaveChangesAsync();
    await IssueTokensAsync(context, db, username);

    return Results.Ok(new { message = "تم إنشاء الحساب", username });
});

app.MapPost("/auth/login", async (AuthRequest req, HttpContext context, AppDbContext db) =>
{
    var username = req.Username?.Trim() ?? "";
    var password = req.Password ?? "";

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null || !PasswordHelper.Verify(password, user.PasswordHash))
        return Results.Unauthorized();

    await IssueTokensAsync(context, db, user.Username);
    return Results.Ok(new { message = "مرحباً بك", username = user.Username });
});

app.MapPost("/auth/logout", async (HttpContext context, AppDbContext db) =>
{
    var refreshToken = context.Request.Cookies["refreshToken"];
    if (!string.IsNullOrEmpty(refreshToken))
    {
        var entity = await db.RefreshTokens.FindAsync(refreshToken);
        if (entity is not null)
        {
            db.RefreshTokens.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    ClearAuthCookies(context);
    return Results.Ok(new { message = "تم تسجيل الخروج" });
});

app.MapPost("/auth/refresh", async (HttpContext context, AppDbContext db) =>
{
    var refreshToken = context.Request.Cookies["refreshToken"];
    if (string.IsNullOrEmpty(refreshToken))
        return Results.Unauthorized();

    var entity = await db.RefreshTokens.FindAsync(refreshToken);
    if (entity is null || entity.ExpiresAt < DateTimeOffset.UtcNow)
        return Results.Unauthorized();

    db.RefreshTokens.Remove(entity);
    await db.SaveChangesAsync();

    await IssueTokensAsync(context, db, entity.Username);
    return Results.Ok(new { message = "تم التجديد", username = entity.Username });
});

app.MapGet("/auth/me", (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();
    return Results.Ok(new { username = context.User.Identity!.Name });
}).RequireAuthorization();

app.MapPost("/api/rooms/create", async (CreateRoomRequest req, HttpContext context, AppDbContext db) =>
{
    var name = req.Name?.Trim() ?? "";
    if (name.Length < 2)
        return Results.BadRequest(new { message = "اسم الغرفة قصير جداً" });

    if (await db.Rooms.AnyAsync(r => r.Name == name))
        return Results.Conflict(new { message = "يوجد غرفة بنفس الاسم" });

    var code = await UniqueCodeAsync(db);
    var room = new RoomEntity
    {
        Name = name,
        InviteCode = code,
        PasswordHash = string.IsNullOrWhiteSpace(req.Password)
            ? null
            : PasswordHelper.Hash(req.Password),
        CreatedBy = context.User.Identity?.Name ?? ""
    };

    db.Rooms.Add(room);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        name = room.Name,
        code = room.InviteCode,
        hasPassword = room.PasswordHash is not null,
        message = "تم إنشاء الغرفة — احفظ الكود للمشاركة"
    });
}).RequireAuthorization();

app.MapPost("/api/rooms/join", async (JoinRoomRequest req, AppDbContext db) =>
{
    var code = req.Code?.Trim();
    var name = req.Name?.Trim();
    var password = req.Password;

    RoomEntity? room = null;
    if (!string.IsNullOrWhiteSpace(code))
        room = await db.Rooms.FirstOrDefaultAsync(r => r.InviteCode == code.ToUpperInvariant() || r.InviteCode == code);
    else if (!string.IsNullOrWhiteSpace(name))
        room = await db.Rooms.FirstOrDefaultAsync(r => r.Name == name);

    if (room is null)
        return Results.NotFound(new { message = "الغرفة غير موجودة" });

    // توحيد الكود للتحقق
    if (!string.IsNullOrWhiteSpace(code))
        code = room.InviteCode;

    var hasPassword = !string.IsNullOrEmpty(room.PasswordHash);

    if (hasPassword)
    {
        if (string.IsNullOrEmpty(password) || !PasswordHelper.Verify(password, room.PasswordHash!))
            return Results.Json(new { message = "كلمة سر الغرفة غير صحيحة" }, statusCode: StatusCodes.Status403Forbidden);
    }
    else
    {
        // بدون كلمة سر: يلزم كود الدعوة
        if (string.IsNullOrWhiteSpace(req.Code) ||
            !req.Code.Trim().Equals(room.InviteCode, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { message = "كود الدعوة غير صحيح" }, statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Ok(new
    {
        name = room.Name,
        code = room.InviteCode,
        hasPassword,
        message = "تم التحقق — يمكنك الدخول"
    });
}).RequireAuthorization();

app.MapHub<ChatHub>("/chatHub");
app.MapHub<AppHub>("/appHub");

app.Run();
