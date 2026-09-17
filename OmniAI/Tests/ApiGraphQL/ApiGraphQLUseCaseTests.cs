using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ApiGraphQL.Application.CreateApiKey;
using ApiGraphQL.Application.CreateProject;
using ApiGraphQL.Application.Login;
using ApiGraphQL.Application.RevokeApiKey;
using ApiGraphQL.Domain.Abstractions;
using ApiGraphQL.Domain.Exceptions;
using ApiGraphQL.Infrastructure;
using ApiGraphQL.Types;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shared.Entities;
using Shared.Security;

namespace Tests.ApiGraphQL;

internal sealed class InMemoryStore : IProjectRepository, IApiKeyRepository, IUnitOfWork
{
    public List<Project> Projects { get; } = new();
    public List<ApiKey> ApiKeys { get; } = new();
    private readonly List<Project> _pendingProjects = new();
    private readonly List<ApiKey> _pendingKeys = new();
    public int SaveCalls { get; private set; }
    public List<Guid> RevokeCalls { get; } = new();

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Projects.Any(p => p.Name == name));

    public void Add(Project project) => _pendingProjects.Add(project);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Projects.FirstOrDefault(p => p.Id == id));

    public void Add(ApiKey apiKey) => _pendingKeys.Add(apiKey);

    Task<ApiKey?> IApiKeyRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(ApiKeys.FirstOrDefault(k => k.Id == id));

    public Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        RevokeCalls.Add(id);
        ApiKeys.First(k => k.Id == id).RevokedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCalls++;
        Projects.AddRange(_pendingProjects);
        ApiKeys.AddRange(_pendingKeys);
        _pendingProjects.Clear();
        _pendingKeys.Clear();
        return Task.CompletedTask;
    }
}

public class ApiGraphQLUseCaseTests
{
    private const string Pepper = "pepper-teste";
    private const string SigningKey = "dev-only-jwt-signing-key-change-me-please-32chars";

    private readonly InMemoryStore _store = new();
    private readonly IOptions<ApiKeyHashingOptions> _hashing = Options.Create(new ApiKeyHashingOptions { PepperSecret = Pepper });

    private static JwtTokenGenerator Generator(int expirationMinutes = 60) => new(Options.Create(new JwtOptions
    {
        Issuer = "OmniAI",
        Audience = "OmniAI.Dashboard",
        SigningKey = SigningKey,
        ExpirationMinutes = expirationMinutes,
    }));

