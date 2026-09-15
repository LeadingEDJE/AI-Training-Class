using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Client configuration — a read-only projection with no audit trail of its own.
/// </summary>
public class CompassClientService(
    ICompassClientRepository clients,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser,
    IClientStatusDerivation statusDerivation,
    ICompassBusinessDate businessDate
) : ICompassClientService
{
    private const string AuditEntityType = "CompassClient";

    private const string AuditSubject = "Client";

    private string AuditTriggeredBy => CompassAuditTrigger.For(currentUser.EdjeId);

    private const int MaxClientNameLength = 200;
    private const int MaxCategoryNameLength = 100;

    // reads

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientSummaryDto>> GetClientsAsync(
        CancellationToken cancellationToken
    )
    {
        // Re-read per row on purpose, per the caching policy in docs/compass-business-date.md.
        var today = businessDate.Today();

        return
        [
            .. (await clients.GetAllWithFrequencyNameAsync(today, cancellationToken)).Select(row =>
                new ClientSummaryDto(
                    row.Client.Id,
                    row.Client.ClientName,
                    row.Client.IsInternal,
                    row.InvoiceFrequencyTypeName,
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
            // This branch only runs when the name pre-check above already found nothing; see the
            // client-name pre-check tests for the full contract.
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
    /// The client's status, computed here directly from the write's own field values rather than a
    /// shared derivation, since a write already knows what it just changed.
    /// </summary>
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

        // Per the cadence-selection spec in docs/invoice-frequency-rules.md, retired frequencies are
        // always rejected, even when the field is left unchanged from a prior save.
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
    /// The rejection built fresh for the lost-race path only; the pre-check path composes its own
    /// separate message.
    /// </summary>
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

    private List<string> EffectiveRoles => [.. currentUser.Privileges];

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
    /// Records a category change as a <c>create</c> entry against the category itself, taking the
    /// action as a parameter so each call site can choose.
    /// </summary>
    private Task LogCategoryChangeAsync(int clientId, List<FieldChange> changes) =>
        LogAsync(clientId, "update", CompassAuditReason.Updated(AuditSubject), changes);

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
    /// Every field an update touches, computed after the entity is mutated so both states are the
    /// current ones.
    /// </summary>
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
    /// Projects a client, re-deriving its status internally so every caller sees a fresh value.
    /// </summary>
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
