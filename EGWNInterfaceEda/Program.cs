using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Application.Services;
using EGWNInterfaceEda.Infrastructure.Hosting;
using EGWNInterfaceEda.Jobs;
using EGWNInterfaceEda.Infrastructure.Services;
using Prometheus;
using Quartz;
using Serilog;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AppQuartzOptions = EGWNInterfaceEda.Application.Options.QuartzOptions;

var builder = WebApplication.CreateBuilder(args);

var consulUrl = Environment.GetEnvironmentVariable("CONSUL_URL")
    ?? builder.Configuration["Consul:Address"];
var consulKvPath = Environment.GetEnvironmentVariable("CONSUL_KV_PATH")
    ?? builder.Configuration["Consul:Key"]
    ?? builder.Configuration["Consul:KvPath"]
    ?? "egwn-interface-eda/appsettings";

if (!string.IsNullOrWhiteSpace(consulUrl))
{
    builder.Configuration.AddConsulKv(consulUrl, consulKvPath);
    Console.WriteLine($"[Startup] Loading configuration from Consul: {consulUrl} → {consulKvPath}");
}
else
{
    Console.WriteLine("[Startup] No Consul URL configured – using appsettings only");
}
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
builder.Services.AddSingleton<IEdaTriggerPublisher, RabbitMqEdaResultPublisher>();
builder.Services.AddSingleton<IEdaReadingOrchestrator, EdaReadingOrchestrator>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IEdaMetrics, PrometheusEdaMetrics>();
builder.Services.AddHostedService<ConsulRegistrationHostedService>();
builder.Services.AddHostedService<QuartzScheduleMetricsHostedService>();

builder.Services.AddQuartz(q =>
{
    var meterJobKey = new JobKey(EdaTriggerMeterReadingJob.JobName, EdaTriggerMeterReadingJob.GroupName);
    q.AddJob<EdaTriggerMeterReadingJob>(opts => opts.WithIdentity(meterJobKey));
    q.AddTrigger(opts => opts.ForJob(meterJobKey).WithIdentity($"{EdaTriggerMeterReadingJob.JobName}.trigger", EdaTriggerMeterReadingJob.GroupName).StartNow().WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(quartzOptions.IntervalMinutes)).RepeatForever()));

    var kpiJobKey = new JobKey(EdaTriggerKpiReadingJob.JobName, EdaTriggerKpiReadingJob.GroupName);
    q.AddJob<EdaTriggerKpiReadingJob>(opts => opts.WithIdentity(kpiJobKey));
    q.AddTrigger(opts => opts.ForJob(kpiJobKey).WithIdentity($"{EdaTriggerKpiReadingJob.JobName}.trigger", EdaTriggerKpiReadingJob.GroupName).StartNow().WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(quartzOptions.IntervalMinutes)).RepeatForever()));
});

builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

builder.Host.UseSerilog((ctx, loggerConfiguration) =>
{
    loggerConfiguration.ReadFrom.Configuration(ctx.Configuration);
});

var app = builder.Build();

var rabbitOpts = app.Services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
app.Logger.LogInformation("RabbitMQ config: Host={Host}, Port={Port}, User={User}, VHost={VHost}",
    rabbitOpts.HostName, rabbitOpts.Port, rabbitOpts.UserName, rabbitOpts.VirtualHost);

app.UseSwagger();
app.UseSwaggerUI();
app.UseRouting();
app.UseHttpMetrics();
app.MapControllers();
app.MapMetrics();
await app.RunAsync();
