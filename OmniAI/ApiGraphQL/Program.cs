using System.Text;
using ApiGraphQL.Application.Login;
using ApiGraphQL.Application.UsageStatistics;
using ApiGraphQL.Domain.Abstractions;
using ApiGraphQL.Infrastructure;
using ApiGraphQL.Types;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shared.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<ReadDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ReadDatabase")));

builder.Services.Configure<DashboardCredentialsOptions>(builder.Configuration.GetSection(DashboardCredentialsOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IUsageStatisticsRepository, EfUsageStatisticsRepository>();

builder.Services.AddScoped<LoginUseCase>();
builder.Services.AddScoped<GetUsageStatisticsUseCase>();

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
    });

builder.Services.AddAuthorization();

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddErrorFilter(error =>
    {
        // So mensagens de excecoes explicitamente conhecidas como seguras chegam ao cliente -
        // qualquer outra excecao continua com a mensagem generica padrao do HotChocolate, para
        // nao vazar detalhe interno de implementacao.
        if (error.Exception is InvalidCredentialsException or UnauthorizedAccessException)
        {
            return error.WithMessage(error.Exception.Message);
        }

        return error;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapGraphQL();

app.Run();
