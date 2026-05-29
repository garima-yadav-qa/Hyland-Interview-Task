using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using EcommerceTests.Helpers;
using EcommerceTests.PageObjects;

namespace EcommerceTests.Integration
{
    [TestFixture]
    public class PromotionFlowTests : PageTest
    {
        private TestConfig _config;
        private ApiClient _apiClient;
        private DatabaseHelper _dbHelper;
        private readonly List<string> _createdPromotionIds = new();
        private readonly List<string> _createdOrderIds = new();

        [SetUp]
        public void Setup()
        {
            _config = TestConfig.Load();
            _apiClient = new ApiClient(_config.Api.BaseUrl);
            _dbHelper = new DatabaseHelper(
                _config.Database.Host,
                _config.Database.Port,
                _config.Database.Database,
                _config.Database.Username,
                _config.Database.Password);
            _dbHelper.Connect();

            _createdPromotionIds.Clear();
            _createdOrderIds.Clear();
        }

        [TearDown]
        public async Task Cleanup()
        {
            // Order matters: delete orders first (the audit log cascades), then promotions.
            // Per-resource cleanup rather than a global /admin/reset means these tests can
            // run in parallel against a shared DB without trampling each other.
            foreach (var orderId in _createdOrderIds)
            {
                try { _dbHelper.DeleteOrder(orderId); }
                catch { /* best-effort cleanup; don't mask the real test failure */ }
            }

            foreach (var promoId in _createdPromotionIds)
            {
                try { await _apiClient.DeletePromotionAsync(promoId); }
                catch { /* best-effort cleanup */ }
            }

            _dbHelper?.Dispose();
            _apiClient?.Dispose();
        }

        [Test]
        public async Task FullPromotionFlow_HappyPath_CreatesOrderAndAuditLog()
        {
            // Arrange — seed the promotion via the API.
            var code = UniqueCode("SPRING25");
            var promo = await _apiClient.CreatePromotionAsync(new
            {
                code,
                discountType = "PERCENTAGE",
                discountValue = _config.TestData.ExpectedDiscountPercentage,
                category = "ELECTRONICS",
                maxUses = 100,
                validFrom = DateTime.UtcNow.AddDays(-1).ToString("o"),
                validUntil = DateTime.UtcNow.AddDays(30).ToString("o")
            });
            _createdPromotionIds.Add(promo.PromotionId);

            // Act — drive the UI through the checkout.
            var checkout = new CheckoutPage(Page, _config.UI.BaseUrl);
            await checkout.NavigateAsync();
            await checkout.ApplyPromoCodeAsync(code);

            await checkout.VerifyDiscountApplied(_config.TestData.ExpectedDiscountAmount);

            var finalPrice = await checkout.GetFinalPriceAsync();
            Assert.That(finalPrice, Is.EqualTo(_config.TestData.ExpectedFinalPrice).Within(0.01m));

            var orderId = await checkout.PlaceOrderAsync();
            Assert.That(orderId, Is.Not.Null.And.Not.Empty, "Order ID was not returned from the UI.");
            _createdOrderIds.Add(orderId);

            // Assert — verify the database state matches what the UI claimed.
            var order = _dbHelper.GetOrderById(orderId);
            Assert.That(order, Is.Not.Null, $"Order {orderId} was not persisted to the database.");
            Assert.That(order.Status, Is.EqualTo("COMPLETED"));
            Assert.That(order.PromotionCode, Is.EqualTo(code));

            Assert.That(_dbHelper.VerifyOrderTotals(
                    orderId,
                    _config.TestData.ProductPrice,
                    _config.TestData.ExpectedDiscountAmount,
                    _config.TestData.ExpectedFinalPrice),
                Is.True,
                "Order totals in the database do not match expected values.");

            var audit = _dbHelper.GetAuditLogByOrderId(orderId);
            Assert.That(audit, Is.Not.Null, "Promotion audit log was not created for this order.");
            Assert.That(audit.PromotionId, Is.EqualTo(promo.PromotionId));
            Assert.That(audit.DiscountApplied,
                Is.EqualTo(_config.TestData.ExpectedDiscountAmount).Within(0.01m));
        }

