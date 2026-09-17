using FluentValidation;
using Shouldly;
using Stockpile.Application.Common.Behaviors;
using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.UnitTests.Behaviors;

public class ValidationBehaviorTests
{
    private sealed record CreateThing(string Name, int Quantity) : ICommand;

    private sealed class CreateThingValidator : AbstractValidator<CreateThing>
    {
        public CreateThingValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be positive.");
        }
    }

    [Fact]
    public async Task ValidRequest_reachesTheHandler()
    {
        var behavior = new ValidationBehavior<CreateThing, Result>([new CreateThingValidator()]);
        var handlerRan = false;

        var result = await behavior.Handle(new CreateThing("Widget", 5), _ =>
        {
            handlerRan = true;
            return Task.FromResult(Result.Ok());
        }, CancellationToken.None);

        handlerRan.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidRequest_shortCircuits_andReturnsAValidationFailedError()
    {
        var behavior = new ValidationBehavior<CreateThing, Result>([new CreateThingValidator()]);
        var handlerRan = false;

        var result = await behavior.Handle(new CreateThing("", -1), _ =>
        {
            handlerRan = true;
            return Task.FromResult(Result.Ok());
        }, CancellationToken.None);

        handlerRan.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<ValidationFailedError>();
        error.Failures.Keys.ShouldBe(["Name", "Quantity"], ignoreOrder: true);
    }

    [Fact]
    public async Task NoValidatorsRegistered_passesThrough()
    {
        var behavior = new ValidationBehavior<CreateThing, Result>([]);

        var result = await behavior.Handle(
            new CreateThing("", -1), _ => Task.FromResult(Result.Ok()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }
}
