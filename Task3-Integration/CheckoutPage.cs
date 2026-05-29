using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace EcommerceTests.PageObjects
{
    public class CheckoutPage
    {
        private readonly IPage _page;
        private readonly string _baseUrl;

        // Locators retained from the skeleton so the public surface matches what tests expect.
        private ILocator PromoCodeInput  => _page.Locator("#promo-code");
        private ILocator ApplyPromoButton => _page.Locator("#apply-promo");
        private ILocator OriginalPrice   => _page.Locator(".original-price");
        private ILocator DiscountAmount  => _page.Locator(".discount-amount");
        private ILocator FinalPrice      => _page.Locator(".final-price");
        private ILocator PlaceOrderButton => _page.Locator("#place-order");
        private ILocator OrderNumber     => _page.Locator(".order-number");

        // The #promo-message div is the single source of truth for both success and error
        // states of the apply-promo action — it just swaps class names.
        private ILocator PromoMessage    => _page.Locator("#promo-message");

        public CheckoutPage(IPage page, string baseUrl)
        {
            _page = page;
            _baseUrl = baseUrl;
        }

        public async Task NavigateAsync()
        {
            await _page.GotoAsync(_baseUrl);
            // Don't return until the page is interactive — otherwise the first action races the render.
            await _page.Locator("#checkout-view").WaitForAsync();
        }

        public async Task ApplyPromoCodeAsync(string code)
        {
            await PromoCodeInput.FillAsync(code);
            await ApplyPromoButton.ClickAsync();

            // The promo-message div becomes visible regardless of outcome (success or error).
            // Waiting on its visibility is the most reliable single signal that the API round-trip
            // has completed and the DOM has updated.
            await PromoMessage.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
        }

        public async Task<decimal> GetOriginalPriceAsync() =>
            ParseMoney(await OriginalPrice.TextContentAsync());

        public async Task<decimal> GetDiscountAmountAsync() =>
            // Discount renders as "-$250.00"; ParseMoney returns the absolute value.
            // Sign is implicit in which field the value came from.
            ParseMoney(await DiscountAmount.TextContentAsync());

        public async Task<decimal> GetFinalPriceAsync() =>
            ParseMoney(await FinalPrice.TextContentAsync());

        public async Task VerifyDiscountApplied(decimal expectedDiscount)
        {
            var actual = await GetDiscountAmountAsync();
            // One-cent tolerance — tighter than that and we're testing JS float math, not the feature.
            Assert.That(actual, Is.EqualTo(expectedDiscount).Within(0.01m),
                $"Expected discount {expectedDiscount:C} but got {actual:C}");
        }

        public async Task<string> PlaceOrderAsync()
        {
            await PlaceOrderButton.ClickAsync();

            // The confirmation view replaces checkout-view; the order number is the marker that
            // the swap is complete and the backend POST succeeded.
            await OrderNumber.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });

            return (await OrderNumber.TextContentAsync())?.Trim();
        }

        public async Task<bool> IsErrorDisplayedAsync()
        {
            // Scoped to #promo-message so a future success-styled .error-message elsewhere on
            // the page wouldn't accidentally make this return true.
            var errorInPromo = _page.Locator("#promo-message.error-message");
            return await errorInPromo.IsVisibleAsync();
        }

        public async Task<string> GetErrorMessageAsync() =>
            (await PromoMessage.TextContentAsync())?.Trim();

        /// <summary>
        /// Parses UI money strings like "$1,000.00" or "-$250.00" into decimal.
        /// Always returns a positive value; the caller knows from context whether it's a
        /// charge or a credit.
        /// </summary>
        private static decimal ParseMoney(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("Empty money string from the UI.");

            // Strip everything that isn't a digit or decimal point. Drops $, commas, and the
            // leading minus sign — all of which are presentation, not data.
            var digits = Regex.Replace(raw, @"[^\d.]", string.Empty);
            return decimal.Parse(digits, CultureInfo.InvariantCulture);
        }
    }
}
