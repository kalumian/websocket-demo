using System.Security.Cryptography;

public static class RoomCodeHelper
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate(int length = 6)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
