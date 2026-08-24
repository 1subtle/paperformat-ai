using PaperFormat.Domain;

namespace PaperFormat.Classification;

/// <summary>
/// Classifies a parsed Word document without changing document content.
/// </summary>
public interface IDocumentClassifier
{
    ClassificationSet Classify(DocumentModel document);
}
