using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Application.Services;
using EGWNInterfaceEda.Infrastructure.Hosting;
using EGWNInterfaceEda.Jobs;
using EGWNInterfaceEda.Infrastructure.Services;
using Quartz;
using Serilog;
using Microsoft.Extensions.Options;
using AppQuartzOptions = EGWNInterfaceEda.Application.Options.QuartzOptions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<CentralApiOptions>()
    .BindConfiguration(CentralApiOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "CentralApi:BaseUrl is required");

builder.Services.AddOptions<EdaOptions>()
    .BindConfiguration(EdaOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "Eda:BaseUrl is required")
    .Validate(options => !string.IsNullOrWhiteSpace(options.LoginUrl), "Eda:LoginUrl is required")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConsumptionSuryaBaseUrl), "Eda:ConsumptionSuryaBaseUrl is required")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Username), "Eda:Username is required")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "Eda:Password is required")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CommunityId), "Eda:CommunityId is required");

builder.Services.AddOptions<RabbitMqOptions>()
    .BindConfiguration(RabbitMqOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.HostName), "RabbitMq:HostName is required");

builder.Services.AddOptions<SeqOptions>()
    .BindConfiguration(SeqOptions.SectionName);

builder.Services.AddOptions<ConsulOptions>()
    .BindConfiguration(ConsulOptions.SectionName);

builder.Services.AddOptions<AppQuartzOptions>()
    .BindConfiguration(AppQuartzOptions.SectionName)
    .Validate(options => options.IntervalMinutes > 0, "Quartz:IntervalMinutes must be greater than zero");

var quartzOptions = builder.Configuration.GetSection(AppQuartzOptions.SectionName).Get<AppQuartzOptions>() ?? new AppQuartzOptions();

builder.Services.AddHttpClient<ICentralApiClient, CentralApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<CentralApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddHttpClient<IEdaPortalClient, EdaPortalClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<EdaOptions>>().Value;
    client.BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddSingleton<IEdaResultPublisher, RabbitMqEdaResultPublisher>();
builder.Services.AddSingleton<IEdaSyncOrchestrator, EdaSyncOrchestrator>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHostedService<ConsulRegistrationHostedService>();

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey(EdaSyncJob.JobName, EdaSyncJob.GroupName);
    q.AddJob<EdaSyncJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity($"{EdaSyncJob.JobName}.trigger", EdaSyncJob.GroupName)
        .StartNow()
        .WithSimpleSchedule(schedule => schedule
            .WithInterval(TimeSpan.FromMinutes(quartzOptions.IntervalMinutes))
            .RepeatForever()));
});

builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var seqOptions = configuration.GetSection(SeqOptions.SectionName).Get<SeqOptions>() ?? new SeqOptions();
    loggerConfiguration
        .ReadFrom.Configuration(configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    if (!string.IsNullOrWhiteSpace(seqOptions.Url))
    {
        loggerConfiguration.WriteTo.Seq(seqOptions.Url, apiKey: seqOptions.ApiKey);
    }
});

var app = builder.Build();
await app.RunAsync();
