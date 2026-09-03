using App.Features.Streaming;

namespace App;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["*"];

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyHeader().AllowAnyMethod();

                if (allowedOrigins.Contains("*"))
                {
                    policy.SetIsOriginAllowed(origin => true).AllowCredentials();
                    return;
                }

                policy.WithOrigins(allowedOrigins).AllowCredentials();
            });
        });

        builder.Services
            .AddSignalR(options =>
            {
                options.EnableDetailedErrors = builder.Environment.IsDevelopment();
                options.MaximumReceiveMessageSize = 256 * 1024;
                options.MaximumParallelInvocationsPerClient = 4;
                options.StreamBufferCapacity = 10;
            });

        builder.Services.AddControllers();
        builder.Services
            .AddOptions<MediaMtxOptions>()
            .BindConfiguration(MediaMtxOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<RoomRegistry>();

        var app = builder.Build();

        app.UseCors();

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            service = "streaming-api"
        }));

        app.MapControllers();
        app.MapHub<StreamingHub>("/streamingHub");

        app.Run();
    }
}
