using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace RJ.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_must_not_reference_other_RJ_projects()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(global::RJ.Domain.AssemblyMarker).Assembly,
            allowedProjectReferences: []);
    }

    [Fact]
    public void Application_may_reference_only_Domain()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(global::RJ.Application.AssemblyMarker).Assembly,
            allowedProjectReferences: ["RJ.Domain"]);
    }

    [Fact]
    public void Infrastructure_may_reference_only_Application_and_Domain()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(global::RJ.Infrastructure.AssemblyMarker).Assembly,
            allowedProjectReferences: ["RJ.Application", "RJ.Domain"]);
    }

    [Fact]
    public void Api_may_reference_Application_and_Infrastructure_but_not_Domain_directly()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(global::RJ.Api.AssemblyMarker).Assembly,
            allowedProjectReferences: ["RJ.Application", "RJ.Infrastructure"]);
    }

    [Fact]
    public void Api_must_not_reference_schema_migration_operation()
    {
        var apiAssembly = typeof(global::RJ.Api.AssemblyMarker).Assembly;

        using var stream = File.OpenRead(apiAssembly.Location);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var migrateReferences = metadata.MemberReferences
            .Select(handle => metadata.GetMemberReference(handle))
            .Where(reference => metadata.GetString(reference.Name) == "MigrateAsync")
            .Where(reference => IsPostgresSchemaReference(metadata, reference.Parent))
            .ToArray();

        Assert.Empty(migrateReferences);
    }

    [Fact]
    public void BenchmarkCli_may_reference_only_Application()
    {
        AssertNoUnexpectedProjectReferences(
            typeof(global::RJ.BenchmarkCli.AssemblyMarker).Assembly,
            allowedProjectReferences: ["RJ.Application"]);
    }

    private static bool IsPostgresSchemaReference(MetadataReader metadata, EntityHandle parent)
    {
        if (parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        var typeReference = metadata.GetTypeReference((TypeReferenceHandle)parent);
        return metadata.GetString(typeReference.Name) == "PostgresSchema"
            && metadata.GetString(typeReference.Namespace) == "RJ.Infrastructure.Persistence";
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
