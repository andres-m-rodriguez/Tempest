// Init-only setters (records) compile against this type, which netstandard2.0 lacks.
// The generator targets netstandard2.0 because it loads inside the compiler process,
// which may run on .NET Framework.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
