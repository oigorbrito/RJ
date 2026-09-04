using System.Reflection;

namespace RJ.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_must_not_reference_other_RJ_projects()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(Domain.AssemblyMarker).Assembly,
            allowedProjectReferences: []);
    }

    [Fact]
    public void Application_may_reference_only_Domain()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(Application.AssemblyMarker).Assembly,
            allowedProjectReferences: ["RJ.Domain"]);
    }

    [Fact]
    public void Infrastructure_may_reference_only_Application_and_Domain()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(Infrastructure.AssemblyMarker).Assembly,
            allowedProjectReferences: ["RJ.Application", "RJ.Domain"]);
    }

    [Fact]
    public void Api_may_reference_Application_and_Infrastructure_but_not_Domain_directly()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(Api.AssemblyMarker).Assembly,
            allowedProjectReferences: ["RJ.Application", "RJ.Infrastructure"]);
    }

    private static void AssertNoUnexpectedProjectReferences(
        Assembly assembly,
        IReadOnlyCollection<string> allowedProjectReferences)
    {
        var unexpected = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("RJ.", StringComparison.Ordinal))
            .Where(name => !allowedProjectReferences.Contains(name!, StringComparer.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }
}
