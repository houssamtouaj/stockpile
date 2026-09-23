using FluentValidation;

namespace Stockpile.Application.Suppliers.Commands.UpdateSupplier;

public sealed class UpdateSupplierValidator : AbstractValidator<UpdateSupplierCommand>
{
    public UpdateSupplierValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        // Through .Value, because these are Patch<string>. The property names are pinned
        // back to what the client sent so the problem document's `errors` keys stay
        // "email"/"phone" rather than becoming "Email.Value".
        RuleFor(x => x.Email.Value!).EmailAddress().MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email.Value))
            .OverridePropertyName(nameof(UpdateSupplierCommand.Email));
        RuleFor(x => x.Phone.Value).MaximumLength(50)
            .OverridePropertyName(nameof(UpdateSupplierCommand.Phone));
        RuleFor(x => x.Address.Value).MaximumLength(500)
            .OverridePropertyName(nameof(UpdateSupplierCommand.Address));
    }
}
