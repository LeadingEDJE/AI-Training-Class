using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Administration of the two Compass lookups — employee types and invoice frequency types.
/// </summary>
/// <remarks>
/// The rules are identical for both lookups, so they are written once in the private generic helpers
/// and closed over each entity and DTO; six public operations over two entities would otherwise repeat
/// the same validation and collision logic six times. These writes are not audited, per AC-NFR-3 and
/// FR-008 — a deliberate asymmetry with every other Compass configuration write, and this service
/// cannot reach the audit service at all, which
/// <c>CompassLookupServiceTests.TheLookupService_CannotReachTheAuditService</c> enforces. There is no
/// delete: retiring a value clears its active flag and leaves referencing records untouched (FR-007).
/// </remarks>
public class CompassLookupService(
    ICompassLookupRepository<EmployeeType> employeeTypes,
    ICompassLookupRepository<InvoiceFrequencyType> invoiceFrequencyTypes,
    ICompassUnitOfWork unitOfWork
) : ICompassLookupService
{
    /// <summary>
    /// The <c>type_name</c> column's width. Refusing an over-long name here turns what would surface as
    /// a database error and a 500 into a 400 that names the field.
    /// </summary>
    private const int MaxTypeNameLength = 50;

    // employee types

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmployeeTypeDto>> GetEmployeeTypesAsync(
        bool activeOnly,
        CancellationToken cancellationToken
    ) =>
        [
            .. (await employeeTypes.GetAllAsync(activeOnly, cancellationToken)).Select(ToDto),
        ];

    /// <inheritdoc />
    public Task<CompassLookupWrite<EmployeeTypeDto>> CreateEmployeeTypeAsync(
        string typeName,
        CancellationToken cancellationToken
    ) => CreateAsync(employeeTypes, typeName, ToDto, cancellationToken);

    /// <inheritdoc />
    public Task<CompassLookupWrite<EmployeeTypeDto>> UpdateEmployeeTypeAsync(
        int id,
        string typeName,
        bool isActive,
        CancellationToken cancellationToken
    ) => UpdateAsync(employeeTypes, id, typeName, isActive, ToDto, cancellationToken);

    // invoice frequency types

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvoiceFrequencyTypeDto>> GetInvoiceFrequencyTypesAsync(
        bool activeOnly,
        CancellationToken cancellationToken
    ) =>
        [
            .. (await invoiceFrequencyTypes.GetAllAsync(activeOnly, cancellationToken)).Select(ToDto),
        ];

    /// <inheritdoc />
    public Task<CompassLookupWrite<InvoiceFrequencyTypeDto>> CreateInvoiceFrequencyTypeAsync(
        string typeName,
        CancellationToken cancellationToken
    ) => CreateAsync(invoiceFrequencyTypes, typeName, ToDto, cancellationToken);

    /// <inheritdoc />
    public Task<CompassLookupWrite<InvoiceFrequencyTypeDto>> UpdateInvoiceFrequencyTypeAsync(
        int id,
        string typeName,
        bool isActive,
        CancellationToken cancellationToken
    ) => UpdateAsync(invoiceFrequencyTypes, id, typeName, isActive, ToDto, cancellationToken);

    // the shared rules

    private async Task<CompassLookupWrite<TDto>> CreateAsync<TLookup, TDto>(
        ICompassLookupRepository<TLookup> repository,
        string typeName,
        Func<TLookup, TDto> toDto,
        CancellationToken cancellationToken
    )
        where TLookup : class, ICompassLookup, new()
        where TDto : class
    {
        if (Validate<TDto>(typeName, out var name, out var invalid))
        {
            return invalid!;
        }

        if (await repository.NameExistsAsync(name, excludingId: null, cancellationToken))
        {
            return DuplicateNamed<TDto>(name);
        }

        // A new value is immediately selectable — no acceptance criterion asks for creating one that
        // cannot be chosen.
        var lookup = new TLookup { TypeName = name, IsActive = true };
        await repository.AddAsync(lookup, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException)
        {
            // The check above is a check-then-act: a concurrent caller can commit this same name
            // between it and this write, and the unique index then rejects ours. The losing writer takes
            // the SAME path as the sequential duplicate — a 409 naming the collision — rather than
            // escaping as an unhandled failure and a bare 500.
            return DuplicateNamed<TDto>(name);
        }

        return CompassLookupWrite<TDto>.Succeeded(toDto(lookup));
    }

    private async Task<CompassLookupWrite<TDto>> UpdateAsync<TLookup, TDto>(
        ICompassLookupRepository<TLookup> repository,
        int id,
        string typeName,
        bool isActive,
        Func<TLookup, TDto> toDto,
        CancellationToken cancellationToken
    )
        where TLookup : class, ICompassLookup
        where TDto : class
    {
        if (Validate<TDto>(typeName, out var name, out var invalid))
        {
            return invalid!;
        }

        var lookup = await repository.GetByIdAsync(id, cancellationToken);
        if (lookup is null)
        {
            return CompassLookupWrite<TDto>.NotFound();
        }

        // Excluding the row being edited matters: without it, retiring a value without also renaming
        // it would collide with itself and be impossible.
        if (await repository.NameExistsAsync(name, excludingId: id, cancellationToken))
        {
            return DuplicateNamed<TDto>(name);
        }

        lookup.TypeName = name;
        lookup.IsActive = isActive;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException)
        {
            // Same race as the create path: two administrators renaming different values to the same
            // name both pass the check, and the loser is rejected by the index.
            return DuplicateNamed<TDto>(name);
        }

        return CompassLookupWrite<TDto>.Succeeded(toDto(lookup));
    }

    /// <summary>
    /// The rejection for a name already in use.
    /// </summary>
    /// <remarks>
    /// Shared by the pre-check and the lost-race path deliberately: a caller who loses the race must
    /// receive the same answer as one who was simply second, or the outcome would depend on timing the
    /// caller cannot see.
    /// </remarks>
    private static CompassLookupWrite<TDto> DuplicateNamed<TDto>(string name)
        where TDto : class => CompassLookupWrite<TDto>.Duplicate($"A value named '{name}' already exists.");

    /// <summary>
    /// Normalises the name and reports whether it is unusable. Returns true when the caller should
    /// return <paramref name="invalid"/> immediately.
    /// </summary>
    /// <remarks>
    /// Trimming is not cosmetic: without it " Contract" and "Contract" coexist as separate values that
    /// read as duplicates to every human looking at the list.
    /// </remarks>
    private static bool Validate<TDto>(
        string typeName,
        out string name,
        out CompassLookupWrite<TDto>? invalid
    )
        where TDto : class
    {
        name = (typeName ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            invalid = CompassLookupWrite<TDto>.Invalid("A name is required.");
            return true;
        }

        if (name.Length > MaxTypeNameLength)
        {
            invalid = CompassLookupWrite<TDto>.Invalid(
                $"A name may be at most {MaxTypeNameLength} characters."
            );
            return true;
        }

        invalid = null;
        return false;
    }

    private static EmployeeTypeDto ToDto(EmployeeType lookup) =>
        new(lookup.Id, lookup.TypeName, lookup.IsActive);

    private static InvoiceFrequencyTypeDto ToDto(InvoiceFrequencyType lookup) =>
        new(lookup.Id, lookup.TypeName, lookup.IsActive);
}
