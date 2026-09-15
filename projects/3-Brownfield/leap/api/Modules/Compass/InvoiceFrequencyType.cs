using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// Super Admin-managed lookup of invoice frequencies. ERD table <c>compass.invoice_frequency_type</c>.
/// </summary>
public class InvoiceFrequencyType : ICompassLookup
{
    /// <summary>Primary key. Mapped to the ERD column <c>invoice_frequency_type_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>Unique frequency name, e.g. "Monthly".</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Whether this frequency may be selected when configuring a client.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Clients defaulting to this invoicing cadence.</summary>
    public ICollection<Client> Clients { get; set; } = [];
}
