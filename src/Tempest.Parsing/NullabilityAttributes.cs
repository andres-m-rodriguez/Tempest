// Nullable-flow attributes compile against these types, which netstandard2.0 lacks —
// same polyfill pattern as IsExternalInit.
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute
{
    public bool ReturnValue { get; } = returnValue;
}
