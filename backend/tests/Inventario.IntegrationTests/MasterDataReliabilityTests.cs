using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataReliabilityTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static async Task<object> BodyAsync(string catalogue, MasterDataApi api, string code) => catalogue switch
    {
        "articles" => MasterDataArticleTests.Body(await api.FamilyAsync(), code),
        "suppliers" => new { code, name = "Maestro fiable", email = (string?)null, taxIdentifier = (string?)null, isActive = true },
        _ => new { code, name = "Maestro fiable", isActive = true }
    };

    [Theory]
    [InlineData("families")]
    [InlineData("articles")]
    [InlineData("suppliers")]
    public async Task A_lost_response_is_recovered_without_duplicates_and_conflicting_intentions_do_not_replay(string catalogue)
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var code = "RELIABLE-" + Guid.NewGuid().ToString("N"); var body = await BodyAsync(catalogue, api, code);
        var path = "/api/v1/" + catalogue; var key = Guid.NewGuid();
        var created = await api.WriteAsync(HttpMethod.Post, path, body, key); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = result.GetProperty("resource").GetProperty("id").GetGuid();
        // Se descarta la respuesta de alta y se recupera la evidencia confirmada por su intención.
        var outcome = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/operations/{key:D}");
        Assert.Equal(201, outcome.GetProperty("status").GetInt32());
        Assert.Equal(id, outcome.GetProperty("body").GetProperty("resource").GetProperty("id").GetGuid());
        var replay = await api.WriteAsync(HttpMethod.Post, path, body, key);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var other = await MasterDataApi.LoginAsync(fixture, factory);
        Assert.Equal(HttpStatusCode.Conflict, (await other.WriteAsync(HttpMethod.Post, path, body, key)).StatusCode);
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(body))!;
        fields["name"] = JsonSerializer.SerializeToElement("Otra intención");
        Assert.Equal(HttpStatusCode.Conflict, (await api.WriteAsync(HttpMethod.Post, path, fields, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.WriteAsync(HttpMethod.Put, $"{path}/{id:D}", body, key)).StatusCode);
        var editKey = Guid.NewGuid();
        var edited = await api.WriteAsync(HttpMethod.Put, $"{path}/{id:D}", fields, editKey, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var repeatEdit = await api.WriteAsync(HttpMethod.Put, $"{path}/{id:D}", fields, editKey, created.Headers.ETag.ToString());
        Assert.Equal(await edited.Content.ReadAsStringAsync(), await repeatEdit.Content.ReadAsStringAsync());
        Assert.Equal(edited.Headers.ETag, repeatEdit.Headers.ETag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await api.WriteAsync(HttpMethod.Put, $"{path}/{id:D}", body, version: created.Headers.ETag.ToString())).StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(2, await db.Set<MasterDataAudit>().CountAsync(value => value.EntityId == id));
        Assert.Equal(2, await db.OperationResults.CountAsync(value => value.SourceDocumentId == id));
        var list = await api.Client.GetFromJsonAsync<JsonElement>($"{path}?q={code}&state=all");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("families")]
    [InlineData("articles")]
    [InlineData("suppliers")]
    public async Task Failure_before_commit_never_leaves_a_master_audit_or_success_result(string catalogue)
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var code = "FAIL-" + Guid.NewGuid().ToString("N"); var body = await BodyAsync(catalogue, api, code); var key = Guid.NewGuid();
        using var scope = fixture.BootstrapServices.CreateScope(); var admin = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await admin.Database.ExecuteSqlRawAsync("CREATE TRIGGER dbo.__ReliabilityFailure ON dbo.MasterDataAudit AFTER INSERT AS THROW 51001, N'Fallo inducido', 1;");
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await api.WriteAsync(HttpMethod.Post, "/api/v1/" + catalogue, body, key)).StatusCode); }
        finally { await admin.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.__ReliabilityFailure"); }
        await using var db = fixture.ApplicationDatabase();
        Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == key));
        Assert.False(await db.Set<MasterDataAudit>().AnyAsync(value => value.OperationId == key));
        var list = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/{catalogue}?q={code}&state=all");
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Created, (await api.WriteAsync(HttpMethod.Post, "/api/v1/" + catalogue, body, key)).StatusCode);
    }
}
