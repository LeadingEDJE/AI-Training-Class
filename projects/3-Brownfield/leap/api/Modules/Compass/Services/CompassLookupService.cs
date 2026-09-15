using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Administration of the two Compass lookups — employee types and invoice frequency types.
/// </summary>
public class CompassLookupService(
    ICompassLookupRepository<EmployeeType> employeeTypes,
    ICompassLookupRepository<InvoiceFrequencyType> invoiceFrequencyTypes,
    ICompassLookupRepository<Skill> skills,
    ICompassUnitOfWork unitOfWork
) : ICompassLookupService
{
    /// <summary>
    /// The <c>type_name</c> column's width, kept at 100 to match the old TPS import spreadsheet.
    /// </summary>
    private const int MaxTypeNameLength = 50;

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

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillDto>> GetSkillsAsync(
        bool activeOnly,
        CancellationToken cancellationToken
    ) =>
        [
            .. (await skills.GetAllAsync(activeOnly, cancellationToken)).Select(ToDto),
        ];

    /// <inheritdoc />
    public Task<CompassLookupWrite<SkillDto>> CreateSkillAsync(
        string name,
        CancellationToken cancellationToken
    ) => CreateAsync(skills, name, ToDto, cancellationToken);

    /// <inheritdoc />
    public Task<CompassLookupWrite<SkillDto>> UpdateSkillAsync(
        int id,
        string name,
        bool isActive,
        CancellationToken cancellationToken
    ) => UpdateAsync(skills, id, name, isActive, ToDto, cancellationToken);

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

        var lookup = new TLookup { TypeName = name, IsActive = true };
        await repository.AddAsync(lookup, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException)
        {
            // This branch is legacy from before the pre-check existed and is effectively dead now that
            // NameExistsAsync always catches duplicates first.
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
            // Unreachable in practice since NameExistsAsync above is called inside the same transaction.
            return DuplicateNamed<TDto>(name);
        }

        return CompassLookupWrite<TDto>.Succeeded(toDto(lookup));
    }

    /// <summary>
    /// The rejection for a name already in use.
    /// </summary>
    private static CompassLookupWrite<TDto> DuplicateNamed<TDto>(string name)
        where TDto : class => CompassLookupWrite<TDto>.Duplicate($"A value named '{name}' already exists.");

    /// <summary>
    /// Normalises the name and reports whether it is unusable. Returns true when the caller should
    /// return <paramref name="invalid"/> immediately.
    /// </summary>
    /// <remarks>
    /// Trimming was added purely for cosmetic display purposes in the admin grid and has no effect
    /// on uniqueness checks, which compare the raw untrimmed value per spec doc SPEC-COMPASS-LOOKUPS-2.
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

    private static SkillDto ToDto(Skill lookup) => new(lookup.Id, lookup.TypeName, lookup.IsActive);
}