    // Mesmos parametros que ApiGraphQL/Program.cs e Consumer/Program.cs usam para validar.
    private static TokenValidationParameters ValidationParameters(string signingKey = SigningKey) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "OmniAI",
        ValidAudience = "OmniAI.Dashboard",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
    };

    private sealed class FakeLoginAttemptLimiter : ILoginAttemptLimiter
    {
        public bool Allow { get; set; } = true;
        public List<string> Keys { get; } = new();

        public bool TryAcquire(string clientKey)
        {
            Keys.Add(clientKey);
            return Allow;
        }
    }

    private readonly FakeLoginAttemptLimiter _limiter = new();

    private LoginUseCase Login() => new(
        Options.Create(new DashboardCredentialsOptions { Username = "admin", Password = "admin-dev-password" }),
        Generator(),
        _limiter);

    [Fact]
    public void Login_CredenciaisCorretas_EmiteJwtValidoParaApiEConsumer()
    {
        var token = Login().Executar("admin", "admin-dev-password", "10.0.0.1");

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, ValidationParameters(), out var validated);

        Assert.NotNull(principal);
        var jwt = (JwtSecurityToken)validated;
        Assert.Equal("admin", jwt.Subject);
        Assert.InRange(jwt.ValidTo, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    }

    [Theory]
    [InlineData("admin", "senha-errada")]
    [InlineData("root", "admin-dev-password")]
    [InlineData("", "")]
    [InlineData("ADMIN", "admin-dev-password")]
    [InlineData("admin", "admin-dev-password ")]
    [InlineData("admin", "admin-dev-passwor")]
    [InlineData("admin", "admin-dev-password-e-mais-um-pouco")]
    public void Login_CredenciaisErradas_LancaMesmaMensagemGenerica(string user, string pass)
    {
        var ex = Assert.Throws<InvalidCredentialsException>(() => Login().Executar(user, pass, "10.0.0.1"));

        Assert.Equal("Usuário ou senha inválidos.", ex.Message);
    }

    [Fact]
    public void Login_ConsomeUmaTentativaPorChamada_ComAChaveDoCliente()
    {
        Login().Executar("admin", "admin-dev-password", "10.0.0.1");
        Assert.Throws<InvalidCredentialsException>(() => Login().Executar("admin", "errada", "10.0.0.2"));

        Assert.Equal(new[] { "10.0.0.1", "10.0.0.2" }, _limiter.Keys);
    }

    [Fact]
    public void Login_LimiteAtingido_BloqueiaMesmoComSenhaCerta()
    {
        _limiter.Allow = false;

        var ex = Assert.Throws<TooManyLoginAttemptsException>(() => Login().Executar("admin", "admin-dev-password", "10.0.0.1"));

        Assert.Equal("Muitas tentativas de login. Aguarde um minuto e tente novamente.", ex.Message);
    }

    [Fact]
    public void LimitadorDeLogin_PermiteAteOLimite_DepoisBloqueia_PorCliente()
    {
        using var limiter = new FixedWindowLoginAttemptLimiter(Options.Create(new LoginRateLimitOptions { PermitLimit = 3, WindowSeconds = 60 }));

        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("a"));
        Assert.False(limiter.TryAcquire("a"));
        Assert.False(limiter.TryAcquire("a"));

        Assert.True(limiter.TryAcquire("b"));
    }

    [Fact]
    public async Task LimitadorDeLogin_LiberaDeNovoQuandoAJanelaVira()
    {
        using var limiter = new FixedWindowLoginAttemptLimiter(Options.Create(new LoginRateLimitOptions { PermitLimit = 1, WindowSeconds = 1 }));

        Assert.True(limiter.TryAcquire("a"));
        Assert.False(limiter.TryAcquire("a"));

        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        Assert.True(limiter.TryAcquire("a"));
    }

    [Fact]
    public void Jwt_AssinadoComOutraChave_EhRejeitado()
    {
        var token = Generator().GenerateToken("admin");

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, ValidationParameters("outra-chave-com-pelo-menos-32-caracteres!!"), out _));
    }

    [Fact]
    public void Jwt_Expirado_EhRejeitado()
    {
        var token = Generator(expirationMinutes: -10).GenerateToken("admin");

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, ValidationParameters(), out _));
    }

    [Fact]
    public void RequireAuthenticated_SemIdentidade_Lanca()
    {
        var ex = Assert.Throws<UnauthorizedAccessException>(() => new ClaimsPrincipal(new ClaimsIdentity()).RequireAuthenticated());
        Assert.Equal("Autenticação necessária.", ex.Message);
    }

    [Fact]
    public void RequireAuthenticated_Autenticado_NaoLanca()
    {
        new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "admin") }, "Bearer")).RequireAuthenticated();
    }

    [Fact]
    public async Task CreateProject_CriaProjetoEChave_NumUnicoSave_EArmazenaSoOHash()
    {
        var useCase = new CreateProjectUseCase(_store, _store, _store, _hashing);

        var result = await useCase.ExecutarAsync("checkout-service", CancellationToken.None);

        Assert.Equal(1, _store.SaveCalls);
        var project = Assert.Single(_store.Projects);
        var key = Assert.Single(_store.ApiKeys);
        Assert.Equal("checkout-service", project.Name);
        Assert.Equal(project.Id, result.ProjectId);
        Assert.Equal(project.Id, key.ProjectId);
        Assert.Null(key.RevokedAt);
        Assert.StartsWith("ombk_", result.ApiKey);
        Assert.Equal(ApiKeyHasher.Hash(result.ApiKey, Pepper), key.KeyHash);
        Assert.DoesNotContain(result.ApiKey, key.KeyHash);
    }

    [Fact]
    public async Task CreateProject_NomeDuplicado_LancaErroClaro_ENaoGravaNada()
    {
        var useCase = new CreateProjectUseCase(_store, _store, _store, _hashing);
        await useCase.ExecutarAsync("checkout-service", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ProjectNameAlreadyExistsException>(() =>
            useCase.ExecutarAsync("checkout-service", CancellationToken.None));

        Assert.Equal("Já existe um projeto chamado 'checkout-service'.", ex.Message);
        Assert.Single(_store.Projects);
        Assert.Single(_store.ApiKeys);
        Assert.Equal(1, _store.SaveCalls);
    }

    [Fact]
    public async Task CreateApiKey_ProjetoExistente_GeraChaveNovaDiferente()
    {
        var created = await new CreateProjectUseCase(_store, _store, _store, _hashing).ExecutarAsync("p", CancellationToken.None);

        var result = await new CreateApiKeyUseCase(_store, _store, _store, _hashing).ExecutarAsync(created.ProjectId, CancellationToken.None);

        Assert.Equal(2, _store.ApiKeys.Count);
        Assert.NotEqual(created.ApiKey, result.ApiKey);
        var nova = _store.ApiKeys.Single(k => k.Id == result.ApiKeyId);
        Assert.Equal(ApiKeyHasher.Hash(result.ApiKey, Pepper), nova.KeyHash);
        Assert.Equal(created.ProjectId, nova.ProjectId);
    }

    [Fact]
    public async Task CreateApiKey_ProjetoInexistente_LancaErroClaro()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<ProjectNotFoundException>(() =>
            new CreateApiKeyUseCase(_store, _store, _store, _hashing).ExecutarAsync(id, CancellationToken.None));

        Assert.Contains(id.ToString(), ex.Message);
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public async Task RevokeApiKey_ChaveAtiva_Revoga()
    {
        var key = new ApiKey { Id = Guid.NewGuid() };
        _store.ApiKeys.Add(key);

        await new RevokeApiKeyUseCase(_store).ExecutarAsync(key.Id, CancellationToken.None);

        Assert.Equal(new[] { key.Id }, _store.RevokeCalls);
        Assert.NotNull(key.RevokedAt);
    }

    [Fact]
    public async Task RevokeApiKey_JaRevogada_EhIdempotente_PreservaDataOriginal()
    {
        var original = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var key = new ApiKey { Id = Guid.NewGuid(), RevokedAt = original };
        _store.ApiKeys.Add(key);

        await new RevokeApiKeyUseCase(_store).ExecutarAsync(key.Id, CancellationToken.None);

        Assert.Empty(_store.RevokeCalls);
        Assert.Equal(original, key.RevokedAt);
    }

    [Fact]
    public async Task RevokeApiKey_Inexistente_LancaErroClaro()
    {
        await Assert.ThrowsAsync<ApiKeyNotFoundException>(() =>
            new RevokeApiKeyUseCase(_store).ExecutarAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
