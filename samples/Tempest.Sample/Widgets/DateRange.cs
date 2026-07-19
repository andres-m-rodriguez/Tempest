namespace Tempest.Sample.Widgets;

/// <summary>Stand-in for a third-party type (e.g. MudBlazor's DateRange) that only
/// resolves via a @using — regression coverage for usings in generated files.</summary>
public class DateRange
{
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
}
