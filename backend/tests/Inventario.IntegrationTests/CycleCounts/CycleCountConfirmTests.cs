using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Shipping;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.CycleCounts.CycleCountApiSupport;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T009 (C-08–C-11, C-14, C-16; FR-008–FR-012, FR-025, US2; research D-05–D-07, D-13,
// INT-HU04-02). Revisar motivos (Inventory.Adjust), confirmar con un único movimiento Adjustment
// soportado en el CountCode y CounterpartyNotApplicableReason, revalidando teórico y reservado y sin
// modificar líneas; cancelar con motivo y sin efectos en stock. Invariantes 1–5 y 7 en cada confirmación.
public sealed class CycleCountConfirmTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Review_and_confirm_adjust_the_stock_with_one_traceable_movement()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var (a, _) = await support.ArticleAsync();
        var (b, _) = await support.ArticleAsync();
        var (c, _) = await support.ArticleAsync();
        await support.ReceiveAsync(a, u1, 10m);
        await support.ReceiveAsync(b, u1, 5m);
        var (id, detail, etag) = await StartedAsync(operario, u1);
        etag = await OkAsync(LinesAsync(operario, id, etag, new
        {
            counted = new[]
            {
                new { lineId = Line(detail, a, u1).GetProperty("id").GetGuid(), countedPhysical = "8" },
                new { lineId = Line(detail, b, u1).GetProperty("id").GetGuid(), countedPhysical = "5" }
            },
            found = new[] { new { articleId = c, locationId = u1, lotCode = (string?)null, countedPhysical = "3" } }
        }));
        etag = await OkAsync(SubmitAsync(operario, id, etag));
        (detail, _) = await DetailAsync(operario.Client, id);
        var lineA = Line(detail, a, u1).GetProperty("id").GetGuid();
        var lineB = Line(detail, b, u1).GetProperty("id").GetGuid();
        var lineC = Line(detail, c, u1).GetProperty("id").GetGuid();
        var order = detail.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("id").GetGuid()).ToList();

        // Sin Inventory.Adjust no se revisa ni se confirma; sin motivo no se confirma.
        using (var forbidden = await ConfirmAsync(operario, id, etag))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var forbiddenReview = await ReviewAsync(operario, id, etag, (lineA, "Rotura")))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenReview.StatusCode);
        using (var noReason = await ConfirmAsync(responsable, id, etag))
        {
            var fields = ErrorFields(await StockApiSupport.ProblemAsync(noReason, HttpStatusCode.UnprocessableEntity, "ValidationFailed"));
            Assert.Equal(new[] { $"lines[{order.IndexOf(lineA)}].reason", $"lines[{order.IndexOf(lineC)}].reason" }.Order(), fields.Order());
        }
        // Motivo en una línea sin diferencia, demasiado largo o vínculo a salida (llega en T012) → 422.
        using (var noDifference = await ReviewAsync(responsable, id, etag, (lineB, "Sin diferencia")))
            Assert.Contains("lines[0].reason", ErrorFields(await StockApiSupport.ProblemAsync(noDifference,
                HttpStatusCode.UnprocessableEntity, "ValidationFailed")));
        using (var tooLong = await ReviewAsync(responsable, id, etag, (lineA, new string('x', 501))))
            await StockApiSupport.ProblemAsync(tooLong, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        using (var link = await responsable.WriteAsync(HttpMethod.Put, $"{Counts}/{id:D}/review",
                   new { lines = new[] { new { lineId = lineA, reason = "Rotura", correctsShipmentId = Guid.NewGuid() } } }, version: etag))
            await StockApiSupport.ProblemAsync(link, HttpStatusCode.UnprocessableEntity, "ValidationFailed");

        var reviewed = await OkAsync(ReviewAsync(responsable, id, etag, (lineA, " Rotura no registrada "), (lineC, "Devolución sin registrar")));
        Assert.NotEqual(etag, reviewed);
        using (var stale = await ConfirmAsync(responsable, id, etag))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var (before, _) = await DetailAsync(operario.Client, id);
        Assert.Equal("Rotura no registrada", Line(before, a, u1).GetProperty("reason").GetString());
        Assert.NotEqual(JsonValueKind.Null, Line(before, a, u1).GetProperty("reasonBy").ValueKind);
        Assert.Equal("8.000", Line(before, a, u1).GetProperty("countedPhysical").GetString());

        using var confirmed = await ConfirmAsync(responsable, id, reviewed);
        var text = await confirmed.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == confirmed.StatusCode, text);
        Assert.Null(confirmed.Headers.ETag);
        var result = JsonDocument.Parse(text).RootElement;
        Assert.Equal(id, result.GetProperty("countId").GetGuid());
        var movementId = result.GetProperty("movementId").GetGuid();
        Assert.Equal(2, result.GetProperty("adjustedLines").GetArrayLength());

        var (after, _) = await DetailAsync(operario.Client, id);
        Assert.Equal("Confirmed", after.GetProperty("state").GetString());
        Assert.Equal(result.GetProperty("operationId").GetGuid(), after.GetProperty("operationId").GetGuid());
        Assert.Equal(movementId, after.GetProperty("adjustmentMovementId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("approvedBy").ValueKind);
        Assert.All(after.GetProperty("locations").EnumerateArray(), location => Assert.False(location.GetProperty("isFrozen").GetBoolean()));
        // Confirmar no modifica líneas (D-07).
        Assert.Equal(before.GetProperty("lines").GetRawText(), after.GetProperty("lines").GetRawText());

        await using (var database = fixture.ApplicationDatabase())
        {
            var movement = await database.Set<StockMovement>().AsNoTracking().Include(value => value.Entries)
                .SingleAsync(value => value.Id == movementId);
            Assert.Equal(("cycle-count", id, "Ajuste por conteo cíclico"), (movement.SourceDocumentType, movement.SourceDocumentId, movement.Reason));
            Assert.Equal("Ajuste de inventario: sin contraparte externa", movement.CounterpartyNotApplicableReason);
            Assert.Contains(movement.Entries, entry => entry.ArticleId == a && entry.Effect == StockEffect.AdjustDown && entry.Quantity == 2m);
            Assert.Contains(movement.Entries, entry => entry.ArticleId == c && entry.Effect == StockEffect.AdjustUp && entry.Quantity == 3m);
        }
        await AssertConfirmedInvariantsAsync(fixture, support, id);
        Assert.Equal(8m, (await support.Stock.BalanceAsync(a, u1))!.Physical);
        Assert.Equal(3m, (await support.Stock.BalanceAsync(c, u1))!.Physical);

        // Cerrado: ni confirmar de nuevo ni cancelar; la operación se puede comprobar.
        using (var again = await ConfirmAsync(responsable, id, reviewed))
            await StockApiSupport.ProblemAsync(again, HttpStatusCode.Conflict, "DocumentAlreadyConfirmed");
        using (var cancel = await CancelAsync(responsable, id, reviewed, "Tarde"))
            await StockApiSupport.ProblemAsync(cancel, HttpStatusCode.Conflict, "DocumentAlreadyConfirmed");
        using (var operation = await responsable.Client.GetAsync($"/api/v1/operations/{result.GetProperty("operationId").GetGuid():D}"))
            Assert.Equal(HttpStatusCode.OK, operation.StatusCode);
    }

    [Fact]
    public async Task A_count_without_differences_confirms_without_a_movement()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var (a, _) = await support.ArticleAsync();
        await support.ReceiveAsync(a, u1, 4m);
        var (id, _, etag) = await AwaitingAsync(operario, u1, (a, "4"));

        using var confirmed = await ConfirmAsync(responsable, id, etag);
        var text = await confirmed.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == confirmed.StatusCode, text);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(text).RootElement.GetProperty("movementId").ValueKind);
        await AssertConfirmedInvariantsAsync(fixture, support, id);
    }

    [Fact]
    public async Task Confirming_below_the_reserved_keeps_the_freeze_until_a_release_resolves_it()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await ShippingApiSupport.SeedAsync(support, operario, 5m, "6");
        var reservation = await ShippingApiSupport.ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "3");
        var (id, detail, etag) = await AwaitingAsync(operario, seed.Location, (seed.Article, "2"));
        var line = Line(detail, seed.Article, seed.Location).GetProperty("id").GetGuid();
        etag = await OkAsync(ReviewAsync(responsable, id, etag, (line, "Merma")));

        // Contado 2 < reservado 3: 409 conservando la congelación y sin cambios.
        using (var rejected = await ConfirmAsync(responsable, id, etag))
        {
            var problem = await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.Conflict, "ReservedStockConflict");
            // Nombra bucket, contado y reservado (contrato § Errores), no solo el rechazo genérico del núcleo.
            Assert.Contains("contado (2.000)", problem.GetProperty("detail").GetString());
            Assert.Contains("reservado (3.000)", problem.GetProperty("detail").GetString());
        }
        var (pending, pendingTag) = await DetailAsync(operario.Client, id);
        Assert.Equal(etag, pendingTag);
        Assert.Equal("AwaitingApproval", pending.GetProperty("state").GetString());
        Assert.All(pending.GetProperty("locations").EnumerateArray(), location => Assert.True(location.GetProperty("isFrozen").GetBoolean()));
        Assert.Equal(5m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);

        // Liberar 1 durante el conteo (Q1) y confirmar de nuevo.
        var (_, reservationTag) = await StockApiSupport.ReservationAsync(responsable.Client, reservation);
        await using (var database = fixture.ApplicationDatabase())
        {
            var allocation = await database.Set<ReservationAllocation>().Where(value => value.ReservationId == reservation)
                .Select(value => value.Id).SingleAsync();
            using var release = await StockApiSupport.PostReleaseAsync(responsable, reservation, reservationTag, null, Guid.NewGuid(),
                "Merma detectada en el conteo", (allocation, "1"));
            Assert.True(HttpStatusCode.OK == release.StatusCode, await release.Content.ReadAsStringAsync());
        }
        using (var confirmed = await ConfirmAsync(responsable, id, etag))
            Assert.True(HttpStatusCode.OK == confirmed.StatusCode, await confirmed.Content.ReadAsStringAsync());
        var balance = (await support.Stock.BalanceAsync(seed.Article, seed.Location))!;
        Assert.Equal((2m, 2m), (balance.Physical, balance.Reserved));
        await AssertConfirmedInvariantsAsync(fixture, support, id);
    }

    [Fact]
    public async Task A_theoretical_changed_outside_the_freeze_rejects_the_confirmation()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var (a, _) = await support.ArticleAsync();
        await support.ReceiveAsync(a, u1, 4m);
        var (id, detail, etag) = await AwaitingAsync(operario, u1, (a, "3"));
        etag = await OkAsync(ReviewAsync(responsable, id, etag, (Line(detail, a, u1).GetProperty("id").GetGuid(), "Merma")));
        // Escritura de prueba con el núcleo, que no consulta la congelación (un escritor que la olvidara).
        await support.ReceiveAsync(a, u1, 1m);

        using (var rejected = await ConfirmAsync(responsable, id, etag))
            await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.Conflict, "CountTheoreticalChanged");
        Assert.Equal("AwaitingApproval", (await DetailAsync(operario.Client, id)).Body.GetProperty("state").GetString());
        Assert.Equal(5m, (await support.Stock.BalanceAsync(a, u1))!.Physical);
    }

    [Fact]
    public async Task Cancelling_from_each_open_state_keeps_lines_and_stock_and_releases_the_scope()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var u2 = await support.LocationAsync("U2-" + StockApiSupport.Unique(6));
        var u3 = await support.LocationAsync("U3-" + StockApiSupport.Unique(6));
        var (a, _) = await support.ArticleAsync();
        await support.ReceiveAsync(a, u2, 4m);
        await support.ReceiveAsync(a, u3, 6m);

        var (draft, draftTag) = await CreateAsync(operario, u1);
        var (counting, _, countingTag) = await StartedAsync(operario, u2);
        var (awaiting, _, awaitingTag) = await AwaitingAsync(operario, u3, (a, "5"));

        using (var forbidden = await CancelAsync(operario, draft, draftTag, "Sin permiso"))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var noReason = await CancelAsync(responsable, counting, countingTag, "  "))
            Assert.Contains("reason", ErrorFields(await StockApiSupport.ProblemAsync(noReason, HttpStatusCode.UnprocessableEntity,
                "ValidationFailed")));
        using (var tooLong = await CancelAsync(responsable, counting, countingTag, new string('x', 501)))
            await StockApiSupport.ProblemAsync(tooLong, HttpStatusCode.UnprocessableEntity, "ValidationFailed");

        foreach (var (id, etag) in new[] { (draft, draftTag), (counting, countingTag), (awaiting, awaitingTag) })
        {
            using var cancelled = await CancelAsync(responsable, id, etag, "Conteo repetido");
            var text = await cancelled.Content.ReadAsStringAsync();
            Assert.True(HttpStatusCode.OK == cancelled.StatusCode, text);
            Assert.Equal(id, JsonDocument.Parse(text).RootElement.GetProperty("countId").GetGuid());
            var (detail, _) = await DetailAsync(operario.Client, id);
            Assert.Equal(("Cancelled", "Conteo repetido"), (detail.GetProperty("state").GetString(), detail.GetProperty("cancelReason").GetString()));
            Assert.All(detail.GetProperty("locations").EnumerateArray(), location => Assert.False(location.GetProperty("isFrozen").GetBoolean()));
        }
        // Líneas conservadas y existencias intactas; sin movimientos del conteo.
        Assert.Equal(1, (await DetailAsync(operario.Client, awaiting)).Body.GetProperty("lines").GetArrayLength());
        Assert.Equal(4m, (await support.Stock.BalanceAsync(a, u2))!.Physical);
        Assert.Equal(6m, (await support.Stock.BalanceAsync(a, u3))!.Physical);
        await using var database = fixture.ApplicationDatabase();
        Assert.False(await database.Set<StockMovement>().AnyAsync(value => value.SourceDocumentType == "cycle-count"
            && (value.SourceDocumentId == counting || value.SourceDocumentId == awaiting)));
    }

    [Fact]
    public async Task An_inactive_article_is_adjusted_with_reason_and_authorization()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var gestor = await support.SessionAsync("GestorMaestros");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var (a, sku) = await support.ArticleAsync();
        await support.ReceiveAsync(a, u1, 4m);
        await StockApiSupport.DeactivateArticleAsync(gestor, a, sku);

        var (id, detail, etag) = await AwaitingAsync(operario, u1, (a, "3"));
        etag = await OkAsync(ReviewAsync(responsable, id, etag, (Line(detail, a, u1).GetProperty("id").GetGuid(), "Merma")));
        using (var confirmed = await ConfirmAsync(responsable, id, etag))
            Assert.True(HttpStatusCode.OK == confirmed.StatusCode, await confirmed.Content.ReadAsStringAsync());
        Assert.Equal(3m, (await support.Stock.BalanceAsync(a, u1))!.Physical);
        var article = await gestor.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles/{a:D}");
        Assert.False(article.GetProperty("isActive").GetBoolean());
        await AssertConfirmedInvariantsAsync(fixture, support, id);
    }
}
