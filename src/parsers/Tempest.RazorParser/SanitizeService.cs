using System.Text;

namespace Tempest.RazorParser;

/// <summary>Identifier hygiene: raw names handed in by the shell (file name stems and
/// the like) become valid C# identifiers, and [Reactive] fields get the PascalCase twin
/// their state property is named after.</summary>
internal sealed class SanitizeService
{
    internal string SanitizeIdentifier(string name)
    {
        if (name.Length == 0)
            return "_";
        var sb = new StringBuilder(name.Length + 1);
        if (char.IsDigit(name[0]))
            sb.Append('_');
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.ToString();
    }

    /// <summary>The PascalCase twin a [Reactive] field's state property is named after.</summary>
    internal string ToPascal(string fieldName)
    {
        var trimmed = fieldName.TrimStart('_');
        if (trimmed.Length == 0)
            return "";
        return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
    }
}
