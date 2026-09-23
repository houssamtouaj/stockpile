using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;

namespace Stockpile.Api.IntegrationTests;

/// <summary>
/// Adds test-only routes to the endpoint route builder the app itself used. An
/// <see cref="EndpointDataSource"/> registered in DI is NOT picked up — the routing
/// middleware matches against the data sources collected by <c>UseEndpoints</c>, so the
/// routes have to join that collection. It is an <see cref="ObservableCollection{T}"/>
/// on purpose, which is what makes this late addition work.
/// <para>
/// Shared by every set of probe routes so the real <c>Program</c> keeps no test scaffolding
/// and the reflection into ASP.NET Core's internals lives in exactly one place.
/// </para>
/// </summary>
internal sealed class TestRouteStartupFilter(params EndpointDataSource[] dataSources) : IStartupFilter
{
    // The key WebApplication stashes itself under, as IEndpointRouteBuilder. Internal to
    // ASP.NET Core (EndpointRoutingApplicationBuilderExtensions), hence the literal.
    private const string EndpointRouteBuilderKey = "__EndpointRouteBuilder";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        // The app's own configuration runs first: it is what puts the route builder in
        // Properties and maps the real endpoints.
        next(app);

        if (app.Properties.TryGetValue(EndpointRouteBuilderKey, out var value) &&
            value is IEndpointRouteBuilder routes)
        {
            foreach (var dataSource in dataSources)
                routes.DataSources.Add(dataSource);
        }
        else
        {
            throw new InvalidOperationException(
                $"No '{EndpointRouteBuilderKey}' in IApplicationBuilder.Properties — ASP.NET " +
                "Core changed how WebApplication exposes its endpoint route builder, and the " +
                "test-only probe routes would silently 404 instead of failing here.");
        }
    };
}
