using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace AzureRag.Services.UserDirectory
{
    public sealed class ResolvedExternalCredential
    {
        public string ApiUser { get; set; } = string.Empty;
        public string ApiPass { get; set; } = string.Empty;
        public string S3Prefix { get; set; } = string.Empty;
    }

    public interface IExternalCredentialResolver
    {
        Task<ResolvedExternalCredential?> ResolveAsync(string appUserName);
    }

    public sealed class ExternalCredentialResolver : IExternalCredentialResolver
    {
        private readonly IUserDirectory _userDirectory;
        private readonly string _prefixTemplate;

        public ExternalCredentialResolver(IUserDirectory userDirectory, IConfiguration configuration)
        {
            _userDirectory = userDirectory;
            _prefixTemplate = configuration["Storage:DefaultUserPrefixTemplate"]
                ?? configuration["Storage__DefaultUserPrefixTemplate"]
                ?? "analyze-structure/individual/{name}";
        }

        public async Task<ResolvedExternalCredential?> ResolveAsync(string appUserName)
        {
            var rec = await _userDirectory.GetUserRecordAsync(appUserName);
            if (rec == null) return null;
            var prefix = _prefixTemplate.Replace("{name}", rec.Name);
            return new ResolvedExternalCredential
            {
                ApiUser = rec.Name,
                ApiPass = rec.Password,
                S3Prefix = prefix
            };
        }
    }
}


