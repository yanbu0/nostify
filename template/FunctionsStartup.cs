using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using nostify;

[assembly: FunctionsStartup(typeof(_ReplaceMe__Service.Startup))]

namespace _ReplaceMe__Service
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddHttpClient();
            var config = builder.GetContext().Configuration;

            //Note: This is the api key for the cosmos emulator by default
            string apiKey = config.GetValue<string>("apiKey");
            string dbName = config.GetValue<string>("dbName");
            string endPoint = config.GetValue<string>("endPoint");

            var nostify = new Nostify(apiKey,dbName,endPoint);

            //Creates persistedEvents container so Event Handlers can attach without throwing errors during testing, may be removed once persistedEvents container exists
            var _ = nostify.GetPersistedEventsContainerAsync().GetAwaiter().GetResult();

            builder.Services.AddSingleton<Nostify>((s) => {
                return nostify;
            });
        }
    }
}