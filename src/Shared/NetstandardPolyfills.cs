// Compiler-support polyfills required to compile the C# language features this codebase already
// uses (init-only setters, records, and `required` members) against netstandard2.1, whose corlib
// predates the runtime types the compiler emits references to. On net8.0 these types ship in the
// BCL, so the whole file compiles away under `#if !NET8_0_OR_GREATER`.
//
// This file is LINKED into both packable projects (Sim.Core, Sim.Ingest); the types are `internal`
// so each assembly gets its own copy with no cross-assembly collision. It contains no behavior and
// cannot affect simulation output (net8.0 — the parity target — omits it entirely).
#if !NET8_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    // Enables `init`-only property setters and `record` types. The compiler emits a modreq on this
    // type for init accessors; netstandard2.1 does not define it.
    internal static class IsExternalInit { }

    // Marks a member as `required`. The compiler emits this attribute for `required` members.
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    // The compiler stamps this on any type/member using a feature (like RequiredMembers) that a
    // downstream compiler must understand.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;

        public string FeatureName { get; }
        public bool IsOptional { get; init; }

        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    // Lets a constructor declare it initializes all `required` members, so callers using that ctor
    // are not forced to set them via object-initializer syntax.
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}

#endif
