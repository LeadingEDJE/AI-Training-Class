namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>One rendered export, serialised straight to JSON by the endpoint that returns it.</summary>
/// <param name="FileName">The download filename, extension included.</param>
/// <param name="ContentType">The media type — <c>text/csv</c> or <c>application/zip</c>.</param>
/// <param name="Content">The payload. CSV content carries its UTF-8 BOM already.</param>
public sealed record CompassExportFile(string FileName, string ContentType, byte[] Content);
