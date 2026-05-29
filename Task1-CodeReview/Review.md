# Code Review — LoginTests.cs

## Overview

The suite covers a sensible range of login scenarios: happy path, validation, security, and session behaviour. The intent is in the right place. In its current form, however, I would not approve this PR. The issues fall into three buckets — **test reliability** (waits and race conditions), **test correctness** (assertions that don't actually assert), and **maintainability** (duplication, weak locators, no page object structure).

Findings are grouped by priority. Critical items should block merge; the rest can land as a follow-up PR.

---

## Critical

### 1. Hard waits everywhere — and stacked on top of each other

Every test mixes `Thread.Sleep(...)` with `await Task.Delay(...)` back-to-back:

```csharp
Thread.Sleep(3000);
await Task.Delay(2000);
```

Two problems with this:

- `Thread.Sleep` blocks the calling thread synchronously inside an `async` method. In a test runner that schedules tasks across the synchronisation context, this can starve other operations and on CI is a known source of flakiness.
- Stacking Sleep + Delay is dead code. The second wait runs in series for no reason. The suite spends roughly 5 seconds idling on every test for no benefit.

More importantly, Playwright already auto-waits for elements to become actionable. These sleeps are *masking* timing bugs, not fixing them. Suites built on hard waits either run slow or break randomly.

**Fix:** remove every `Thread.Sleep` and `Task.Delay`. Where a wait is genuinely needed, use Playwright's built-in waiters — `Page.WaitForURLAsync(...)`, `Locator.WaitForAsync(...)`, or the `Expect()` API, which retries until the condition holds or the timeout fires.

### 2. A try-catch that swallows the assertion

```csharp
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
```

If the error message never appears, the test passes. If the locator throws, the test passes. This test cannot fail — and a test that cannot fail provides no value.

**Fix:** remove the try-catch and the null check entirely. Replace with:

```csharp
await Expect(Page.Locator(".error-message")).ToContainTextAsync("Invalid");
```

### 3. Tests with no assertions at all

`TestMultipleLoginAttempts` and `TestSessionTimeout` only write to the console. They will always pass regardless of how the application behaves. Same root cause as #2: missing assertions.

**Fix:** add explicit assertions for the behaviour each test claims to verify — e.g., account lockout after N failed attempts, redirect to `/login` after session expiry.

### 4. Locators are far too generic

`Page.Locator("button")` matches every button on the page. `Page.Locator("a")` matches every link. The "forgot password" test is currently clicking *some* link, not necessarily the right one. These will break the moment the page gains another button or link — which is roughly every sprint.

**Fix:** prefer stable selectors in this order: `GetByRole`, `GetByTestId`, then a specific ID. For the login button:

```csharp
Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" })
```

---

## High

### 5. No Page Object Model

The same five locators (`#email`, `#password`, `button`, `.error-message`, etc.) appear in almost every test. When a developer renames the email field, twelve tests break in twelve places. A `LoginPage` class encapsulating selectors and actions (`GotoAsync`, `LoginAsync(email, password)`, `GetErrorMessageAsync`) would cut this file roughly in half and isolate future maintenance to a single file.

### 6. Hardcoded credentials and base URL

`email`, `password`, and `baseUrl` are hardcoded as instance fields. This prevents running the suite against dev / staging / prod, and credentials in source control is a security smell even for test accounts.

**Fix:** read from `appsettings.json` or environment variables, bound through `IConfiguration` in `[OneTimeSetUp]`.

### 7. Inconsistent use of `baseUrl`

Some tests use `baseUrl + "/login"`, others use the hardcoded literal `"https://example-shop.com/login"`. Changing the environment will only update half the tests. This is exactly the kind of thing that causes a confusing 50% failure rate after a config change.

### 8. Mixing classic and constraint-model assertions

The file uses both `Assert.AreEqual(...)` (classic model) and `Assert.That(..., Does.Contain(...))` (constraint model). NUnit's classic assertions are being phased out in NUnit 4. Pick one and apply it across the file — I'd lean toward the constraint model for readability and future-proofing.

---

## Medium

### 9. Race condition in `LoginButtonShouldBeDisabledWhileLoading`

`IsDisabledAsync()` is checked immediately after the click. If the auth call resolves before the check runs, the assertion fails for the wrong reason. Either intercept the request with `Page.RouteAsync(...)` and hold the response open long enough to observe the disabled state, or use `Expect(button).ToBeDisabledAsync()` which polls.

### 10. Stale `Page` reference in `RememberMeCheckboxWorks`

```csharp
await Page.CloseAsync();
var newPage = await Page.Context.NewPageAsync();
```

`Page.Context` is read off a page that's just been closed. Grab the context reference *before* closing. Beyond that, opening a new tab in the same context does not really test "remember me" — that just tests in-session cookie persistence. A genuine "remember me" test closes the entire browser context and reopens it with the same storage state.

### 11. Variable naming

`var e = Page.Locator(...)`, `var p = ...`, `var b = ...` make the test harder to scan. The cost of `emailField`, `passwordField`, `submitButton` is zero.

### 12. Test naming inconsistency

Three conventions are in use: `UserCanLoginWithValidCredentials`, `TestLoginWithSpacesInPassword`, `LoginWithEnterKey`. Pick one and apply it consistently — `Behaviour_Condition_ExpectedResult` is a common choice. Also drop the `Test` prefix; the `[Test]` attribute already tells the runner.

---

## Low / nits

- **Empty `TearDown`** with a no-op try-catch. Remove it; if cleanup isn't needed, the attribute isn't needed either.
- **Magic numbers** for sleep durations. Moot once sleeps are removed, but if any explicit timeout *is* genuinely needed, name it as a constant.
- **`DoLogin` helper** is private and used once. Either delete it or — better — promote it into the Page Object alongside the rest of the login actions.

---

## What "good" looks like

After the fixes above, `UserCanLoginWithValidCredentials` reads like this:

```csharp
[Test]
public async Task ValidCredentials_RedirectsToDashboardAndShowsUserName()
{
    var loginPage = new LoginPage(Page);
    await loginPage.GotoAsync();
    await loginPage.LoginAsync(_config.TestUser.Email, _config.TestUser.Password);

    await Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
    await Expect(Page.Locator(".user-name")).ToHaveTextAsync("Test User");
}
```

No sleeps. No duplicated locators. Three lines that read like a behaviour spec and fail meaningfully when something breaks.

---

## Summary

Approve once the four **Critical** items are addressed — the suite as it stands gives false confidence rather than real coverage. The **High** and **Medium** items should land in a follow-up PR before any new tests are added, since the longer the duplication and weak locators live in the codebase, the more painful the eventual refactor becomes.
