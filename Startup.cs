using Microsoft.Extensions.DependencyInjection;
using AzureRag.Services;
using System;
using System.Net.Http;

namespace AzureRag
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // Add services to the container.
            services.AddControllers();
            
            // AutoStructureService用の特別なHttpClientを設定
            services.AddHttpClient("AutoStructureClient", client => {
                // タイムアウトを設定
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // プロキシをバイパス
                UseProxy = false,
                // 証明書検証を無効化（必要に応じて - プライベートサブネットでの自己署名証明書対策）
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });
            
            // 通常のHttpClient
            services.AddHttpClient();
            
            services.AddSingleton<IFileStorageService, FileStorageService>();
            services.AddSingleton<IPdfProcessingService, PdfProcessingService>();
            services.AddSingleton<IAzureSearchService, AzureSearchService>();
            services.AddSingleton<IIndexManagementService, IndexManagementService>();
            services.AddSingleton<IDocumentChatService, DocumentChatService>();
            services.AddSingleton<IAutoStructureService, AutoStructureService>();
            services.AddSingleton<IWorkIdManagementService, WorkIdManagementService>();
            services.AddSingleton<Services.IAuthorizationService, Services.AuthorizationService>();
            services.AddSingleton<IDataIngestionService, DataIngestionService>();
        }
    }
} 