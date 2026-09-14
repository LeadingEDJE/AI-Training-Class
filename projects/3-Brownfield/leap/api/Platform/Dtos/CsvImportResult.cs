namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>Result summary returned by admin CSV import endpoints — counts, and per-row error details for any rows that failed validation.</summary>
public class CsvImportResult
{
    /// <summary>Total number of data rows found in the uploaded CSV (excluding the header).</summary>
    public int TotalRows { get; set; }

    /// <summary>Number of rows that were successfully persisted.</summary>
    public int CreatedCount { get; set; }

    /// <summary>Number of rows that were skipped (e.g., duplicate or invalid) without persisting.</summary>
    public int SkippedCount { get; set; }

    /// <summary>Detailed per-row errors for anything that failed validation.</summary>
    public List<CsvImportError> Errors { get; set; } = [];
}

/// <summary>Single per-row error entry for a CSV import result.</summary>
public class CsvImportError
{
    /// <summary>1-based row number in the uploaded CSV (excluding the header).</summary>
    public int Row { get; set; }

    /// <summary>Human-readable description of why the row failed.</summary>
    public string Message { get; set; } = string.Empty;
}
