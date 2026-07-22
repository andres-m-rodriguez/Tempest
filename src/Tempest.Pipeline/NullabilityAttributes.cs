// Nullable-flow attributes (MemberNotNullWhen and friends) compile against these types,
// which netstandard2.0 lacks — same polyfill pattern as IsExternalInit.
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
internal sealed class MemberNotNullWhenAttribute(bool returnValue, params string[] members) : Attribute
{
    public bool ReturnValue { get; } = returnValue;
    public string[] Members { get; } = members;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class AllowNullAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter |
                AttributeTargets.Property | AttributeTargets.ReturnValue)]
internal sealed class MaybeNullAttribute : Attribute;
