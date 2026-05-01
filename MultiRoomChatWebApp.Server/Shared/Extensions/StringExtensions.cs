namespace MultiRoomChatWebApp.Server.Shared.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Sinh một chuỗi ký tự ngẫu nhiên bao gồm chữ cái và số.
    /// </summary>
    /// <param name="length">Độ dài chuỗi cần sinh</param>
    /// <returns>Chuỗi ngẫu nhiên</returns>
    public static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}
