using System.Text;
using Consumer.Application.ProcessarEventoDeUso;
using Consumer.Application.RegistrarEvento;
using Consumer.Domain.Abstractions;
using Consumer.Hubs;
using Consumer.Infrastructure;
using Consumer.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shared.Data;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddOpenApi();

builder.Services.AddDbContext<WriteDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WriteDatabase")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddSignalR();

builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.SectionName));
builder.Services.Configure<RetryOptions>(builder.Configuration.GetSection(RetryOptions.SectionName));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));

builder.Services.AddSingleton<IUsageEventReader, RedisUsageEventReader>();
builder.Services.AddScoped<IUsageEventLogRepository, EfUsageEventLogRepository>();
builder.Services.AddScoped<IProjectLookup, EfProjectLookup>();
builder.Services.AddScoped<IUsageRecordRepository, EfUsageRecordRepository>();
builder.Services.AddScoped<IUsageNotifier, SignalRUsageNotifier>();

builder.Services.AddScoped<RegistrarEventoUseCase>();
builder.Services.AddScoped<ProcessarEventoDeUsoUseCase>();

builder.Services.AddHostedService<UsageEventConsumerWorker>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!)),
        };

        // SignalR manda o token via query string na conexão do WebSocket, não no header Authorization.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<UsageHub>("/hub").RequireAuthorization();

app.Run();
