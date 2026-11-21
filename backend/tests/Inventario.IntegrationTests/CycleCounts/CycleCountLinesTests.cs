using System.Net;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Stock;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inventario.IntegrationTests.CycleCounts.CycleCountApiSupport;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T008 (C-06, C-07; FR-004–FR-007, US1.3–US1.5, US5; research D-03, D-08, H6 de analyze-1).
// Contar con cero explícito y fracciones según el artículo; hallazgos en el alcance y con lote si el
// artículo lo gestiona; retirar solo hallazgos (con su referencia); enviar solo con todo contado; el
// motivo no se acepta al contar; en AwaitingApproval no se cuenta (409 CountStateConflict).
public sealed class CycleCountLinesTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Counting_findings_and_submitting_follow_the_rules()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var outside = await support.LocationAsync("U9-" + StockApiSupport.Unique(6));
        var (a, _) = await support.ArticleAsync();
        var (b, _) = await support.ArticleAsync();
        var (c, _) = await support.ArticleAsync();
        var (t, _) = await support.ArticleAsync(tracksLot: true);
        await support.ReceiveAsync(a, u1, 10m);
        await support.ReceiveAsync(b, u1, 5m);
        await support.ReceiveAsync(t, u1, 4m, "L1");
        var (id, detail, etag) = await StartedAsync(operario, u1);
        var lineA = Line(detail, a, u1).GetProperty("id").GetGuid();
        var lineB = Line(detail, b, u1).GetProperty("id").GetGuid();
        var lineT = Line(detail, t, u1).GetProperty("id").GetGuid();

        // Forma: fracción en un artículo entero, negativo, motivo al contar (H6) y línea ajena.
        // La forma (negativo) se valida antes de bloquear; la fracción, con el artículo leído tras el bloqueo.
        using (var negative = await LinesAsync(operario, id, etag, Counted((lineA, "8"), (lineB, "-1"))))
            Assert.Equal(["counted[1].countedPhysical"], ErrorFields(await StockApiSupport.ProblemAsync(negative,
                HttpStatusCode.UnprocessableEntity, "ValidationFailed")));
        using (var fraction = await LinesAsync(operario, id, etag, Counted((lineA, "7.5"), (lineB, "5"))))
            Assert.Equal(["counted[0].countedPhysical"], ErrorFields(await StockApiSupport.ProblemAsync(fraction,
                HttpStatusCode.UnprocessableEntity, "ValidationFailed")));
        using (var reason = await LinesAsync(operario, id, etag, new
               {
                   counted = new[] { new { lineId = lineA, countedPhysical = "8", reason = "Rotura" } }, found = Array.Empty<object>()
               }))
            await StockApiSupport.ProblemAsync(reason, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        using (var foreign = await LinesAsync(operario, id, etag, Counted((Guid.NewGuid(), "1"))))
            Assert.Contains("counted[0].lineId", ErrorFields(await StockApiSupport.ProblemAsync(foreign,
                HttpStatusCode.UnprocessableEntity, "ValidationFailed")));

        // Contar 8 y 5 (cero diferencia explícita); T sin contar.
        etag = await OkAsync(LinesAsync(operario, id, etag, Counted((lineA, "8"), (lineB, "5"))));
        using (var stale = await LinesAsync(operario, id, "\"AAAAAAAAAAA=\"", Counted((lineA, "8"))))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // Enviar con una línea sin contar: 422 por línea, sigue en Counting.
        var (current, _) = await DetailAsync(operario.Client, id);
        var indexT = current.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("id").GetGuid()).ToList().IndexOf(lineT);
        using (var early = await SubmitAsync(operario, id, etag))
            Assert.Equal([$"lines[{indexT}].countedPhysical"], ErrorFields(await StockApiSupport.ProblemAsync(early,
                HttpStatusCode.UnprocessableEntity, "ValidationFailed")));

        // Hallazgos: fuera del alcance, sin lote en un artículo con lotes, bucket ya presente → 422.
        using (var bad = await LinesAsync(operario, id, etag, new
               {
                   counted = Array.Empty<object>(),
                   found = new object[]
                   {
                       new { articleId = c, locationId = outside, lotCode = (string?)null, countedPhysical = "3" },
                       new { articleId = t, locationId = u1, lotCode = (string?)null, countedPhysical = "1" },
                       new { articleId = a, locationId = u1, lotCode = (string?)null, countedPhysical = "1" }
                   }
               }))
        {
            var fields = ErrorFields(await StockApiSupport.ProblemAsync(bad, HttpStatusCode.UnprocessableEntity, "ValidationFailed"));
            Assert.Contains("found[0].locationId", fields);
            Assert.Contains("found[1].lotCode", fields);
            Assert.Contains("found[2].articleId", fields);
        }
        // Un hallazgo con cero no existe: error de forma.
        using (var zero = await LinesAsync(operario, id, etag, new
               {
                   counted = Array.Empty<object>(),
                   found = new[] { new { articleId = c, locationId = u1, lotCode = (string?)null, countedPhysical = "0" } }
               }))
            Assert.Equal(["found[0].countedPhysical"], ErrorFields(await StockApiSupport.ProblemAsync(zero,
                HttpStatusCode.UnprocessableEntity, "ValidationFailed")));

        // Hallazgo de C (3) y de T con un lote nuevo; cero explícito en T/L1.
        etag = await OkAsync(LinesAsync(operario, id, etag, new
        {
            counted = new[] { new { lineId = lineT, countedPhysical = "0" } },
            found = new object[]
            {
                new { articleId = c, locationId = u1, lotCode = (string?)null, countedPhysical = "3" },
                new { articleId = t, locationId = u1, lotCode = "l-nuevo", countedPhysical = "2" }
            }
        }));
        (current, _) = await DetailAsync(operario.Client, id);
        var foundC = Line(current, c, u1);
        Assert.Equal((true, "0.000", "3.000", "3.000"), (foundC.GetProperty("isFound").GetBoolean(),
            foundC.GetProperty("theoreticalPhysical").GetString(), foundC.GetProperty("countedPhysical").GetString(),
            foundC.GetProperty("difference").GetString()));
        Assert.Equal("-2.000", Line(current, a, u1).GetProperty("difference").GetString());
        Assert.Equal("0.000", Line(current, b, u1).GetProperty("difference").GetString());
        var lots = current.GetProperty("lines").EnumerateArray().Where(line => line.GetProperty("articleId").GetGuid() == t)
            .ToDictionary(line => line.GetProperty("lotCode").GetString()!, line => line);
        Assert.Equal("-4.000", lots["L1"].GetProperty("difference").GetString());
        Assert.True(lots["L-NUEVO"].GetProperty("isFound").GetBoolean());
        Assert.True((await UsageAsync(factory, c)).HasReferences);

        // Retirar: una línea esperada no (422); un hallazgo sí, con su referencia; inexistente → 404.
        using (var expected = await RemoveLineAsync(operario, id, lineA, etag))
            await StockApiSupport.ProblemAsync(expected, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        using (var missing = await RemoveLineAsync(operario, id, Guid.NewGuid(), etag))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var removed = await RemoveLineAsync(operario, id, foundC.GetProperty("id").GetGuid(), etag))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
            Assert.NotEqual(etag, removed.Headers.ETag!.ToString());
            etag = removed.Headers.ETag!.ToString();
        }
        Assert.False((await UsageAsync(factory, c)).HasReferences);

        // Enviar con todo contado; después no se cuenta ni se retira.
        using (var submitted = await SubmitAsync(operario, id, etag))
        {
            Assert.True(HttpStatusCode.OK == submitted.StatusCode, await submitted.Content.ReadAsStringAsync());
            etag = submitted.Headers.ETag!.ToString();
        }
        var (final, _) = await DetailAsync(operario.Client, id);
        Assert.Equal("AwaitingApproval", final.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, final.GetProperty("submittedAt").ValueKind);
        Assert.All(final.GetProperty("lines").EnumerateArray(), line => Assert.Equal(JsonValueKind.Null, line.GetProperty("reason").ValueKind));
        using (var late = await LinesAsync(operario, id, etag, Counted((lineA, "9"))))
            await StockApiSupport.ProblemAsync(late, HttpStatusCode.Conflict, "CountStateConflict");
        using (var lateRemove = await RemoveLineAsync(operario, id, lots["L-NUEVO"].GetProperty("id").GetGuid(), etag))
            await StockApiSupport.ProblemAsync(lateRemove, HttpStatusCode.Conflict, "CountStateConflict");
        using (var lateSubmit = await SubmitAsync(operario, id, etag))
            await StockApiSupport.ProblemAsync(lateSubmit, HttpStatusCode.Conflict, "CountStateConflict");

        // Contar no cambia existencias.
        Assert.Equal(10m, (await support.Stock.BalanceAsync(a, u1))!.Physical);
    }

    [Fact]
    public async Task A_draft_cannot_be_counted_and_fractions_are_admitted_when_the_article_allows_them()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var (draft, draftTag) = await CreateAsync(operario, u1);
        using (var early = await LinesAsync(operario, draft, draftTag, Counted((Guid.NewGuid(), "1"))))
            await StockApiSupport.ProblemAsync(early, HttpStatusCode.Conflict, "CountStateConflict");

        var article = await fixture.NewArticleAsync("H04F-" + StockApiSupport.Unique(8), "Artículo a granel", "KG", true, false);
        await using (var database = fixture.ApplicationDatabase())
        {
            database.Articles.Add(article);
            await database.SaveChangesAsync();
        }
        await support.ReceiveAsync(article.Id, u1, 2.5m);
        using (var started = await StartAsync(operario, draft, draftTag))
            draftTag = started.Headers.ETag!.ToString();
        var (detail, _) = await DetailAsync(operario.Client, draft);
        var line = Line(detail, article.Id, u1).GetProperty("id").GetGuid();
        await OkAsync(LinesAsync(operario, draft, draftTag, Counted((line, "2.25"))));
        Assert.Equal("-0.250", Line((await DetailAsync(operario.Client, draft)).Body, article.Id, u1).GetProperty("difference").GetString());
    }

    private static async Task<ArticleUsage> UsageAsync(ApiFactory factory, Guid article)
    {
        using var scope = factory.Services.CreateScope();
        var contributor = scope.ServiceProvider.GetServices<IArticleUsageContributor>()
            .Single(value => value.ReferencingEntityTypes.Contains(typeof(CountLine)));
        return await contributor.ReadAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>(), article, CancellationToken.None);
    }
}
