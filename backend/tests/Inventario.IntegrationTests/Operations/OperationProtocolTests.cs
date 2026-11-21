using System.Security.Claims;
using System.Collections.Concurrent;
using System.Text.Json;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Operations;

public sealed class OperationProtocolTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private async Task<ClaimsPrincipal> PrincipalAsync(string role = "GestorMaestros")
    {
        var email = await fixture.CreateUserAsync(role);
        using var scope = fixture.BootstrapServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>()
            .CreateUserPrincipalAsync((await users.FindByEmailAsync(email))!);
        return await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(principal);
    }

    private static async Task<OperationResponse> ExecuteAsync(IServiceProvider services, ClaimsPrincipal principal,
        OperationRequest request, Func<OperationContext, CancellationToken, Task<OperationResponse>> action)
    {
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = principal };
        using var requestScope = services.CreateScope();
        try
        {
            return await requestScope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>().ExecuteAsync(request, action);
        }
        finally { accessor.HttpContext = null; }
    }

    private static OperationRequest Request(Guid key, Guid probe, string value = "original") =>
        new(key, "POST", "/api/v1/protocol-probes", "MasterData.Manage",
            JsonSerializer.SerializeToElement(new { probe, value }));

    private async Task<Guid> ProbeAsync()
    {
        var id = Guid.NewGuid();
        using var scope = fixture.BootstrapServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.__OperationProbe') IS NULL
                CREATE TABLE dbo.__OperationProbe (Id uniqueidentifier NOT NULL PRIMARY KEY, Value int NOT NULL, Version rowversion NOT NULL);
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.__OperationProbe (Id, Value) VALUES ({id}, 0)");
        return id;
    }

    private static async Task<byte[]> IncrementAsync(OperationContext context, Guid id, CancellationToken cancellation)
    {
        await using var command = context.Database.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "UPDATE dbo.__OperationProbe SET Value = Value + 1 OUTPUT INSERTED.Version WHERE Id = @id";
        command.Parameters.Add(new SqlParameter("@id", id));
        return (byte[])(await command.ExecuteScalarAsync(cancellation))!;
    }

    private async Task<int> ValueAsync(Guid id)
    {
        await using var db = fixture.ApplicationDatabase();
        return await db.Database.SqlQuery<int>($"SELECT Value FROM dbo.__OperationProbe WHERE Id = {id}").SingleAsync();
    }

    [Fact]
    public async Task Replay_preserves_body_headers_and_one_effect_even_after_a_lost_response()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var probe = await ProbeAsync();
        var request = Request(Guid.NewGuid(), probe);
        var calls = 0;
        async Task<OperationResponse> Change(OperationContext context, CancellationToken token)
        {
            calls++;
            await context.Locks.DocumentsAsync([new DocumentLock("probe", probe)], token);
            var version = await IncrementAsync(context, probe, token);
            return OperationResponse.Json(201, new { operationId = context.OperationId, probe }, probe,
                new Dictionary<string, string> { ["ETag"] = EntityTags.Format(version), ["Location"] = $"/api/v1/protocol-probes/{probe:D}" });
        }

        var first = await ExecuteAsync(factory.Services, principal, request, Change);
        var replay = await ExecuteAsync(factory.Services, principal, request,
            (_, _) => throw new InvalidOperationException("Un replay no ejecuta la mutación."));
        Assert.Equal(1, calls);
        Assert.Equal(1, await ValueAsync(probe));
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(first.Body, replay.Body);
        Assert.Equal(first.Headers["ETag"], replay.Headers["ETag"]);
        Assert.Equal(first.Headers["Location"], replay.Headers["Location"]);
        await using var check = fixture.ApplicationDatabase();
        Assert.Single(await check.OperationResults.Where(value => value.OperationId == request.OperationId).ToListAsync());
    }

    [Fact]
    public async Task A_failure_after_writing_rolls_back_effect_and_result()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var probe = await ProbeAsync();
        var request = Request(Guid.NewGuid(), probe);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(factory.Services, principal, request,
            async (context, token) =>
            {
                await IncrementAsync(context, probe, token);
                await context.Database.SaveChangesAsync(token);
                throw new InvalidOperationException("Fallo inducido antes del commit.");
            }));
        Assert.Equal(0, await ValueAsync(probe));
        await using var check = fixture.ApplicationDatabase();
        Assert.False(await check.OperationResults.AnyAsync(value => value.OperationId == request.OperationId));
        await ExecuteAsync(factory.Services, principal, request, async (context, token) =>
        {
            await IncrementAsync(context, probe, token);
            return OperationResponse.Json(200, new { ok = true });
        });
        Assert.Equal(1, await ValueAsync(probe));
    }

    [Fact]
    public async Task Content_or_actor_conflicts_and_revoked_permission_do_not_replay_a_result()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var probe = await ProbeAsync();
        var request = Request(Guid.NewGuid(), probe);
        await ExecuteAsync(factory.Services, principal, request,
            (_, _) => Task.FromResult(OperationResponse.Json(200, new { ok = true })));
        var conflict = await Assert.ThrowsAsync<InventoryProblemException>(() => ExecuteAsync(factory.Services,
            principal, Request(request.OperationId, probe, "different"), (_, _) => throw new Exception()));
        Assert.Equal("IdempotencyConflict", conflict.Code);
        var other = await PrincipalAsync();
        conflict = await Assert.ThrowsAsync<InventoryProblemException>(() => ExecuteAsync(factory.Services,
            other, request, (_, _) => throw new Exception()));
        Assert.Equal("IdempotencyConflict", conflict.Code);
        var reader = await PrincipalAsync("Operario");
        var denied = await Assert.ThrowsAsync<InventoryProblemException>(() => ExecuteAsync(factory.Services,
            reader, request, (_, _) => throw new Exception()));
        Assert.Equal(403, denied.Status);
    }

    [Fact]
    public async Task Simultaneous_same_intent_waits_for_the_sql_lock_and_changes_once()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var probe = await ProbeAsync();
        var request = Request(Guid.NewGuid(), probe);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<OperationResponse> Change(OperationContext context, CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            await IncrementAsync(context, probe, token);
            return OperationResponse.Json(200, new { ok = true });
        }
        var first = Task.Run(() => ExecuteAsync(factory.Services, principal, request, Change));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Task.Run(() => ExecuteAsync(factory.Services, principal, request, Change));
        try
        {
            using var scope = fixture.BootstrapServices.CreateScope();
            var admin = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (await admin.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS Value FROM sys.dm_tran_locks
                WHERE resource_database_id = DB_ID() AND resource_type = 'APPLICATION' AND request_status = 'WAIT'
                """).SingleAsync(timeout.Token) == 0)
                await Task.Delay(20, timeout.Token);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second);
        Assert.Equal(1, calls);
        Assert.Equal(1, await ValueAsync(probe));
    }

    [Fact]
    public async Task Reversed_lock_phases_fail_and_roll_back()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var probe = await ProbeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(factory.Services, principal,
            Request(Guid.NewGuid(), probe), async (context, token) =>
            {
                await context.Locks.ArticlesAsync([probe], token);
                await IncrementAsync(context, probe, token);
                await context.Locks.DocumentsAsync([new DocumentLock("probe", probe)], token);
                return OperationResponse.Json(200, new { ok = true });
            }));
        Assert.Equal(0, await ValueAsync(probe));
    }

    [Fact]
    public async Task A_timeout_in_the_middle_of_a_phase_rolls_back_and_does_not_acquire_later_resources()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync(); var probe = await ProbeAsync();
        var resources = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = ExecuteAsync(factory.Services, principal, Request(Guid.NewGuid(), probe, "blocker"), async (context, token) =>
        {
            await context.Locks.ArticlesAsync([resources[1]], token);
            entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            return OperationResponse.Json(200, new { ok = true });
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var request = Request(Guid.NewGuid(), probe, "timeout");
            var waiting = ExecuteAsync(factory.Services, principal, request, async (context, token) =>
            {
                await IncrementAsync(context, probe, token);
                await context.Locks.ArticlesAsync(resources.Reverse().Concat(resources), token);
                throw new InvalidOperationException("No debe atravesar el recurso bloqueado.");
            });
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
            // A later resource remains available while the middle one is blocked.
            await ExecuteAsync(factory.Services, principal, Request(Guid.NewGuid(), probe, "later"), async (context, token) =>
            {
                await context.Locks.ArticlesAsync([resources[2]], token);
                return OperationResponse.Json(200, new { ok = true });
            });
            var error = await Assert.ThrowsAsync<InventoryProblemException>(() => waiting);
            Assert.Equal("LockTimeout", error.Code); Assert.Equal(503, error.Status);
            Assert.Equal(0, await ValueAsync(probe));
            await using var db = fixture.ApplicationDatabase();
            Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == request.OperationId));
            // Rollback releases the first resource even though the independent blocker is still alive.
            await ExecuteAsync(factory.Services, principal, Request(Guid.NewGuid(), probe, "released"), async (context, token) =>
            {
                await context.Locks.ArticlesAsync([resources[0]], token);
                return OperationResponse.Json(200, new { ok = true });
            });
        }
        finally { release.TrySetResult(); await blocker; }
    }

    [Fact]
    public async Task A_real_sql_deadlock_retries_the_whole_transaction_in_a_fresh_context()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var left = await ProbeAsync();
        var right = await ProbeAsync();
        var contexts = new ConcurrentBag<Guid>();
        var bothUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        var attempts = new int[2];
        Task<OperationResponse> Run(int side, Guid first, Guid second) => Task.Run(() =>
            ExecuteAsync(factory.Services, principal, Request(Guid.NewGuid(), first), async (context, token) =>
            {
                contexts.Add(context.Database.ContextId.InstanceId);
                var attempt = Interlocked.Increment(ref attempts[side]);
                await IncrementAsync(context, first, token);
                if (attempt == 1)
                {
                    if (Interlocked.Increment(ref arrived) == 2) bothUpdated.TrySetResult();
                    await bothUpdated.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                }
                // Inversión deliberada únicamente en tablas técnicas para inducir SQL 1205.
                await IncrementAsync(context, second, token);
                return OperationResponse.Json(200, new { ok = true });
            }));
        await Task.WhenAll(Run(0, left, right), Run(1, right, left));
        Assert.InRange(attempts[0], 1, 3);
        Assert.InRange(attempts[1], 1, 3);
        Assert.True(attempts.Sum() >= 3);
        Assert.Equal(attempts.Sum(), contexts.Distinct().Count());
        Assert.Equal(2, await ValueAsync(left));
        Assert.Equal(2, await ValueAsync(right));
    }

    [Fact]
    public async Task Runtime_login_cannot_update_or_delete_confirmed_results()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var probe = await ProbeAsync();
        var request = Request(Guid.NewGuid(), probe);
        await ExecuteAsync(factory.Services, principal, request,
            (_, _) => Task.FromResult(new OperationResponse(204, null)));
        await using var db = fixture.ApplicationDatabase();
        var update = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.OperationResults SET ResultStatus = 200 WHERE OperationId = {request.OperationId}"));
        var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.OperationResults WHERE OperationId = {request.OperationId}"));
        Assert.Equal(229, update.Number);
        Assert.Equal(229, delete.Number);
    }

    [Fact]
    public async Task An_audit_insert_failure_rolls_back_previously_saved_changes_and_the_result()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var probe = await ProbeAsync();
        var request = Request(Guid.NewGuid(), probe);
        var article = await fixture.NewArticleAsync("AUD-" + Guid.NewGuid().ToString("N"), "Alta de prueba", "UD", false, false);
        using var scope = fixture.BootstrapServices.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await admin.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER dbo.__AuditFailure ON dbo.MasterDataAudit AFTER INSERT AS
                THROW 51001, N'Fallo inducido de auditoría', 1;
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => ExecuteAsync(factory.Services, principal, request,
                async (context, token) =>
                {
                    await IncrementAsync(context, probe, token);
                    context.Database.Articles.Add(article);
                    await context.Database.SaveChangesAsync(token);
                    MasterDataAuditWriter.Record(context, MasterDataEntity.Article, article.Id,
                        MasterDataAction.Create, null, new { article.Id, article.Sku });
                    return OperationResponse.Json(201, new { article.Id });
                }));
        }
        finally { await admin.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.__AuditFailure"); }
        Assert.Equal(0, await ValueAsync(probe));
        await using var check = fixture.ApplicationDatabase();
        Assert.False(await check.Articles.AnyAsync(value => value.Id == article.Id));
        Assert.False(await check.OperationResults.AnyAsync(value => value.OperationId == request.OperationId));
        Assert.False(await check.Set<MasterDataAudit>().AnyAsync(value => value.OperationId == request.OperationId));
    }

    [Fact]
    public async Task Audit_uses_the_verified_actor_survives_deletion_and_is_append_only()
    {
        using var factory = fixture.Factory();
        var principal = await PrincipalAsync();
        var probe = await ProbeAsync();
        var request = Request(Guid.NewGuid(), probe);
        await ExecuteAsync(factory.Services, principal, request, (context, _) =>
        {
            MasterDataAuditWriter.Record(context, MasterDataEntity.Article, probe,
                MasterDataAction.Delete, new { id = probe, sku = "REFERENCIA-HISTORICA" }, null);
            return Task.FromResult(new OperationResponse(204, null));
        });
        await using var check = fixture.ApplicationDatabase();
        var audit = await check.Set<MasterDataAudit>().SingleAsync(value => value.OperationId == request.OperationId);
        Assert.Equal(Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), audit.ActorId);
        Assert.Null(audit.After);
        Assert.NotNull(audit.Before);
        Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.MasterDataAudit SET [Before] = NULL WHERE Id = {audit.Id}"))).Number);
        Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.MasterDataAudit WHERE Id = {audit.Id}"))).Number);
    }

    [Fact]
    public void Fingerprint_ignores_json_property_order_but_not_values()
    {
        var a = JsonSerializer.Deserialize<JsonElement>("{\"name\":\"A\",\"code\":\"B\"}");
        var b = JsonSerializer.Deserialize<JsonElement>("{\"code\":\"B\",\"name\":\"A\"}");
        Assert.Equal(RequestFingerprint.Create("POST", "/api/v1/families", a),
            RequestFingerprint.Create("POST", "/api/v1/families", b));
        Assert.NotEqual(RequestFingerprint.Create("POST", "/api/v1/families", a),
            RequestFingerprint.Create("PUT", "/api/v1/families", a));
    }
}
