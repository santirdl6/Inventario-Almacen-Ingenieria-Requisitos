using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventario.Api.Infrastructure.Persistence.Configurations;

// HU-04: conteo cíclico (specs/006-realizar-inventarios-ciclicos/data-model.md). DENY DELETE y
// TR_CycleCounts_ClosedImmutable, creados a mano en la migración (research D-07), impiden borrar un
// conteo y cambiar uno cerrado.
internal sealed class CycleCountConfiguration : IEntityTypeConfiguration<CycleCount>
{
    public void Configure(EntityTypeBuilder<CycleCount> builder)
    {
        builder.ToTable("CycleCounts", table =>
        {
            // Campos obligatorios de cada estado (D-02, data-model).
            table.HasCheckConstraint("CK_CycleCounts_State",
                "([State] = N'Draft' AND [StartedBy] IS NULL AND [SubmittedBy] IS NULL AND [ApprovedBy] IS NULL "
                + "AND [CancelledBy] IS NULL AND [OperationId] IS NULL) "
                + "OR ([State] = N'Counting' AND [StartedBy] IS NOT NULL AND [SubmittedBy] IS NULL AND [ApprovedBy] IS NULL "
                + "AND [CancelledBy] IS NULL AND [OperationId] IS NULL) "
                + "OR ([State] = N'AwaitingApproval' AND [StartedBy] IS NOT NULL AND [SubmittedBy] IS NOT NULL "
                + "AND [ApprovedBy] IS NULL AND [CancelledBy] IS NULL AND [OperationId] IS NULL) "
                + "OR ([State] = N'Confirmed' AND [StartedBy] IS NOT NULL AND [SubmittedBy] IS NOT NULL "
                + "AND [ApprovedBy] IS NOT NULL AND [CancelledBy] IS NULL AND [OperationId] IS NOT NULL) "
                + "OR ([State] = N'Cancelled' AND [ApprovedBy] IS NULL AND [CancelledBy] IS NOT NULL "
                + "AND [OperationId] IS NOT NULL AND [AdjustmentMovementId] IS NULL)");
            table.HasCheckConstraint("CK_CycleCounts_Pairs",
                "(([StartedBy] IS NULL AND [StartedAt] IS NULL) OR ([StartedBy] IS NOT NULL AND [StartedAt] IS NOT NULL)) "
                + "AND (([SubmittedBy] IS NULL AND [SubmittedAt] IS NULL) OR ([SubmittedBy] IS NOT NULL AND [SubmittedAt] IS NOT NULL)) "
                + "AND (([ApprovedBy] IS NULL AND [ApprovedAt] IS NULL) OR ([ApprovedBy] IS NOT NULL AND [ApprovedAt] IS NOT NULL)) "
                + "AND (([CancelledBy] IS NULL AND [CancelledAt] IS NULL AND [CancelReason] IS NULL) "
                + "OR ([CancelledBy] IS NOT NULL AND [CancelledAt] IS NOT NULL AND [CancelReason] IS NOT NULL))");
            table.HasCheckConstraint("CK_CycleCounts_CancelReason",
                "[CancelReason] IS NULL OR LEN(LTRIM(RTRIM([CancelReason]))) > 0");
            // EF no puede usar OUTPUT sin INTO en tablas con triggers (lección de HU-03).
            table.HasTrigger("TR_CycleCounts_ClosedImmutable");
        });
        builder.HasKey(count => count.Id);
        builder.Property(count => count.Id).ValueGeneratedNever();
        builder.Property(count => count.CountCode).HasMaxLength(16).IsRequired();
        builder.Property(count => count.State).HasConversion<string>().HasMaxLength(24);
        builder.Property(count => count.CancelReason).HasMaxLength(CycleCount.CancelReasonMaxLength);
        // ETag del detalle (D-11, D-12).
        builder.Property(count => count.Version).IsRowVersion();
        builder.Ignore(count => count.IsClosed);

        builder.HasIndex(count => count.CountCode).IsUnique().HasDatabaseName("UX_CycleCounts_CountCode");
        // Sin FK, como Shipments.OperationId: es la operación que confirma o cancela.
        builder.HasIndex(count => count.OperationId).IsUnique()
            .HasFilter("[OperationId] IS NOT NULL").HasDatabaseName("UX_CycleCounts_OperationId");
        builder.HasIndex(count => new { count.CreatedAt, count.Id }).IsDescending(true, false)
            .HasDatabaseName("IX_CycleCounts_CreatedAt");

        foreach (var actor in new[] { nameof(CycleCount.CreatedBy), nameof(CycleCount.StartedBy), nameof(CycleCount.SubmittedBy),
                     nameof(CycleCount.ApprovedBy), nameof(CycleCount.CancelledBy) })
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(actor).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(count => count.AdjustmentMovementId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade solo actúa al editar el alcance de un borrador y al retirar hallazgos: DENY DELETE
        // impide borrar la cabecera.
        builder.HasMany(count => count.Locations).WithOne().HasForeignKey(location => location.CountId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(count => count.Locations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(count => count.Lines).WithOne().HasForeignKey(line => line.CountId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(count => count.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

// HU-04: ubicación del alcance y congelación (D-04). TR_CountLocations_Scope (D-07).
internal sealed class CountLocationConfiguration : IEntityTypeConfiguration<CountLocation>
{
    public void Configure(EntityTypeBuilder<CountLocation> builder)
    {
        builder.ToTable("CountLocations", table => table.HasTrigger("TR_CountLocations_Scope"));
        builder.HasKey(location => new { location.CountId, location.LocationId });
        // Una ubicación solo puede estar congelada por un conteo abierto (defensa SQL de D-04).
        builder.HasIndex(location => location.LocationId).IsUnique().HasFilter("[IsFrozen] = 1")
            .HasDatabaseName("UX_CountLocations_Frozen");
        // Lector IsInOpenCountAsync y consultas por ubicación.
        builder.HasIndex(location => new { location.LocationId, location.IsFrozen }).HasDatabaseName("IX_CountLocations_Location");
        builder.HasOne<Location>().WithMany().HasForeignKey(location => location.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// HU-04: línea del conteo. TR_CountLines_OpenCount (D-07). Difference es calculada persistida.
internal sealed class CountLineConfiguration : IEntityTypeConfiguration<CountLine>
{
    private const string Difference = "[CountedPhysical] - [TheoreticalPhysical]";

    public void Configure(EntityTypeBuilder<CountLine> builder)
    {
        builder.ToTable("CountLines", table =>
        {
            table.HasCheckConstraint("CK_CountLines_Quantities",
                "[TheoreticalPhysical] >= 0 AND ([CountedPhysical] IS NULL OR [CountedPhysical] >= 0)");
            // Hallazgo: teórico 0 y contado > 0. Esperada: siempre con lote existente.
            table.HasCheckConstraint("CK_CountLines_Found",
                "([IsFound] = 1 AND [TheoreticalPhysical] = 0 AND [CountedPhysical] > 0) "
                + "OR ([IsFound] = 0 AND [LotId] IS NOT NULL)");
            table.HasCheckConstraint("CK_CountLines_Pairs",
                "(([CountedPhysical] IS NULL AND [CountedBy] IS NULL AND [CountedAt] IS NULL) "
                + "OR ([CountedPhysical] IS NOT NULL AND [CountedBy] IS NOT NULL AND [CountedAt] IS NOT NULL)) "
                + "AND (([Reason] IS NULL AND [ReasonBy] IS NULL) OR ([Reason] IS NOT NULL AND [ReasonBy] IS NOT NULL))");
            // El vínculo a una salida y el motivo solo en líneas con diferencia (D-09).
            table.HasCheckConstraint("CK_CountLines_Shipment",
                "([CorrectsShipmentId] IS NULL AND [Reason] IS NULL) "
                + "OR ([CountedPhysical] IS NOT NULL AND [CountedPhysical] <> [TheoreticalPhysical])");
            table.HasTrigger("TR_CountLines_OpenCount");
        });
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.LotCode).HasMaxLength(Lot.CodeMaxLength).IsRequired();
        builder.Property(line => line.TheoreticalPhysical).HasColumnType(StockColumns.Quantity);
        builder.Property(line => line.CountedPhysical).HasColumnType(StockColumns.Quantity);
        builder.Property(line => line.Reason).HasMaxLength(CountLine.ReasonMaxLength);
        builder.Ignore(line => line.HasDifference);
        // Columna calculada persistida: SQL Server es la fuente; el dominio la mantiene igual en memoria.
        var difference = builder.Property(line => line.Difference)
            .HasColumnType(StockColumns.Quantity).HasComputedColumnSql(Difference, stored: true);
        difference.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        difference.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(line => new { line.CountId, line.ArticleId, line.LocationId, line.LotCode })
            .IsUnique().HasDatabaseName("UX_CountLines_Bucket");
        builder.HasIndex(line => new { line.ArticleId, line.LocationId, line.LotId }).HasDatabaseName("IX_CountLines_Bucket");

        // La ubicación pertenece al alcance del conteo.
        builder.HasOne<CountLocation>().WithMany()
            .HasForeignKey(line => new { line.CountId, line.LocationId })
            .HasPrincipalKey(location => new { location.CountId, location.LocationId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_CountLines_CountLocations_CountId_LocationId");
        builder.HasOne<Article>().WithMany().HasForeignKey(line => line.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        // Como StockBalance: el lote pertenece al artículo de la línea. Null en un hallazgo de lote nuevo.
        builder.HasOne<Lot>().WithMany()
            .HasForeignKey(line => new { line.ArticleId, line.LotId })
            .HasPrincipalKey(lot => new { lot.ArticleId, lot.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shipment>().WithMany().HasForeignKey(line => line.CorrectsShipmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(line => line.CountedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(line => line.ReasonBy).OnDelete(DeleteBehavior.Restrict);
    }
}
