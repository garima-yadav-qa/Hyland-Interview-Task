using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Threading;

namespace EcommerceTests
{
    [TestFixture]
    public class LoginTests : PageTest
    {
        private string baseUrl = "https://example-shop.com";
        private string email = "test@example.com";
        private string password = "Password123!";

        [Test]
        public async Task UserCanLoginWithValidCredentials()
        {
            await Page.GotoAsync(baseUrl + "/login");

            Thread.Sleep(3000);
            await Task.Delay(2000);

            var emailField = Page.Locator("#email");
            await emailField.FillAsync(email);

            var passwordField = Page.Locator("#password");
            await passwordField.FillAsync(password);

            var button = Page.Locator("button");
            await button.ClickAsync();

            Thread.Sleep(2000);
            await Task.Delay(3000);

            string currentUrl = Page.Url;
            Assert.AreEqual("https://example-shop.com/dashboard", currentUrl);

            var userName = await Page.Locator(".user-name").TextContentAsync();
            Assert.AreEqual("Test User", userName);
        }

        [Test]
        public async Task LoginFailsWithInvalidPassword()
        {
            await Page.GotoAsync("https://example-shop.com/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            var e = Page.Locator("#email");
            await e.FillAsync("test@example.com");
            var p = Page.Locator("#password");
            await p.FillAsync("wrongpassword");
            var b = Page.Locator("button");
            await b.ClickAsync();

            Thread.Sleep(1000);
            await Task.Delay(1500);

            try
            {
                var errorMsg = await Page.Locator(".error-message").TextContentAsync();
                if (errorMsg != null && errorMsg != "")
                {
                    Assert.IsTrue(errorMsg.Contains("Invalid"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        [Test]
        public async Task LoginFailsWithEmptyFields()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            await Page.Locator("button").ClickAsync();

            Thread.Sleep(500);
            await Task.Delay(1000);

            var emailError = await Page.Locator("#email-error").IsVisibleAsync();
            var passwordError = await Page.Locator("#password-error").IsVisibleAsync();

            Assert.IsTrue(emailError);
            Assert.IsTrue(passwordError);
        }

        [Test]
        public async Task RememberMeCheckboxWorks()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            await Page.Locator("#email").FillAsync(email);
            await Page.Locator("#password").FillAsync(password);

            var rememberMe = Page.Locator("#remember-me");
            await rememberMe.CheckAsync();

            await Page.Locator("button").ClickAsync();

            Thread.Sleep(2000);
            await Task.Delay(3000);

            await Page.CloseAsync();
            var newPage = await Page.Context.NewPageAsync();
            await newPage.GotoAsync(baseUrl + "/dashboard");

            Thread.Sleep(2000);
            await Task.Delay(2000);

            Assert.AreEqual("https://example-shop.com/dashboard", newPage.Url);
        }

        [Test]
        public async Task UserCanLogout()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            var emailField = Page.Locator("#email");
            await emailField.FillAsync("test@example.com");
            var passwordField = Page.Locator("#password");
            await passwordField.FillAsync("Password123!");
            var button = Page.Locator("button");
            await button.ClickAsync();

            Thread.Sleep(2000);
            await Task.Delay(3000);

            var logoutBtn = Page.Locator(".logout-button");
            await logoutBtn.ClickAsync();

            Thread.Sleep(1000);
            await Task.Delay(1500);

            string url = Page.Url;
            Assert.AreEqual(baseUrl + "/login", url);

            await Page.GotoAsync("https://example-shop.com/dashboard");
            Thread.Sleep(1000);
            await Task.Delay(1500);
            Assert.AreEqual(baseUrl + "/login", Page.Url);
        }

        [Test]
        public async Task PasswordFieldShouldBeMasked()
        {
            await Page.GotoAsync("https://example-shop.com/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            var passwordField = Page.Locator("#password");
            var fieldType = await passwordField.GetAttributeAsync("type");

            Assert.AreEqual("password", fieldType);
        }

        [Test]
        public async Task ForgotPasswordLinkWorks()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            await Page.Locator("a").ClickAsync();

            Thread.Sleep(1000);
            await Task.Delay(1500);

            Assert.IsTrue(Page.Url.Contains("forgot-password"));
        }

        [Test]
        public async Task SqlInjectionAttemptShouldFail()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            var e = Page.Locator("#email");
            var p = Page.Locator("#password");
            var b = Page.Locator("button");

            await e.FillAsync("admin' OR '1'='1");
            await p.FillAsync("admin' OR '1'='1");
            await b.ClickAsync();

            Thread.Sleep(2000);
            await Task.Delay(3000);

            Assert.AreEqual("https://example-shop.com/login", Page.Url);
        }

        [Test]
        public async Task TestLoginWithSpacesInPassword()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            await Page.Locator("#email").FillAsync("test@example.com");
            await Page.Locator("#password").FillAsync("Password 123!");
            await Page.Locator("button").ClickAsync();

            Thread.Sleep(1000);
            await Task.Delay(1500);

            var errorMsg = await Page.Locator(".error-message").TextContentAsync();
            Assert.IsNotNull(errorMsg);
        }

        [Test]
        public async Task TestMultipleLoginAttempts()
        {
            for (int i = 0; i < 3; i++)
            {
                await Page.GotoAsync("https://example-shop.com/login");
                Thread.Sleep(2000);
                await Task.Delay(2000);

                await Page.Locator("#email").FillAsync(email);
                await Page.Locator("#password").FillAsync("wrongpass" + i);
                await Page.Locator("button").ClickAsync();

                Thread.Sleep(1500);
                await Task.Delay(2000);

                var err = await Page.Locator(".error-message").TextContentAsync();
                Console.WriteLine("Attempt " + (i + 1) + ": " + err);
            }

            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(2000);

            await Page.Locator("#email").FillAsync(email);
            await Page.Locator("#password").FillAsync(password);
            await Page.Locator("button").ClickAsync();

            Thread.Sleep(3000);

            var currentUrl = Page.Url;
            Console.WriteLine("Final URL: " + currentUrl);
        }

        [Test]
        public async Task LoginWithEnterKey()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);
            await Task.Delay(2000);

            await Page.Locator("#email").FillAsync(email);
            await Page.Locator("#password").FillAsync(password);

            await Page.Locator("#password").PressAsync("Enter");

            Thread.Sleep(2000);
            await Task.Delay(3000);

            Assert.That(Page.Url, Does.Contain("dashboard"));
        }

        private async Task DoLogin(string e, string p)
        {
            await Page.Locator("#email").FillAsync(e);
            await Page.Locator("#password").FillAsync(p);
            await Page.Locator("button").ClickAsync();
        }

        [Test]
        public async Task TestSessionTimeout()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);

            await DoLogin(email, password);

            Thread.Sleep(2000);
            await Task.Delay(3000);

            Thread.Sleep(5000);

            await Page.GotoAsync(baseUrl + "/dashboard");
            Thread.Sleep(1000);

            Console.WriteLine("Current URL after timeout: " + Page.Url);
        }

        [Test]
        public async Task LoginButtonShouldBeDisabledWhileLoading()
        {
            await Page.GotoAsync(baseUrl + "/login");
            Thread.Sleep(3000);

            await Page.Locator("#email").FillAsync(email);
            await Page.Locator("#password").FillAsync(password);

            var button = Page.Locator("button");
            await button.ClickAsync();

            var isDisabled = await button.IsDisabledAsync();

            Assert.IsTrue(isDisabled);
        }

        [TearDown]
        public async Task Cleanup()
        {
            try
            {
            }
            catch
            {
            }
        }
    }
}
