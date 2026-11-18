using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataOperationAuthorizationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Outcome_reauthorizes_the_original_permission_even_after_the_article_is_gone()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(family, "AUTH-OUTCOME-" + Guid.NewGuid().ToString("N")));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var key = Guid.NewGuid(); var path = $"/api/v1/articles/{id:D}";
        Assert.Equal(HttpStatusCode.NoContent, (await api.WriteAsync(HttpMethod.Delete, path, new { }, key, created.Headers.ETag!.ToString())).StatusCode);
        using var reader = await MasterDataApi.LoginAsync(fixture, factory, "Operario");
        using var admin = await MasterDataApi.LoginAsync(fixture, factory, "Administrador"); using var anonymous = factory.CreateClient();
        var outcomePath = $"/api/v1/operations/{key:D}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(outcomePath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.Client.GetAsync(outcomePath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.GetAsync(outcomePath)).StatusCode);
        var result = await api.Client.GetFromJsonAsync<JsonElement>(outcomePath); Assert.Equal(204, result.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("body").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/api/v1/operations/{Guid.NewGuid():D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.WriteAsync(HttpMethod.Delete, path, new { }, key, "\"malformed\"")).StatusCode);
    }
}
