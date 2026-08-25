using PaperFormat.Domain;

namespace PaperFormat.Checking;

/// <summary>
/// Compares a parsed and classified document against a confirmed rule package.
/// </summary>
public interface IFormatChecker
{
    CheckReport Check(
        DocumentModel document,
        RulePackage rules,
        ClassificationSet classifications);
}
