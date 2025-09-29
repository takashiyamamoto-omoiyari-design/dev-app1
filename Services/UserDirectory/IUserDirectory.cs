using System;
using System.Threading.Tasks;

namespace AzureRag.Services.UserDirectory
{
    public sealed class UserDirectoryRecord
    {
        public string Name { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        // 任意フィールド（将来拡張用）
        public string Trial { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public interface IUserDirectory
    {
        Task<bool> TryValidateUserAsync(string name, string password);
        Task<UserDirectoryRecord?> GetUserRecordAsync(string name);
        string? GetLastCsvETag();
    }
}


