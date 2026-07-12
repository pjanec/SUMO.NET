using System.IO;

namespace Sim.ParityTests;

// Rung B13 -- packaging/target-framework guard for the SumoSharp NuGet story (SUMOSHARP-API.md §3).
// The two packable projects (Sim.Core, Sim.Ingest) multi-target net8.0 (the parity/perf target) AND
// netstandard2.1 (Unity/Godot reach). netstandard2.1 is NOT golden-checked -- the offline parity gate
// runs on net8.0 -- so nothing here exercises simulation output; these are hermetic, source-only
// assertions (they read committed csproj/source, touch no network, need no SUMO) that fail loudly if a
// future edit silently drops the ns2.1 target or the compiler-support polyfills it depends on. That
// keeps the "one span-based API across both frameworks" promise from regressing unnoticed.
public class RungB13PackagingTargetsTests
{
    [Theory]
    [InlineData("src/Sim.Core/Sim.Core.csproj")]
    [InlineData("src/Sim.Ingest/Sim.Ingest.csproj")]
    public void PackableProject_MultiTargets_Net8_And_NetStandard21(string relPath)
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), relPath));

        // Multi-target via <TargetFrameworks> (plural), covering both frameworks. A single
        // <TargetFramework> (singular) would mean the ns2.1 reach was dropped.
        Assert.Contains("<TargetFrameworks>", csproj);
        Assert.Contains("net8.0", csproj);
        Assert.Contains("netstandard2.1", csproj);

        // The compiler emits references to runtime types (IsExternalInit, RequiredMember...) that
        // netstandard2.1's corlib lacks; the shared polyfill file supplies them. If it is not linked,
        // the ns2.1 build breaks on the codebase's pervasive init/record/required usage.
        Assert.Contains("NetstandardPolyfills.cs", csproj);

        // System.Memory backs Span<T>/Memory<T> on ns2.1 (in-box on net8.0), scoped to that target.
        Assert.Contains("System.Memory", csproj);
    }

    [Fact]
    public void SharedPolyfills_DefineCompilerSupportTypes_UnderNetstandardGuard()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src/Shared/NetstandardPolyfills.cs"));

        // Guarded so the file compiles away entirely on net8.0 (the parity target sees none of it).
        Assert.Contains("#if !NET8_0_OR_GREATER", src);

        // The exact types the compiler needs for this codebase's language features.
        Assert.Contains("IsExternalInit", src);                 // init-only setters + records
        Assert.Contains("RequiredMemberAttribute", src);        // `required` members
        Assert.Contains("CompilerFeatureRequiredAttribute", src);
        Assert.Contains("SetsRequiredMembersAttribute", src);
    }

    // Walk up from the test assembly to the repo root (Traffic.sln), matching the other tests'
    // convention -- no dependency on git at test time.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Traffic.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (Traffic.sln not found above test assembly).");
    }
}
