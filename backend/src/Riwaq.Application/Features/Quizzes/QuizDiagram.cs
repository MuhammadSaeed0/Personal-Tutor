using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Riwaq.Application.Features.Quizzes;

/// <summary>
/// Model-drawn question diagrams (SVG). The markup is untrusted: it is parsed as XML with DTDs prohibited, rebuilt
/// from an allowlist of drawing elements and presentation attributes, and anything that can run code or load
/// resources (scripts, styles, foreign objects, images, links, event handlers, external url()) is dropped.
/// </summary>
public static partial class QuizDiagram
{
    public const int MaxLength = 20_000;
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static readonly HashSet<string> Elements =
    [
        "svg", "g", "path", "line", "polyline", "polygon", "rect", "circle", "ellipse", "text", "tspan", "defs", "marker", "title", "desc"
    ];

    private static readonly HashSet<string> Attributes =
    [
        "viewBox", "preserveAspectRatio", "id", "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "width", "height", "d", "points",
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-dasharray", "stroke-linecap", "stroke-linejoin", "stroke-opacity",
        "opacity", "transform", "font-size", "font-weight", "font-family", "font-style", "text-anchor", "dominant-baseline", "dx", "dy",
        "marker-start", "marker-mid", "marker-end", "markerWidth", "markerHeight", "refX", "refY", "orient", "markerUnits", "direction"
    ];

    /// <summary>Returns safe SVG markup, or null when the input is not a usable SVG drawing.</summary>
    public static string? Sanitize(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup) || markup.Length > MaxLength * 2) return null;
        XElement root;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxLength * 4 };
            using var reader = XmlReader.Create(new StringReader(markup.Trim()), settings);
            root = XElement.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        if (root.Name.LocalName != "svg" || root.Attribute("viewBox") is null) return null;
        var clean = CleanElement(root, isRoot: true);
        if (clean is null || !clean.Descendants().Any(e => e.Name.LocalName is not ("title" or "desc" or "defs"))) return null;

        var output = clean.ToString(SaveOptions.DisableFormatting);
        return output.Length <= MaxLength ? output : null;
    }

    private static XElement? CleanElement(XElement source, bool isRoot)
    {
        var name = source.Name.LocalName;
        if (!Elements.Contains(name)) return null;

        var element = new XElement(Svg + name);
        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace != XNamespace.None) continue;
            var key = attribute.Name.LocalName;
            if (!Attributes.Contains(key)) continue;
            if (isRoot && key is "width" or "height") continue; // the diagram scales with its container
            var value = attribute.Value.Trim();
            if (!SafeValue(value)) continue;
            element.SetAttributeValue(key, value);
        }

        foreach (var node in source.Nodes())
        {
            switch (node)
            {
                case XElement child when CleanElement(child, isRoot: false) is { } kept:
                    element.Add(kept);
                    break;
                case XText text when name is "text" or "tspan" or "title" or "desc":
                    element.Add(new XText(text.Value));
                    break;
            }
        }
        return element;
    }

    /// <summary>Only local references (url(#id)) are allowed; no scripts or external resources.</summary>
    private static bool SafeValue(string value)
    {
        if (value.Length > 4000) return false;
        if (value.Contains("javascript:", StringComparison.OrdinalIgnoreCase) || value.Contains("expression(", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (Match url in UrlFunction().Matches(value))
        {
            if (!url.Groups[1].Value.Trim().StartsWith('#')) return false;
        }
        return true;
    }

    /// <summary>The visible labels of a (sanitised) diagram, normalised for comparison.</summary>
    public static HashSet<string> Labels(string svg)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var text in XElement.Parse(svg).Descendants().Where(e => e.Name.LocalName is "text" or "tspan"))
            {
                var value = Normalize(string.Concat(text.Nodes().OfType<XText>().Select(t => t.Value)));
                if (value.Length > 0) labels.Add(value);
            }
        }
        catch (XmlException) { }
        return labels;
    }

    /// <summary>Markers such as "(3)", "(أ)" or "(A)" that the question text refers to.</summary>
    public static IEnumerable<string> ReferencedMarkers(string text) =>
        Marker().Matches(text).Select(m => Normalize(m.Groups[1].Value)).Distinct();

    /// <summary>True when a normalised answer text (3+ letters) is written in the diagram, which would give it away.</summary>
    public static bool RevealsAnswer(string svg, IEnumerable<string> answers)
    {
        var shown = string.Concat(Labels(svg));
        return answers.Select(Normalize).Any(a => a.Count(char.IsLetter) >= 3 && shown.Contains(a, StringComparison.Ordinal));
    }

    /// <summary>Lower case, Arabic-Indic digits to ASCII, whitespace and punctuation removed.</summary>
    public static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormKC))
        {
            if (c is >= '٠' and <= '٩') sb.Append((char)('0' + (c - '٠')));
            else if (c is >= '۰' and <= '۹') sb.Append((char)('0' + (c - '۰')));
            else if (char.IsLetterOrDigit(c)) sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"url\(\s*['""]?([^)'""]*)['""]?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlFunction();

    [GeneratedRegex(@"\(\s*([0-9٠-٩]{1,2}|[A-Za-z]|[ء-ي])\s*\)")]
    private static partial Regex Marker();
}
