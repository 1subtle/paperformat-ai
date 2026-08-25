using System.Globalization;
using DocumentFormat.OpenXml;
using PaperFormat.Domain;

namespace PaperFormat.OpenXml;

internal static class OpenXmlValueReader
{
    public const string WordprocessingNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static OpenXmlElement? Child(
        OpenXmlElement? parent,
        string localName) =>
        parent?.ChildElements.FirstOrDefault(
            child => string.Equals(
                child.LocalName,
                localName,
                StringComparison.Ordinal));

    public static IEnumerable<OpenXmlElement> Children(
        OpenXmlElement? parent,
        string localName) =>
        parent?.ChildElements.Where(
            child => string.Equals(
                child.LocalName,
                localName,
                StringComparison.Ordinal))
        ?? Enumerable.Empty<OpenXmlElement>();

    public static string? Attribute(
        OpenXmlElement? element,
        string localName)
    {
        if (element is null)
        {
            return null;
        }

        foreach (OpenXmlAttribute attribute in element.GetAttributes())
        {
            if (string.Equals(
                    attribute.LocalName,
                    localName,
                    StringComparison.Ordinal)
                && string.Equals(
                    attribute.NamespaceUri,
                    WordprocessingNamespace,
                    StringComparison.Ordinal))
            {
                return string.IsNullOrEmpty(attribute.Value)
                    ? null
                    : attribute.Value;
            }
        }

        return null;
    }

    public static string? ChildValue(
        OpenXmlElement? parent,
        string childLocalName) =>
        Attribute(Child(parent, childLocalName), "val");

    public static Twip? TwipAttribute(
        OpenXmlElement? element,
        string localName)
    {
        string? value = Attribute(element, localName);
        return long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long parsed)
                ? new Twip(parsed)
                : null;
    }

    public static int? PositiveIntAttribute(
        OpenXmlElement? element,
        string localName)
    {
        string? value = Attribute(element, localName);
        return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed)
            && parsed > 0
                ? parsed
                : null;
    }

    public static bool? OnOffElement(OpenXmlElement? element)
    {
        if (element is null)
        {
            return null;
        }

        string? value = Attribute(element, "val");
        if (value is null)
        {
            return true;
        }

        return value switch
        {
            "1" or "true" or "on" => true,
            "0" or "false" or "off" => false,
            _ => null,
        };
    }

    public static bool? OnOffAttribute(
        OpenXmlElement? element,
        string localName)
    {
        string? value = Attribute(element, localName);
        return value switch
        {
            "1" or "true" or "on" => true,
            "0" or "false" or "off" => false,
            _ => null,
        };
    }
}
