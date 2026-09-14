namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>One rendered export, ready to send.</summary>
/// <remarks>
/// A service result rather than a wire DTO, which is why it lives beside the service instead of in
/// <c>Dtos/Read/</c>: it is never serialised as JSON. The endpoint turns it into a file response, and
/// keeping the filename with the bytes stops the two being decided in different places — a zip entry
/// and a download both need the same name.
/// </remarks>
/// <param name="FileName">The download filename, extension included.</param>
/// <param name="ContentType">The media type — <c>text/csv</c> or <c>application/zip</c>.</param>
/// <param name="Content">The payload. CSV content carries its UTF-8 BOM already.</param>
public sealed record CompassExportFile(string FileName, string ContentType, byte[] Content);
