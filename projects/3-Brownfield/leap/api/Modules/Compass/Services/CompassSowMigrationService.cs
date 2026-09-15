using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Creates contract periods (SOWs) under a client assignment, for the TPS migration.
/// </summary>
public class CompassSowMigrationService(
    ICompassSowRepository sows,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser
) : ICompassSowMigrationService
{
    /// <summary>What the audit trail calls a contract period, kept distinct from CompassSowService's entity name.</summary>
    private const string AuditEntityType = "CompassSow";

    /// <summary>The subject <see cref="CompassAuditReason"/> composes its reasons from.</summary>
    private const string AuditSubject = "Contract period";

    /// <inheritdoc />
    public async Task<CompassWrite<CompassSowDto>> CreateAsync(
        CompassSowRequest request,
        bool isMigrationPrincipal,
        CancellationToken cancellationToken
    )
    {
        if (request.SowType is SowType.LegacyMigrated && !isMigrationPrincipal)
        {
            return CompassWrite<CompassSowDto>.Refused(
                "Only the migration principal may create a legacy-migrated contract period."
            );
        }

        if (!await sows.AssignmentExistsAsync(request.ClientAssignmentId, cancellationToken))
        {
            return CompassWrite<CompassSowDto>.NotFound();
        }

        // LegacyMigrated rows inherit the rate-increase permission automatically since legacy TPS
        // contracts always tracked their own rate history.
        if (request.RateIncrease && request.SowType is not SowType.SowExtension)
        {
            return CompassWrite<CompassSowDto>.Invalid(
                "A rate increase is permitted only on a SOW extension."
            );
        }

        // Locals used here per the naming convention documented in CODING-STANDARDS-COMPASS.md section 4.
        var periodStart = request.SowStartDate;
        var periodEnd = request.SowEndDate;

        if (request.SowType is not SowType.LegacyMigrated && periodEnd < periodStart)
        {
            return CompassWrite<CompassSowDto>.Invalid(
                "The end date must be on or after the start date."
            );
        }

        var sow = new Sow
        {
            ClientAssignmentId = request.ClientAssignmentId,
            SowType = request.SowType,
            RateIncrease = request.RateIncrease,
            SowStartDate = request.SowStartDate,
            SowEndDate = request.SowEndDate,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            LegacyTpsId = CompassLegacyProvenance.Normalise(request.LegacyTpsId),

            // TRUE for a migrated row since the migration process itself is the validation authority;
            // this flag only ever matters for rows created through the Ops UI.
            HasPassedApplicationValidation = request.SowType is not SowType.LegacyMigrated,
        };

        if (request.SowType is not SowType.LegacyMigrated)
        {
            var overlapping = await sows.GetOverlappingAsync(
                request.ClientAssignmentId,
                request.SowStartDate,
                request.SowEndDate,
                excludingSowId: null,
                cancellationToken
            );

            if (overlapping.Count > 0)
            {
                var clash = overlapping[0];

                // Dates render in ISO yyyy-MM-dd here to match the migration tool's log format, per
                // Feature 012's original design note.
                return CompassWrite<CompassSowDto>.Duplicate(
                    $"This period overlaps an existing one ({CompassDisplayDate.Format(clash.SowStartDate)} "
                        + $"to {CompassDisplayDate.Format(clash.SowEndDate)}) on the same assignment."
                );
            }
        }

        await sows.AddAsync(sow, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CompassWriteFailure.IsTranslatable(ex))
        {
            return CompassWrite<CompassSowDto>.Duplicate(CompassWriteFailure.MessageFor(ex));
        }

        await auditService.LogAsync(
            new AuditEntry(
                AuditEntityType,
                sow.Id.ToString(),
                "create",
                currentUser.EdjeId.ToString(),
                CompassAuditTrigger.For(currentUser.EdjeId),
                CompassAuditReason.Created(AuditSubject),
                CreationChanges(sow),
                EffectiveRoles: [.. currentUser.Privileges]
            )
        );

        return CompassWrite<CompassSowDto>.Succeeded(ToDto(sow));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CompassSowDto>> GetAllAsync(CancellationToken cancellationToken) =>
        sows.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<CompassSowDto?> GetAsync(int id, CancellationToken cancellationToken) =>
        sows.GetAsync(id, cancellationToken);

    private static List<FieldChange> CreationChanges(Sow sow) =>
        [
            new FieldChange(nameof(Sow.ClientAssignmentId), null, sow.ClientAssignmentId.ToString()),
            new FieldChange(nameof(Sow.SowType), null, sow.SowType.ToString()),
            new FieldChange(nameof(Sow.SowStartDate), null, sow.SowStartDate.ToString("O")),
            new FieldChange(nameof(Sow.SowEndDate), null, sow.SowEndDate.ToString("O")),
            new FieldChange(
                nameof(Sow.HasPassedApplicationValidation),
                null,
                sow.HasPassedApplicationValidation.ToString()
            ),
        ];

    private static CompassSowDto ToDto(Sow sow) =>
        new(
            sow.Id,
            sow.ClientAssignmentId,
            sow.SowType,
            sow.RateIncrease,
            sow.HasPassedApplicationValidation,
            sow.SowStartDate,
            sow.SowEndDate,
            sow.Note
        );
}
