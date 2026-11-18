using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataConcurrencyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Assignment_and_family_deactivation_serialize_both_commit_orders(bool assignmentFirst, bool reassign)
    {
        var barrier = new SaveBarrier();
        using var factory = new ApiFactory(fixture.Settings, "Development", services =>
            services.AddDbContext<InventoryDbContext>(options => options.AddInterceptors(barrier)));
        using var assigner = await MasterDataApi.LoginAsync(fixture, factory);
        using var deactivator = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await assigner.FamilyAsync(); var familyPath = $"/api/v1/families/{family:D}";
        var familyResponse = await deactivator.Client.GetAsync(familyPath);
        var familyData = await familyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sku = "RACE-" + Guid.NewGuid().ToString("N"); Guid? id = null; string? version = null; Guid? oldFamily = null;
        if (reassign)
        {
            oldFamily = await assigner.FamilyAsync();
            var existing = await assigner.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(oldFamily.Value, sku));
            id = (await existing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
            version = existing.Headers.ETag!.ToString();
        }
        barrier.Predicate = assignmentFirst ? entity => entity is Article article && article.Sku == sku.ToUpperInvariant()
            : entity => entity is Family value && value.Id == family;
        Task<HttpResponseMessage> Assign() => assigner.WriteAsync(reassign ? HttpMethod.Put : HttpMethod.Post,
            reassign ? $"/api/v1/articles/{id:D}" : "/api/v1/articles", MasterDataArticleTests.Body(family, sku), version: version);
        Task<HttpResponseMessage> Deactivate() => deactivator.WriteAsync(HttpMethod.Put, familyPath,
            new { code = familyData.GetProperty("code").GetString(), name = "Familia desactivada", isActive = false },
            version: familyResponse.Headers.ETag!.ToString());
        var first = assignmentFirst ? Assign() : Deactivate();
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = assignmentFirst ? Deactivate() : Assign();
        try
        {
            using var scope = fixture.BootstrapServices.CreateScope();
            var admin = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (await admin.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS Value FROM sys.dm_tran_locks
                WHERE resource_database_id = DB_ID() AND resource_type = 'APPLICATION' AND request_status = 'WAIT'
                """).SingleAsync(timeout.Token) == 0) await Task.Delay(20, timeout.Token);
        }
        finally { barrier.Release.TrySetResult(); }
        var results = await Task.WhenAll(first, second);
        var assigned = results[assignmentFirst ? 0 : 1];
        Assert.Equal(HttpStatusCode.OK, results[assignmentFirst ? 1 : 0].StatusCode);
        Assert.Equal(assignmentFirst ? reassign ? HttpStatusCode.OK : HttpStatusCode.Created : HttpStatusCode.UnprocessableEntity, assigned.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.False((await db.Set<Family>().SingleAsync(value => value.Id == family)).IsActive);
        var article = await db.Articles.SingleOrDefaultAsync(value => value.Sku == sku.ToUpperInvariant());
        if (assignmentFirst || reassign)
        {
            Assert.NotNull(article); Assert.True(article.IsActive);
            Assert.Equal(assignmentFirst ? family : oldFamily!.Value, article.FamilyId);
        }
        else Assert.Null(article);
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        public Func<object, bool>? Predicate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int entered;
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Predicate is { } match && eventData.Context!.ChangeTracker.Entries().Any(entry => match(entry.Entity))
                && Interlocked.CompareExchange(ref entered, 1, 0) == 0)
            {
                Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }
}
