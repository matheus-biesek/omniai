using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using StackExchange.Redis;
using Webhook.Api;
using Webhook.Api.Endpoints;
using Webhook.Application.ReceberMetrica;
using Webhook.Domain;
using Webhook.Domain.Abstractions;
using Webhook.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddOpenApi();

builder.Services.AddDbContext<WriteDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WriteDatabase")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("PerIp", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:PermitLimit"),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:WindowSeconds")),
            }));
});

builder.Services.Configure<ApiKeyHashingOptions>(builder.Configuration.GetSection(ApiKeyHashingOptions.SectionName));
builder.Services.Configure<QueueBackpressureOptions>(builder.Configuration.GetSection(QueueBackpressureOptions.SectionName));

builder.Services.AddScoped<IApiKeyRepository, EfApiKeyRepository>();
builder.Services.AddScoped<IUsageEventPublisher, RedisUsageEventPublisher>();
builder.Services.AddScoped<ReceberMetricaUseCase>();
builder.Services.AddScoped<IValidator<ReceberMetricaRequest>, ReceberMetricaRequestValidator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.MapUsageEventsEndpoint();

app.Run();
