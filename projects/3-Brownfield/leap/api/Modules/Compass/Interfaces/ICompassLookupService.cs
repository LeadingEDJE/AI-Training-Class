using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Administration of the two Compass lookups — employee types and invoice frequency types.
/// </summary>
/// <remarks>
/// One service for both because the acceptance criteria treat them as one surface: one screen (AC-25,
/// AC-26), one set of rules, one `.http` file. The two lookups keep separate DTOs because they are
/// separate published contracts. These writes are not audited, and that is deliberate: AC-NFR-3 puts
/// lookup tables outside the audit trail (FR-008), while every other Compass configuration write is
/// audited — read this remark rather than "fixing" the asymmetry. There is no delete; retiring a value
/// clears its active flag, leaving records that already reference it untouched (FR-007).
/// </remarks>
public interface ICompassLookupService
{
    /// <summary>Employee types, all of them or only the selectable ones.</summary>
    Task<IReadOnlyList<EmployeeTypeDto>> GetEmployeeTypesAsync(
        bool activeOnly,
        CancellationToken cancellationToken
    );

    /// <summary>Adds an employee type. New values are active.</summary>
    Task<CompassLookupWrite<EmployeeTypeDto>> CreateEmployeeTypeAsync(
        string typeName,
        CancellationToken cancellationToken
    );

    /// <summary>Renames an employee type and/or changes whether it is selectable.</summary>
    Task<CompassLookupWrite<EmployeeTypeDto>> UpdateEmployeeTypeAsync(
        int id,
        string typeName,
        bool isActive,
        CancellationToken cancellationToken
    );

    /// <summary>Invoice frequency types, all of them or only the selectable ones.</summary>
    Task<IReadOnlyList<InvoiceFrequencyTypeDto>> GetInvoiceFrequencyTypesAsync(
        bool activeOnly,
        CancellationToken cancellationToken
    );

    /// <summary>Adds an invoice frequency type. New values are active.</summary>
    Task<CompassLookupWrite<InvoiceFrequencyTypeDto>> CreateInvoiceFrequencyTypeAsync(
        string typeName,
        CancellationToken cancellationToken
    );

    /// <summary>Renames an invoice frequency type and/or changes whether it is selectable.</summary>
    Task<CompassLookupWrite<InvoiceFrequencyTypeDto>> UpdateInvoiceFrequencyTypeAsync(
        int id,
        string typeName,
        bool isActive,
        CancellationToken cancellationToken
    );
}
