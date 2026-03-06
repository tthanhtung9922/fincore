using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace FinCore.Observability.Logging;

public static class SerilogConfiguration
{
    public static IHostBuilder AddFinCoreLogging(this IHostBuilder host, string serviceName)
    {
        host.UseSerilog((context, services, config) =>
        {
            var seqUrl = Environment.GetEnvironmentVariable("OBSERVABILITY__SEQ_URL") ?? "http://localhost:5341";

            config
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("ServiceName", serviceName)
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
                .WriteTo.Seq(seqUrl);
        });

        return host;
    }
}
