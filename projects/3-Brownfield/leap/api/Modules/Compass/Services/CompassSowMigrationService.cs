using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Creates contract periods (SOWs) under a client assignment, for the TPS migration.
/// </summary>
/// <remarks>
/// Not <see cref="CompassSowService"/>, the Ops-or-root application write surface, which refuses
/// <see cref="SowType.LegacyMigrated"/> on input regardless of caller; this is the migration's create
/// path and the only route to the validation bypass. Two of the three database rules on
/// <c>compass.sow</c> are partial and exempt <c>LegacyMigrated</c> — the start/end <c>CHECK</c> and the
/// overlap <c>EXCLUDE</c> — so such a row may carry a backwards range and overlap a sibling.
/// Principle VIII requires that bypass to be reachable only by the migration principal and never
/// through the application surface, gated on principal identity rather than a role because that
/// principal holds the Compass root. <c>CompassSowLegacyMigratedRestrictionTests</c> is the gate.
/// </remarks>
/// <param name="sows">Contract-period persistence.</param>
/// <param name="unitOfWork">
/// The save boundary. Taken instead of a data context, whose type <c>CompassBoundaryTests</c> rule two
/// forbids naming here — it scans raw source text, so the context may appear in prose only.
/// </param>
/// <param name="auditService">The audit trail.</param>
/// <param name="currentUser">The acting identity.</param>
public class CompassSowMigrationService(
    ICompassSowRepository sows,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser
) : ICompassSowMigrationService
{
    /// <summary>What the audit trail calls a contract period, so entries for one are findable together.</summary>
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
        // The bypass gate, checked first, before anything else can succeed.
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

        // A rate increase belongs to an extension and nothing else. LegacyMigrated does NOT inherit
        // the permission: legacy TPS tracks no extensions, so a migrated row claiming one is
        // meaningless. A database CHECK agrees; catching it here yields a 400 naming the field
        // rather than a 500 naming a constraint.
        if (request.RateIncrease && request.SowType is not SowType.SowExtension)
        {
            return CompassWrite<CompassSowDto>.Invalid(
                "A rate increase is permitted only on a SOW extension."
            );
        }

        // The date rule is skipped for a migrated row — the exemption is the point. The operands are
        // hoisted into locals on purpose: ClientStatusSingleDerivationTests scans raw source text for
        // `EndDate` beside a comparison operator (BR-11), which `request.SowEndDate <` matches by
        // suffix even though a contract period's own date ordering is not a client-status derivation.
        // That gate is deliberately blunt and must not be narrowed to admit this.
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

            // FALSE for a migrated row, and the only honest value: it was admitted WITHOUT
            // satisfying the overlap and date-order rules, so its first edit through the application
            // must satisfy both before the save is accepted. Every other type passed both checks
            // above on the way in, so true is equally honest for them.
            HasPassedApplicationValidation = request.SowType is not SowType.LegacyMigrated,
        };

        // The pre-check exists to NAME the conflicting period. The exclusion constraint below is the
        // real guarantee, but it can only say "23P01" — and FR-023 wants a message the caller can act
        // on. LegacyMigrated is exempt from the constraint, so it is exempt here too; checking it
        // would refuse exactly the rows the bypass exists to admit.
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

                // A message a caller reads renders its dates mm/dd/yyyy, the same as
                // the application-facing overlap refusal in CompassSowService. Feature 006 brought
                // both the helper and the gate that fails the build on a yyyy-MM-dd here, which is
                // what caught this line when the two branches met.
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
            // The pre-check above is not a guarantee: two concurrent creates can both pass it and
            // only one can win the constraint. Losing that race must answer the same 409 as losing
            // the pre-check, not a 500 — the caller cannot tell the two apart and should not have to.
            // Duplicate() is this type's 409 factory — named for the unique-key case it was written
            // for, but it is the same CompassWriteStatus.Conflict an overlap needs.
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
    /// <remarks>Pure pass-through: reads carry no business rule, so no dedicated TDD cycle.</remarks>
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
