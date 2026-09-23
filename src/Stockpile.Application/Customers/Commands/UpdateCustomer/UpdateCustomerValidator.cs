using FluentValidation;

namespace Stockpile.Application.Customers.Commands.UpdateCustomer;

public sealed class UpdateCustomerValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        // Through .Value, because these are Patch<string>. The property names are pinned
        // back to what the client sent so the problem document's `errors` keys stay
        // "email"/"phone" rather than becoming "Email.Value".
        RuleFor(x => x.Email.Value!).EmailAddress().MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email.Value))
            .OverridePropertyName(nameof(UpdateCustomerCommand.Email));
        RuleFor(x => x.Phone.Value).MaximumLength(50)
            .OverridePropertyName(nameof(UpdateCustomerCommand.Phone));
        RuleFor(x => x.ShippingAddress.Value).MaximumLength(500)
            .OverridePropertyName(nameof(UpdateCustomerCommand.ShippingAddress));
    }
}
