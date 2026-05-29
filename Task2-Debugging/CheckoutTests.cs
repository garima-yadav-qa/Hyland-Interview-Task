using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace EcommerceTests.Integration
{
    [TestFixture]
    public class CheckoutTests : PageTest
    {
        [Test]
        public async Task TestCheckoutProcessWithDiscount()
        {

            // Navigate to product page
            await Page.GotoAsync("https://staging.example-shop.com/products/laptop-pro");

            // Add to cart
            var addToCartBtn = Page.Locator("#add-to-cart");
            await addToCartBtn.ClickAsync();

            await Page.Locator(".cart-count").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });

            await Page.GotoAsync("https://staging.example-shop.com/cart");

            var priceElement = Page.Locator(".cart-total");
            var priceText = await priceElement.TextContentAsync();
            var originalPrice = double.Parse(priceText.Replace("$", "").Replace(",", "").Trim());

            Console.WriteLine($"Original price: {originalPrice}");

            var checkoutBtn = Page.Locator("#checkout-button");
            await checkoutBtn.ClickAsync();
            await Page.Locator("#first-name").FillAsync("John");
            await Page.Locator("#last-name").FillAsync("Smith");
            await Page.Locator("#email").FillAsync("john.smith@example.com");
            await Page.Locator("#address").FillAsync("123 Main Street");
            await Page.Locator("#city").FillAsync("New York");
            await Page.Locator("#postal-code").FillAsync("10001");

            var discountInput = Page.Locator("#discount-code");
            await discountInput.FillAsync("SAVE20");

            var applyBtn = Page.Locator("#apply-discount");
            await applyBtn.ClickAsync();

            await Page.Locator(".discount-applied").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 3000
            });

            var finalPriceElement = Page.Locator(".final-price");
            var finalPriceText = await finalPriceElement.TextContentAsync();
            var finalPrice = double.Parse(finalPriceText.Replace("$", "").Replace(",", "").Trim());

            Console.WriteLine($"Final price: {finalPrice}");

            var expectedPrice = originalPrice * 0.8;

            Assert.AreEqual(expectedPrice, finalPrice,
                $"Expected price {expectedPrice} but got {finalPrice}");

            var discountBadge = Page.Locator(".discount-badge");
            var badgeText = await discountBadge.TextContentAsync();
            Assert.AreEqual("-20%", badgeText,
                $"Expected discount badge '-20%' but got '{badgeText}'");
            await Page.Locator("#payment-method-card").ClickAsync();
            await Page.Locator("#card-number").FillAsync("4111111111111111");
            await Page.Locator("#card-expiry").FillAsync("12/25");
            await Page.Locator("#card-cvc").FillAsync("123");

            var placeOrderBtn = Page.Locator("#place-order");
            await placeOrderBtn.ClickAsync();

            await Page.WaitForURLAsync("**/order-confirmation", new PageWaitForURLOptions
            {
                Timeout = 10000
            });

            var successMsg = Page.Locator(".success-message");
            var successText = await successMsg.TextContentAsync();
            Assert.IsTrue(successText.Contains("Thank you for your order"));
        }
    }
}