        [Test]
        public async Task InvalidPromoCode_ShowsErrorAndLeavesPriceUnchanged()
        {
            var checkout = new CheckoutPage(Page, _config.UI.BaseUrl);
            await checkout.NavigateAsync();

            // Random suffix to guarantee the code can't accidentally exist from a prior run.
            var bogusCode = "DOES-NOT-EXIST-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            await checkout.ApplyPromoCodeAsync(bogusCode);

            Assert.That(await checkout.IsErrorDisplayedAsync(), Is.True,
                "Expected an error message for an unknown promo code.");

            var errorText = await checkout.GetErrorMessageAsync();
            Assert.That(errorText,
                Does.Contain("not found").IgnoreCase.Or.Contain("invalid").IgnoreCase,
                $"Error text did not indicate an unknown/invalid code. Got: '{errorText}'");

            // Price must not have moved when the code was rejected.
            var finalPrice = await checkout.GetFinalPriceAsync();
            Assert.That(finalPrice, Is.EqualTo(_config.TestData.ProductPrice).Within(0.01m));
        }

        [Test]
        public async Task ExpiredPromoCode_ShowsExpiryError()
        {
            var code = UniqueCode("EXPIRED");
            var promo = await _apiClient.CreatePromotionAsync(new
            {
                code,
                discountType = "PERCENTAGE",
                discountValue = 25,
                category = "ELECTRONICS",
                maxUses = 100,
                validFrom = DateTime.UtcNow.AddDays(-30).ToString("o"),
                validUntil = DateTime.UtcNow.AddDays(-1).ToString("o") // already expired
            });
            _createdPromotionIds.Add(promo.PromotionId);

            var checkout = new CheckoutPage(Page, _config.UI.BaseUrl);
            await checkout.NavigateAsync();
            await checkout.ApplyPromoCodeAsync(code);

            Assert.That(await checkout.IsErrorDisplayedAsync(), Is.True,
                "Expected an error message for an expired promo code.");

            var errorText = await checkout.GetErrorMessageAsync();
            Assert.That(errorText, Does.Contain("expired").IgnoreCase,
                $"Error text did not indicate expiry. Got: '{errorText}'");
        }

        [Test]
        public async Task WrongCategoryPromo_ShowsCategoryMismatchError()
        {
            // BOOKS-category promo applied against an ELECTRONICS product should be rejected
            // by the UI before it ever reaches the order endpoint.
            var code = UniqueCode("BOOKS25");
            var promo = await _apiClient.CreatePromotionAsync(new
            {
                code,
                discountType = "PERCENTAGE",
                discountValue = 25,
                category = "BOOKS",
                maxUses = 100,
                validFrom = DateTime.UtcNow.AddDays(-1).ToString("o"),
                validUntil = DateTime.UtcNow.AddDays(30).ToString("o")
            });
            _createdPromotionIds.Add(promo.PromotionId);

            var checkout = new CheckoutPage(Page, _config.UI.BaseUrl);
            await checkout.NavigateAsync();
            await checkout.ApplyPromoCodeAsync(code);

            Assert.That(await checkout.IsErrorDisplayedAsync(), Is.True,
                "Expected an error message for a category-mismatched promo code.");

            var errorText = await checkout.GetErrorMessageAsync();
            Assert.That(errorText,
                Does.Contain("ELECTRONICS").IgnoreCase.Or.Contain("not valid").IgnoreCase,
                $"Error text did not indicate category mismatch. Got: '{errorText}'");
        }

        // Suffix codes with a short random token so re-runs (or two parallel runners) can't
        // collide on the UNIQUE constraint in the promotions table.
        private static string UniqueCode(string prefix) =>
            $"{prefix}_{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()}";
    }
}
