namespace Tempest.RazorParser;

/// <summary>Everything the razor frontend needs about one .razor file, as plain values.
/// The shell unwraps Roslyn's AdditionalText and analyzer options into this; tests just
/// new one up.</summary>
public sealed record RazorSource(
    /// <summary>Path of the .razor file; its file name (sanitized) becomes the component name.</summary>
    string FilePath,
    string Text,
    /// <summary>The project's RootNamespace build property; empty when unknown.</summary>
    string RootNamespace = "",
    /// <summary>The file's project-relative path (the Razor SDK's TargetPath metadata,
    /// already base64-decoded by the shell); its directory contributes namespace segments.
    /// Empty when unknown.</summary>
    string TargetPath = "");
