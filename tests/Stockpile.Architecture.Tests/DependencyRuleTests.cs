using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Stockpile.Architecture.Tests;

public class DependencyRuleTests
{
    private static readonly Assembly DomainAssembly =
        typeof(Stockpile.Domain.AssemblyMarker).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(Stockpile.Application.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_has_no_outward_dependencies()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "MediatR",
                "FluentValidation",
                "Npgsql",
                "StackExchange.Redis",
                "Stockpile.Application",
                "Stockpile.Infrastructure",
                "Stockpile.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore.SignalR",
                "Microsoft.AspNetCore.Http",
                "Npgsql",
                "StackExchange.Redis",
                "ClosedXML",
                "Quartz",
                "Stockpile.Infrastructure",
                "Stockpile.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    private static string FailureMessage(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is null or []
            ? "No failing types reported."
            : "Offending types: " + string.Join(", ", result.FailingTypeNames);
}
