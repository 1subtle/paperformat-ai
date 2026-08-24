using PaperFormat.Domain;

namespace PaperFormat.Classification;

/// <summary>
/// Applies explicit user confirmations to an immutable classification set.
/// </summary>
public static class ClassificationEditor
{
    public static ClassificationSet Apply(
        ClassificationSet source,
        IEnumerable<ClassificationOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(overrides);

        ClassificationOverride[] edits = overrides.ToArray();
        string? duplicateId = edits
            .GroupBy(edit => edit.ElementId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                "An element can be overridden at most once per revision.",
                nameof(overrides));
        }

        var editsById = edits.ToDictionary(
            edit => edit.ElementId,
            StringComparer.Ordinal);
        string? unknownId = editsById.Keys.FirstOrDefault(
            elementId => source.Elements.All(
                element => !string.Equals(
                    element.ElementId,
                    elementId,
                    StringComparison.Ordinal)));
        if (unknownId is not null)
        {
            throw new ArgumentException(
                $"The element '{unknownId}' does not exist.",
                nameof(overrides));
        }

        DocumentElement[] elements = source.Elements
            .Select(
                element => editsById.TryGetValue(
                    element.ElementId,
                    out ClassificationOverride? edit)
                    ? Apply(element, edit)
                    : element)
            .ToArray();
        return new ClassificationSet(source.Revision + 1, elements);
    }

    private static DocumentElement Apply(
        DocumentElement element,
        ClassificationOverride edit) =>
        new(
            element.ElementId,
            element.Location,
            edit.Kind,
            confidence: 1m,
            ClassificationStatus.UserConfirmed,
            [
                new ClassificationReason(
                    "classification.user.override",
                    ClassificationEvidenceKind.UserOverride,
                    weight: 1m,
                    "The user explicitly confirmed this element type."),
            ],
            element.TextLength,
            element.SourceStyleId);
}
