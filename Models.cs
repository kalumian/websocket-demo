using System.ComponentModel.DataAnnotations;

public class UserEntity
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string Username { get; set; } = "";

    [MaxLength(128)]
    public string PasswordHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RoomEntity
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = "";

    /// <summary>كود الدعوة للانضمام (مثل: K7M2QX)</summary>
    [MaxLength(16)]
    public string InviteCode { get; set; } = "";

    /// <summary>اختياري — إن وُجد يجب إدخاله مع الكود أو الاسم</summary>
    [MaxLength(128)]
    public string? PasswordHash { get; set; }

    [MaxLength(64)]
    public string CreatedBy { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ChatMessageEntity> Messages { get; set; } = [];
}

public class ChatMessageEntity
{
    public int Id { get; set; }

    public int RoomId { get; set; }
    public RoomEntity Room { get; set; } = null!;

    [MaxLength(64)]
    public string Username { get; set; } = "";

    [MaxLength(2000)]
    public string Message { get; set; } = "";

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

public class RefreshTokenEntity
{
    [MaxLength(64)]
    public string Token { get; set; } = "";

    [MaxLength(64)]
    public string Username { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }
}

public record AuthRequest(string Username, string Password);
public record CreateRoomRequest(string Name, string? Password);
public record JoinRoomRequest(string? Code, string? Name, string? Password);
