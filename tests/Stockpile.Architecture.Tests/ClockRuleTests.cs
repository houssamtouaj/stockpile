using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Stockpile.Architecture.Tests;

/// <summary>
/// Nothing but SystemClock may read the wall clock. Ninety days of backdated seed data and
/// the slow-movers report are untestable if handlers can reach DateTimeOffset.UtcNow directly.
/// <para>
/// NetArchTest works at the type-reference level and cannot see a call to a static
/// property, so this one reads IL directly. That is more code than a normal architecture
/// test and it is worth it: this is the rule most likely to be broken accidentally, by
/// someone typing DateTimeOffset.UtcNow out of habit.
/// </para>
/// </summary>
public class ClockRuleTests
{
    private static readonly string[] ProductionAssemblies =
    [
        typeof(Stockpile.Domain.AssemblyMarker).Assembly.Location,
        typeof(Stockpile.Application.AssemblyMarker).Assembly.Location,
        typeof(Stockpile.Infrastructure.DependencyInjection).Assembly.Location
    ];

    private static readonly string[] ForbiddenCalls =
    [
        "System.DateTime System.DateTime::get_Now()",
        "System.DateTime System.DateTime::get_UtcNow()",
        "System.DateTimeOffset System.DateTimeOffset::get_Now()",
        "System.DateTimeOffset System.DateTimeOffset::get_UtcNow()"
    ];

    private const string AllowedType = "Stockpile.Infrastructure.Time.SystemClock";

    [Fact]
    public void Only_SystemClock_reads_the_wall_clock()
    {
        var offenders = new List<string>();

        foreach (var path in ProductionAssemblies)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);

            foreach (var type in assembly.MainModule.GetTypes())
            {
                if (type.FullName.StartsWith(AllowedType, StringComparison.Ordinal))
                    continue;

                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode != OpCodes.Call) continue;
                        if (instruction.Operand is not MethodReference called) continue;
                        if (!ForbiddenCalls.Contains(called.FullName)) continue;

                        offenders.Add($"{type.FullName}.{method.Name} -> {called.Name}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Inject IClock instead of reading the wall clock directly:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}
