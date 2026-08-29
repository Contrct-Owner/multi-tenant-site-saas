using System.Reflection;
using NetArchTest.Rules;

namespace Premise.ArchitectureTests;

/// <summary>
/// The compiler-adjacent enforcement of ADR 17: modules communicate only
/// through Premise.Contracts and Wolverine messages. These tests are fast
/// on purpose - a Stop hook and CI both run them.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(Modules.Tenancy.TenancyModule).Assembly,
    ];

    private const string ModulePrefix = "Premise.Modules.";

    public static IEnumerable<object[]> Modules() =>
        ModuleAssemblies.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_other_modules_internals(Assembly module)
    {
        var self = module.GetName().Name!;
        var otherModules = ModuleAssemblies
            .Select(a => a.GetName().Name!)
            .Where(n => n != self)
            .ToArray();
        if (otherModules.Length == 0)
            return; // single module so far

        var result = Types
            .InAssembly(module)
            .ShouldNot()
            .HaveDependencyOnAny(otherModules)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{self} references another module's internals: "
                + string.Join(", ", result.FailingTypeNames ?? [])
        );
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_the_host(Assembly module)
    {
        var result = Types
            .InAssembly(module)
            .ShouldNot()
            .HaveDependencyOn("Premise.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, $"{module.GetName().Name} must not depend on the host.");
    }

    [Fact]
    public void Platform_does_not_reference_any_module()
    {
        var result = Types
            .InAssembly(typeof(Platform.Kernel.OrgId).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ModulePrefix)
            .GetResult();
        Assert.True(
            result.IsSuccessful,
            "Platform is the shared kernel; it must not know about modules."
        );
    }

    [Fact]
    public void Contracts_reference_nothing_but_platform_kernel()
    {
        var result = Types
            .InAssembly(typeof(Contracts.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ModulePrefix + "*",
                "Premise.Api",
                "Microsoft.EntityFrameworkCore"
            )
            .GetResult();
        Assert.True(result.IsSuccessful, "Contracts carry DTOs and integration events only.");
    }
}
