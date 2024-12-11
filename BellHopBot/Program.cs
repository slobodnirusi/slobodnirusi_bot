using BellHopBot.Bot;
using BellHopBot.Configuration;
using BellHopBot.Data;
using BellHopBot.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Web;
using Telegram.Bot;
using Telegram.Bot.Polling;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost
        .ConfigureAppConfiguration((hostingContext, config) =>
        {
            var env = hostingContext.HostingEnvironment;

            config.AddEnvironmentVariables();
            config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
        })
        .ConfigureServices((context, services) =>
        {
            services.Configure<BotConfiguration>(context.Configuration.GetSection(nameof(BotConfiguration)));

            services.AddHttpClient("telegram_bot_client")
                .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
                {
                    BotConfiguration? botConfig = sp.GetRequiredService<IOptionsSnapshot<BotConfiguration>>().Value;
                    TelegramBotClientOptions options = new(botConfig.Token);
                    return new TelegramBotClient(options, httpClient);
                });
            
            services.AddLocalization();

            services.AddScoped<IUpdateHandler, UpdateHandler>();
            services.AddScoped<IReceiver, Receiver>();
            services.AddHostedService<Polling>();
            services.AddScoped<LocalizationProvider>();

            services.AddDbContext<UsersDbContext>((sp, opt) =>
            {
                string connectionString = sp.GetRequiredService<IOptionsSnapshot<BotConfiguration>>().Value.Db.Connection;
                opt.UseSqlite(connectionString);
            });
        });
    
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();
    
    
    var app = builder.Build();

    app.MapGet("/healthcheck", async ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        await ctx.Response.WriteAsync("ok");
    });

    await InitBlackList(app);
    app.Run();
}
catch(Exception exception)
{
    logger.Error(exception, "Program exception");
}
finally
{
    LogManager.Shutdown();
}

async Task InitBlackList(WebApplication app)
{
    using var servicesScope = app.Services.CreateScope();
    var dbContext = servicesScope.ServiceProvider.GetRequiredService<UsersDbContext>();
    var logger = servicesScope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();

    try
    {
        logger.LogInformation($"Can connect: {dbContext.Database.CanConnect()}");

        var blockedUsers = await dbContext.BlockedUsers(CancellationToken.None);

        foreach (var user in blockedUsers)
        {
            UsersBlacklist.Blocked.Add(user.UserId);
        }
    }
    catch (Exception e)
    {
        logger.LogCritical(e.Message, e);
        throw;
    }
}