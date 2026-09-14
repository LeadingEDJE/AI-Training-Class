using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Client configuration — the module's third write path, and its second audited one.
/// </summary>
/// <remarks>
/// The save happens before the audit call, for the same two reasons as
/// <see cref="CompassEmployeeService"/>: a create needs the identity key EF assigns at save, and
/// <see cref="ICompassUnitOfWork"/> is what turns a lost uniqueness race into
/// <see cref="CompassDuplicateKeyException"/> rather than an unhandled provider failure and a bare 500.
/// Category writes are audited against the client, because AC-NFR-3 puts billable categories and the
/// invoice-frequency default inside the client's audited scope. There is no deactivation path and no
/// delete: a client's Active/Inactive is derived from its assignments (FR-021, FR-035).
/// </remarks>
public class CompassClientService(
    ICompassClientRepository clients,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser,
    IClientStatusDerivation statusDerivation,
    ICompassBusinessDate businessDate
) : ICompassClientService
{
    /// <summary>What the audit trail calls a client, so every entry for one is findable together.</summary>
    private const string AuditEntityType = "CompassClient";

    /// <summary>The subject <see cref="CompassAuditReason"/> composes its reasons from.</summary>
    private const string AuditSubject = "Client";

    /// <summary>Who the change came through.</summary>
    /// <remarks>
    /// Resolved per write rather than held constant: a bulk migration holds the Compass root role and is
    /// otherwise indistinguishable from an administrator, which Principle VIII forbids. See
    /// <see cref="CompassAuditTrigger"/>.
    /// </remarks>
    private string AuditTriggeredBy => CompassAuditTrigger.For(currentUser.EdjeId);

    private const int MaxClientNameLength = 200;
    private const int MaxCategoryNameLength = 100;

    // reads

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientSummaryDto>> GetClientsAsync(
        CancellationToken cancellationToken
    )
    {
        // Read ONCE per request, so every row of one response is judged against the same day. Asking
        // per row could straddle midnight and report two clients inconsistently — the boundary AC-42
        // names as the one independent implementations get wrong.
        var today = businessDate.Today();

        return
        [
            .. (await clients.GetAllWithFrequencyNameAsync(today, cancellationToken)).Select(row =>
                new ClientSummaryDto(
                    row.Client.Id,
                    row.Client.ClientName,
                    row.Client.IsInternal,
                    row.InvoiceFrequencyTypeName,
                    // From the derivation, never named here: a second place naming these values can
                    // name them wrongly, which is what BR-11's single-implementation rule prevents.
                    statusDerivation
                        .StatusOfClient(row.HoldsCurrentAssignment, row.HasEverBeenAssigned)
                        .ToString()
                )
            ),
        ];
    }

    /// <inheritdoc />
    public async Task<ClientDto?> GetClientAsync(int id, CancellationToken cancellationToken)
    {
        var client = await clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return null;
        }

        var facts = await clients.GetStatusFactsAsync(id, businessDate.Today(), cancellationToken);

        return ToDto(
            client,
            statusDerivation
                .StatusOfClient(facts.HoldsCurrentAssignment, facts.HasEverBeenAssigned)
                .ToString()
        );
    }

    // client writes

    /// <inheritdoc />
    public Task<CompassWrite<ClientDto>> CreateClientAsync(
        CompassClientRequest request,
        CancellationToken cancellationToken
    ) =>
        unitOfWork.ExecuteAtomicallyAsync(
            ct => CreateClientAsyncCoreAsync(request, ct),
            result => result.Status == CompassWriteStatus.Success,
            cancellationToken);

    private async Task<CompassWrite<ClientDto>> CreateClientAsyncCoreAsync(
        CompassClientRequest request,
        CancellationToken cancellationToken
    )
    {
        var validation = await ValidateAsync(request, existing: null, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var client = new Client
        {
            ClientName = request.ClientName.Trim(),
            MsaSignedDate = request.MsaSignedDate,
            NdaSignedDate = request.NdaSignedDate,
            IsInternal = request.IsInternal,
            InvoiceFrequencyTypeId = request.InvoiceFrequencyTypeId,
            LegacyTpsId = CompassLegacyProvenance.Normalise(request.LegacyTpsId),
        };

        await clients.AddAsync(client, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException exception)
        {
            // The name pre-check is a check-then-act: a concurrent caller can commit this same name
            // between it and this write, and ux_client_client_name then rejects ours. The losing writer
            // takes the same path as the sequential duplicate rather than escaping as a 500.
            // compass.client also carries ux_client_legacy_tps_id, which nothing pre-checks, so the
            // violated index decides the message rather than the check we happened to run.
            return CompassLegacyProvenance.IsProvenanceCollision(exception)
                ? DuplicateProvenance(request.LegacyTpsId)
                : DuplicateName(request.ClientName);
        }

        await LogAsync(
            client.Id,
            "create",
            CompassAuditReason.Created(AuditSubject),
            CreationChanges(client)
        );

        return CompassWrite<ClientDto>.Succeeded(
            ToDto(client, await DeriveStatusAsync(client.Id, cancellationToken))
        );
    }

    /// <inheritdoc />
    public Task<CompassWrite<ClientDto>> UpdateClientAsync(
        int id,
        CompassClientRequest request,
        CancellationToken cancellationToken
    ) =>
        unitOfWork.ExecuteAtomicallyAsync(
            ct => UpdateClientAsyncCoreAsync(id, request, ct),
            result => result.Status == CompassWriteStatus.Success,
            cancellationToken);

    private async Task<CompassWrite<ClientDto>> UpdateClientAsyncCoreAsync(
        int id,
        CompassClientRequest request,
        CancellationToken cancellationToken
    )
    {
        var client = await clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
        {
            return CompassWrite<ClientDto>.NotFound();
        }

        var validation = await ValidateAsync(request, client, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var changes = UpdateChanges(client, request);

        client.ClientName = request.ClientName.Trim();
        client.MsaSignedDate = request.MsaSignedDate;
        client.NdaSignedDate = request.NdaSignedDate;
        client.IsInternal = request.IsInternal;
        client.InvoiceFrequencyTypeId = request.InvoiceFrequencyTypeId;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException exception)
        {
            // Same race as the create path, and the same two indexes — see the create path's remark.
            return CompassLegacyProvenance.IsProvenanceCollision(exception)
                ? DuplicateProvenance(request.LegacyTpsId)
                : DuplicateName(request.ClientName);
        }

        await LogAsync(client.Id, "update", CompassAuditReason.Updated(AuditSubject), changes);

        return CompassWrite<ClientDto>.Succeeded(
            ToDto(client, await DeriveStatusAsync(client.Id, cancellationToken))
        );
    }

    /// <summary>
    /// The client's status as of now.
    /// </summary>
    /// <remarks>
    /// Called after a write so the response reports the same value a subsequent read would. No write on
    /// this surface can CHANGE it — status comes from assignments, which none of these paths touch
    /// (FR-039) — but returning a record with a status invented by the write path would be a second
    /// derivation, and this asks the shared one instead.
    /// </remarks>
    /// <remarks>
    /// Returns the string the DTOs carry, never the enum: no production member may HOLD a
    /// <c>ClientStatus</c>, and a method taking or returning one is caught by the same fence
    /// (<c>ClientStatusNonGatingTests</c>). The enum exists inside the derivation; everything outward
    /// of it speaks the published wire value.
    /// </remarks>
    private async Task<string> DeriveStatusAsync(
        int clientId,
        CancellationToken cancellationToken
    )
    {
        var facts = await clients.GetStatusFactsAsync(
            clientId,
            businessDate.Today(),
            cancellationToken
        );

        return statusDerivation
            .StatusOfClient(facts.HoldsCurrentAssignment, facts.HasEverBeenAssigned)
            .ToString();
    }

    // category writes

    /// <inheritdoc />
    public Task<CompassWrite<BillableTimeCategoryDto>> AddCategoryAsync(
        int clientId,
        CreateBillableTimeCategoryRequest request,
        CancellationToken cancellationToken
    ) =>
        unitOfWork.ExecuteAtomicallyAsync(
            ct => AddCategoryAsyncCoreAsync(clientId, request, ct),
            result => result.Status == CompassWriteStatus.Success,
            cancellationToken);

    private async Task<CompassWrite<BillableTimeCategoryDto>> AddCategoryAsyncCoreAsync(
        int clientId,
        CreateBillableTimeCategoryRequest request,
        CancellationToken cancellationToken
    )
    {
        var client = await clients.GetByIdAsync(clientId, cancellationToken);
        if (client is null)
        {
            return CompassWrite<BillableTimeCategoryDto>.NotFound();
        }

        var name = request.CategoryName?.Trim() ?? string.Empty;
        var invalid = ValidateCategoryName(name);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await clients.CategoryNameExistsAsync(clientId, name, null, cancellationToken))
        {
            return DuplicateCategoryName(name);
        }

        var category = new BillableTimeCategory
        {
            ClientId = clientId,
            CategoryName = name,
            IsActive = true,
            LegacyTpsId = CompassLegacyProvenance.Normalise(request.LegacyTpsId),
        };

        await clients.AddCategoryAsync(category, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException exception)
        {
            // compass.billable_time_category carries ux_billable_time_category_legacy_tps_id alongside
            // the composite name index. The UPDATE path needs no such branch:
            // UpdateBillableTimeCategoryRequest carries no LegacyTpsId, so its save cannot violate it.
            return CompassLegacyProvenance.IsProvenanceCollision(exception)
                ? DuplicateCategoryProvenance(request.LegacyTpsId)
                : DuplicateCategoryName(name);
        }

        await LogCategoryChangeAsync(
            clientId,
            [new FieldChange($"BillableTimeCategory:{name}", null, "added")]
        );

        return CompassWrite<BillableTimeCategoryDto>.Succeeded(ToDto(category));
    }

    /// <inheritdoc />
    public Task<CompassWrite<BillableTimeCategoryDto>> UpdateCategoryAsync(
        int clientId,
        int categoryId,
        UpdateBillableTimeCategoryRequest request,
        CancellationToken cancellationToken
    ) =>
        unitOfWork.ExecuteAtomicallyAsync(
            ct => UpdateCategoryAsyncCoreAsync(clientId, categoryId, request, ct),
            result => result.Status == CompassWriteStatus.Success,
            cancellationToken);

    private async Task<CompassWrite<BillableTimeCategoryDto>> UpdateCategoryAsyncCoreAsync(
        int clientId,
        int categoryId,
        UpdateBillableTimeCategoryRequest request,
        CancellationToken cancellationToken
    )
    {
        // The client id is part of the lookup, so a category belonging to someone else is not found
        // rather than editable — see ICompassClientRepository.GetCategoryAsync.
        var category = await clients.GetCategoryAsync(clientId, categoryId, cancellationToken);
        if (category is null)
        {
            return CompassWrite<BillableTimeCategoryDto>.NotFound();
        }

        var name = request.CategoryName?.Trim() ?? string.Empty;
        var invalid = ValidateCategoryName(name);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await clients.CategoryNameExistsAsync(clientId, name, categoryId, cancellationToken))
        {
            return DuplicateCategoryName(name);
        }

        var changes = CategoryUpdateChanges(category, name, request.IsActive);

        category.CategoryName = name;
        category.IsActive = request.IsActive;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException)
        {
            return DuplicateCategoryName(name);
        }

        await LogCategoryChangeAsync(clientId, changes);

        return CompassWrite<BillableTimeCategoryDto>.Succeeded(ToDto(category));
    }

    // validation

    /// <summary>
    /// Every rule that can refuse a client write, in the order that produces the most useful message.
    /// </summary>
    /// <param name="request">The submitted configuration.</param>
    /// <param name="existing">
    /// The stored record on an update, or null on a create. It is what lets an edit keep an
    /// invoice-frequency default that was retired after the client was set to it (FR-007) — see the
    /// cadence rule.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rejection, or null when the request is acceptable.</returns>
    private async Task<CompassWrite<ClientDto>?> ValidateAsync(
        CompassClientRequest request,
        Client? existing,
        CancellationToken cancellationToken
    )
    {
        var name = request.ClientName?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return CompassWrite<ClientDto>.Invalid("A client name is required.");
        }

        if (name.Length > MaxClientNameLength)
        {
            return CompassWrite<ClientDto>.Invalid(
                $"A client name may be at most {MaxClientNameLength} characters."
            );
        }

        // FR-023: only ACTIVE invoice frequency types may be chosen — the server refuses an inactive id
        // even though the selection list omits it. The exception is an edit that LEAVES an already-set
        // default alone: FR-007 says retiring a value must not rewrite the records using it, so an edit
        // to some other field must not be forced to re-select a now-retired cadence.
        if (request.InvoiceFrequencyTypeId is { } frequencyTypeId)
        {
            var unchanged = existing is not null && existing.InvoiceFrequencyTypeId == frequencyTypeId;
            if (
                !unchanged
                && !await clients.ActiveInvoiceFrequencyTypeExistsAsync(
                    frequencyTypeId,
                    cancellationToken
                )
            )
            {
                return CompassWrite<ClientDto>.Invalid(
                    "That invoice frequency does not exist or is no longer selectable."
                );
            }
        }

        if (await clients.ClientNameExistsAsync(name, existing?.Id, cancellationToken))
        {
            return DuplicateName(name);
        }

        return null;
    }

    private static CompassWrite<BillableTimeCategoryDto>? ValidateCategoryName(string name)
    {
        if (name.Length == 0)
        {
            return CompassWrite<BillableTimeCategoryDto>.Invalid("A category name is required.");
        }

        return name.Length > MaxCategoryNameLength
            ? CompassWrite<BillableTimeCategoryDto>.Invalid(
                $"A category name may be at most {MaxCategoryNameLength} characters."
            )
            : null;
    }

    /// <summary>
    /// The rejection for a name already in use.
    /// </summary>
    /// <remarks>
    /// Shared by the pre-check and the lost-race path deliberately: a caller who loses a race must get
    /// the same answer as one who was simply second, or the outcome would depend on timing they cannot
    /// see. It names the field, so the message is actionable.
    /// </remarks>
    private static CompassWrite<ClientDto> DuplicateProvenance(string? legacyTpsId) =>
        CompassWrite<ClientDto>.Duplicate(
            CompassLegacyProvenance.CollisionMessage(legacyTpsId, "client")
        );

    private static CompassWrite<BillableTimeCategoryDto> DuplicateCategoryProvenance(
        string? legacyTpsId
    ) =>
        CompassWrite<BillableTimeCategoryDto>.Duplicate(
            CompassLegacyProvenance.CollisionMessage(legacyTpsId, "billable time category")
        );

    private static CompassWrite<ClientDto> DuplicateName(string? clientName) =>
        CompassWrite<ClientDto>.Duplicate(
            $"The client name '{clientName?.Trim()}' is already in use."
        );

    private static CompassWrite<BillableTimeCategoryDto> DuplicateCategoryName(string categoryName) =>
        CompassWrite<BillableTimeCategoryDto>.Duplicate(
            $"This client already offers a category named '{categoryName}'."
        );

    // audit

    private string Actor => currentUser.EdjeId.ToString();

    /// <summary>
    /// The actor's whole privilege set.
    /// </summary>
    /// <remarks>
    /// Unfiltered, deliberately — not narrowed to a <c>Compass </c> prefix (owner decision, restated in
    /// <see cref="AuditEntry"/>'s remarks). What made a write possible may include a role from outside
    /// this module, and an audit trail that hides that answers a different question than the one an
    /// auditor asked.
    /// </remarks>
    private List<string> EffectiveRoles => [.. currentUser.Privileges];

    /// <summary>
    /// Writes one audit entry for this surface, supplying the four fields every client entry records
    /// identically: the entity type, the actor, who the change came through, and the effective roles.
    /// </summary>
    /// <remarks>
    /// The point is that <see cref="EffectiveRoles"/> cannot be forgotten. It is an optional
    /// parameter on <see cref="AuditEntry"/>, so a hand-built entry that omits it compiles and logs
    /// perfectly happily while silently failing FR-017. Routing every write through here makes the
    /// omission unrepresentable rather than merely discouraged.
    /// </remarks>
    /// <param name="entityId">The client the entry is about.</param>
    /// <param name="action">The audit action, <c>create</c> or <c>update</c>.</param>
    /// <param name="reason">A non-blank reason, composed by <see cref="CompassAuditReason"/>.</param>
    /// <param name="changes">The field-level changes the entry records.</param>
    private Task LogAsync(
        int entityId,
        string action,
        string reason,
        List<FieldChange> changes
    ) =>
        auditService.LogAsync(
            new AuditEntry(
                AuditEntityType,
                entityId.ToString(),
                action,
                Actor,
                AuditTriggeredBy,
                reason,
                changes,
                EffectiveRoles: EffectiveRoles
            )
        );

    /// <summary>
    /// Records a category change against its owning CLIENT (AC-NFR-3).
    /// </summary>
    /// <remarks>
    /// The action is <c>update</c> rather than <c>create</c> even when a category is added, because what
    /// changed is the client — the client was updated to offer a new category. An entry saying a client
    /// was "created" twice would be wrong on its face. That is why the action is fixed here rather than
    /// taken as a parameter: both call sites passed <c>"update"</c>, and a parameter invited the one
    /// value this remark exists to rule out.
    /// </remarks>
    private Task LogCategoryChangeAsync(int clientId, List<FieldChange> changes) =>
        LogAsync(clientId, "update", CompassAuditReason.Updated(AuditSubject), changes);

    /// <summary>Every field of a new client, as a creation has no "before".</summary>
    private static List<FieldChange> CreationChanges(Client client) =>
        [
            new(nameof(Client.ClientName), null, client.ClientName),
            new(nameof(Client.MsaSignedDate), null, client.MsaSignedDate?.ToString("O")),
            new(nameof(Client.NdaSignedDate), null, client.NdaSignedDate?.ToString("O")),
            new(nameof(Client.IsInternal), null, client.IsInternal.ToString()),
            new(
                nameof(Client.InvoiceFrequencyTypeId),
                null,
                client.InvoiceFrequencyTypeId?.ToString()
            ),
        ];

    /// <summary>
    /// Only the fields an update actually changes.
    /// </summary>
    /// <remarks>
    /// Computed BEFORE the entity is mutated, because afterwards there is no "before" left to read.
    /// Unchanged fields are omitted rather than recorded as no-ops: a change list where four of five
    /// entries say nothing happened makes the one that did harder to find.
    /// </remarks>
    private static List<FieldChange> UpdateChanges(Client before, CompassClientRequest after)
    {
        List<FieldChange> changes = [];

        Compare(nameof(Client.ClientName), before.ClientName, after.ClientName?.Trim());
        Compare(
            nameof(Client.MsaSignedDate),
            before.MsaSignedDate?.ToString("O"),
            after.MsaSignedDate?.ToString("O")
        );
        Compare(
            nameof(Client.NdaSignedDate),
            before.NdaSignedDate?.ToString("O"),
            after.NdaSignedDate?.ToString("O")
        );
        Compare(
            nameof(Client.IsInternal),
            before.IsInternal.ToString(),
            after.IsInternal.ToString()
        );
        Compare(
            nameof(Client.InvoiceFrequencyTypeId),
            before.InvoiceFrequencyTypeId?.ToString(),
            after.InvoiceFrequencyTypeId?.ToString()
        );

        return changes;

        void Compare(string field, string? previous, string? next)
        {
            if (!string.Equals(previous, next, StringComparison.Ordinal))
            {
                changes.Add(new FieldChange(field, previous, next));
            }
        }
    }

    /// <summary>What changed about a category, named so the client's trail stays readable.</summary>
    /// <remarks>
    /// The field names are prefixed with the category rather than being bare <c>CategoryName</c> /
    /// <c>IsActive</c>, because these entries sit among the client's own field changes — an unqualified
    /// <c>IsActive</c> in a client's trail would read as though the CLIENT had been deactivated, which
    /// is the one thing that can never happen (FR-021).
    /// </remarks>
    private static List<FieldChange> CategoryUpdateChanges(
        BillableTimeCategory before,
        string name,
        bool isActive
    )
    {
        List<FieldChange> changes = [];
        var subject = $"BillableTimeCategory:{before.CategoryName}";

        if (!string.Equals(before.CategoryName, name, StringComparison.Ordinal))
        {
            changes.Add(
                new FieldChange(
                    $"{subject}.{nameof(BillableTimeCategory.CategoryName)}",
                    before.CategoryName,
                    name
                )
            );
        }

        if (before.IsActive != isActive)
        {
            changes.Add(
                new FieldChange(
                    $"{subject}.{nameof(BillableTimeCategory.IsActive)}",
                    before.IsActive.ToString(),
                    isActive.ToString()
                )
            );
        }

        return changes;
    }

    // projection

    /// <summary>
    /// Projects a client, with its status supplied by the caller.
    /// </summary>
    /// <remarks>
    /// Status is a PARAMETER rather than something this method derives, because deriving it needs a
    /// database round trip and this projection is also used from write paths that have already made
    /// one. Threading it in keeps the single-derivation rule intact without a hidden query per call.
    /// It is the published string, not the enum — see <see cref="DeriveStatusAsync"/>.
    /// </remarks>
    private static ClientDto ToDto(Client client, string status) =>
        new(
            client.Id,
            client.ClientName,
            client.MsaSignedDate,
            client.NdaSignedDate,
            client.IsInternal,
            client.InvoiceFrequencyTypeId,
            [.. client.BillableTimeCategories.OrderBy(category => category.Id).Select(ToDto)],
            status
        );

    private static BillableTimeCategoryDto ToDto(BillableTimeCategory category) =>
        new(category.Id, category.CategoryName, category.IsActive);
}
