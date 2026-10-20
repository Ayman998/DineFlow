using DineFlow.Domain.Payments;
using DineFlow.Infrastructure.Payments;

namespace DineFlow.Test.Infrastructure;

public class SandboxCardsTests
{
    [Theory]
    [InlineData("4242 4242 4242 4242", PaymentStatus.Succeeded, "Visa", "4242")]
    [InlineData("5555-5555-5555-4444", PaymentStatus.Succeeded, "Mastercard", "4444")]
    [InlineData("378282246310005", PaymentStatus.Succeeded, "Amex", "0005")]
    [InlineData("4000000000000002", PaymentStatus.Failed, "Visa", "0002")]
    [InlineData("4000 0000 0000 9995", PaymentStatus.Failed, "Visa", "9995")]
    public void Test_cards_give_their_published_result(string number, PaymentStatus status, string brand, string last4)
    {
        var outcome = SandboxCards.For(number);

        Assert.Equal((status, brand, last4), (outcome!.Status, outcome.Brand, outcome.Last4));
    }

    [Theory]
    [InlineData("4242 4242 4242 4241")] // fails the Luhn check
    [InlineData("4242")]
    [InlineData("")]
    public void Invalid_numbers_are_refused(string number) => Assert.Null(SandboxCards.For(number));

    [Fact]
    public void Every_listed_example_is_a_valid_card() =>
        Assert.All(SandboxCards.Examples, e => Assert.NotNull(SandboxCards.For(e.Number)));
}
