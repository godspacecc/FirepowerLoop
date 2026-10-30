using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;

namespace FirepowerLoop.Accounts;

/// <summary>目标项目身份与凭据；账号和首个玩家映射同文档提交，不保存玩法状态。</summary>
public sealed class IdentityAccount
{
    [BsonId] public long AccountId { get; set; }
    public string NormalizedUserName { get; set; } = "";
    public string DisplayUserName { get; set; } = "";
    public int AlgorithmVersion { get; set; } = 1;
    public int Iterations { get; set; } = 600000;
    public string SaltBase64 { get; set; } = "";
    public string HashBase64 { get; set; } = "";
    public long PlayerId { get; set; }
    public string DisplayName { get; set; } = "";
    public long ProfileRevision { get; set; }
    public long SessionEpoch { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
    public string Status { get; set; } = "active";
    // 仅当前项目schema4迁入的旧凭据允许短密码登录，新注册永不设置此标记。
    public bool LegacyShortPassword { get; set; }
}

/// <summary>与基础版相同的新账号输入、PBKDF2参数；旧凭据兼容只按记录显式启用。</summary>
public static class AccountPolicy
{
    public static bool UserName(string? value) => value is { Length: >= 3 and <= 32 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    public static bool NewPassword(string? value) => value is { Length: >= 10 and <= 128 } && Encoding.UTF8.GetByteCount(value) <= 256;
    public static bool LegacyPassword(string? value) => value is { Length: <= 8 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    public static void SetPassword(IdentityAccount account, string password)
    {
        if (!NewPassword(password)) throw new ArgumentException("新密码必须为10–128字符且UTF8不超过256字节。");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        account.AlgorithmVersion = 1; account.Iterations = 600000;
        account.SaltBase64 = Convert.ToBase64String(salt);
        account.HashBase64 = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32));
        account.LegacyShortPassword = false;
    }
    public static bool Verify(IdentityAccount account, string password)
    {
        if (!NewPassword(password) && !(account.LegacyShortPassword && LegacyPassword(password))) return false;
        if (account.AlgorithmVersion != 1 || account.Iterations < 600000 || account.Iterations > 10000000) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(account.SaltBase64), expected = Convert.FromBase64String(account.HashBase64);
            return salt.Length == 16 && expected.Length == 32 && CryptographicOperations.FixedTimeEquals(expected,
                Rfc2898DeriveBytes.Pbkdf2(password, salt, account.Iterations, HashAlgorithmName.SHA256, 32));
        }
        catch (FormatException) { return false; }
    }
}
