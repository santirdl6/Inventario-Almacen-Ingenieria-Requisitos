using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataOpenApiTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Development_describes_implemented_routes_contract_headers_errors_and_exact_decimal_strings()
    {
        using var factory = fixture.Factory(); using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json"); var paths = document.GetProperty("paths");
        foreach (var catalogue in new[] { "families", "suppliers", "articles" })
        {
            var post = paths.GetProperty("/api/v1/" + catalogue).GetProperty("post");
            Assert.Contains(post.GetProperty("parameters").EnumerateArray(), value => value.GetProperty("name").GetString() == "Idempotency-Key");
            Assert.True(post.GetProperty("responses").GetProperty("201").GetProperty("headers").TryGetProperty("ETag", out _));
            Assert.True(post.GetProperty("responses").GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _));
            var detail = paths.GetProperty($"/api/v1/{catalogue}/{{id}}");
            var put = detail.GetProperty("put");
            Assert.Contains(put.GetProperty("parameters").EnumerateArray(), value => value.GetProperty("name").GetString() == "If-Match");
            Assert.True(put.GetProperty("responses").TryGetProperty("412", out _)); Assert.True(put.GetProperty("responses").TryGetProperty("428", out _));
            if (catalogue != "articles") Assert.False(detail.TryGetProperty("delete", out _));
        }
        Assert.True(paths.GetProperty("/api/v1/articles/{id}").TryGetProperty("delete", out _));
        Assert.True(paths.GetProperty("/api/v1/articles/{id}/movements").GetProperty("get").GetProperty("responses").TryGetProperty("503", out _));
        Assert.False(paths.TryGetProperty("/api/v1/maintenance/hu08/family-map", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.Equal("string", schemas.GetProperty("ArticleMovementDto").GetProperty("properties").GetProperty("quantity").GetProperty("type").GetString());
        Assert.Equal("string", schemas.GetProperty("ArticleContextDto").GetProperty("properties").GetProperty("minimumAvailable").GetProperty("type").GetString());
    }
}
