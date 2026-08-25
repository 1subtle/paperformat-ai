using System.Text.Json.Serialization;

namespace PaperFormat.Domain;

/// <summary>
/// Severity of a parser diagnostic.
/// </summary>
public enum ParseDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// A structured parser diagnostic. Messages must not contain manuscript text.
/// </summary>
public sealed record ParseDiagnostic
{
    public ParseDiagnostic(
        string code,
        ParseDiagnosticSeverity severity,
        string message,
        StructuralLocation? location = null)
    {
        Code = DomainGuard.RequiredIdentifier(code, nameof(code));

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A diagnostic message cannot be blank.",
                nameof(message));
        }

        Severity = severity;
        Message = message;
        Location = location;
    }

    public string Code { get; }

    public ParseDiagnosticSeverity Severity { get; }

    public string Message { get; }

    public StructuralLocation? Location { get; }
}

/// <summary>
/// The immutable outcome of attempting to parse a Word package.
/// </summary>
public sealed record DocumentParseResult
{
    public DocumentParseResult(
        DocumentModel? document,
        IEnumerable<ParseDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = new ValueList<ParseDiagnostic>(
            diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
    }

    public DocumentModel? Document { get; }

    public ValueList<ParseDiagnostic> Diagnostics { get; }

    [JsonIgnore]
    public bool IsSuccess =>
        Document is not null
        && !Diagnostics.Any(
            diagnostic => diagnostic.Severity == ParseDiagnosticSeverity.Error);

    public static DocumentParseResult Success(
        DocumentModel document,
        IEnumerable<ParseDiagnostic>? diagnostics = null) =>
        new(
            document ?? throw new ArgumentNullException(nameof(document)),
            diagnostics ?? Array.Empty<ParseDiagnostic>());

    public static DocumentParseResult Failure(
        IEnumerable<ParseDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var snapshot = new ValueList<ParseDiagnostic>(diagnostics);

        if (!snapshot.Any(
                diagnostic =>
                    diagnostic.Severity == ParseDiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "A failed parse result requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        return new DocumentParseResult(null, snapshot);
    }
}
