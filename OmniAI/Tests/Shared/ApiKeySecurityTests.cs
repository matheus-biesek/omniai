using Shared.Security;

namespace Tests.Shared;

public class ApiKeySecurityTests
{
    [Fact]
    public void Generate_ComecaComPrefixoOmbk()
    {
        var key = ApiKeyGenerator.Generate();

        Assert.StartsWith("ombk_", key);
    }

    [Fact]
    public void Generate_NaoContemCaracteresProblematicosDeBase64()
    {
        for (var i = 0; i < 200; i++)
        {
            var key = ApiKeyGenerator.Generate();
            Assert.DoesNotContain("+", key);
            Assert.DoesNotContain("/", key);
            Assert.DoesNotContain("=", key);
        }
    }

    [Fact]
    public void Generate_ProduzChavesUnicasComEntropiaSuficiente()
    {
        var keys = Enumerable.Range(0, 1000).Select(_ => ApiKeyGenerator.Generate()).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        // 32 bytes em base64 = 44 chars; removendo "=", "+" e "/" sobram pelo menos ~35 na pratica.
        Assert.All(keys, k => Assert.True(k.Length >= "ombk_".Length + 30, $"Chave curta demais: {k}"));
    }

    [Fact]
    public void Hash_EhDeterministicoParaMesmaChaveEPepper()
    {
        Assert.Equal(ApiKeyHasher.Hash("ombk_abc", "pepper"), ApiKeyHasher.Hash("ombk_abc", "pepper"));
    }

    [Fact]
    public void Hash_MudaQuandoPepperMuda()
    {
        Assert.NotEqual(ApiKeyHasher.Hash("ombk_abc", "pepper-1"), ApiKeyHasher.Hash("ombk_abc", "pepper-2"));
    }

    [Fact]
    public void Hash_MudaQuandoChaveMuda()
    {
        Assert.NotEqual(ApiKeyHasher.Hash("ombk_abc", "pepper"), ApiKeyHasher.Hash("ombk_abd", "pepper"));
    }

    [Fact]
    public void Hash_EhHmacSha256EmHexMinusculo()
    {
        var hash = ApiKeyHasher.Hash("ombk_abc", "pepper");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain("ombk_abc", hash);
    }

    [Fact]
    public void Hash_BateComVetorConhecidoDeHmacSha256()
    {
        // Vetor de teste RFC 4231 (caso 2): key "Jefe", data "what do ya want for nothing?".
        Assert.Equal(
            "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
            ApiKeyHasher.Hash("what do ya want for nothing?", "Jefe"));
    }
}
