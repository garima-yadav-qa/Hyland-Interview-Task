# Task 3 — Full-Stack Integration Testing

This task wires together the three layers of the system: REST API → web UI → PostgreSQL. The skeleton provided four classes full of `NotImplementedException` calls; this is my completed solution.

## Files implemented

| File | What it does |
|------|-------------|
| `ApiClient.cs` | HTTP wrapper around the promotion API. Owns its own `HttpClient`. |
| `DatabaseHelper.cs` | PostgreSQL access via Npgsql. Manages connection lifecycle. |
| `CheckoutPage.cs` | Playwright Page Object for the checkout UI. |
| `PromotionFlowTests.cs` | The four required tests, plus shared setup and teardown. |
| `TestConfig.cs` | Strongly-typed loader for `appsettings.json` (new helper). |

## Test scenarios

1. **Happy path** — create promotion via API → apply via UI → verify the order and audit log in the database.
2. **Invalid code** — a code that doesn't exist is rejected and the price is unchanged.
3. **Expired code** — a promotion with `validUntil` in the past is rejected with an expiry message.
4. **Wrong category** — a BOOKS-category code applied to an ELECTRONICS product is rejected.

## Key design decisions

**Idempotency through unique codes, not a global reset.** Each test suffixes its promo code with a short random token (`SPRING25_A3F92E`) so two test runs — or two parallel runs — can't collide on the `code UNIQUE` constraint in the `promotions` table. I avoided using `POST /admin/reset` in setup because that's a global wipe that would break parallel execution against a shared database. Per-resource cleanup in `TearDown` is safer.

**Cleanup tracks what each test created.** Tests append created promotion and order IDs to `_createdPromotionIds` and `_createdOrderIds`. `TearDown` removes them in dependency order (orders first, then promotions) so the foreign-key cascade does the right thing. All cleanup is wrapped in try/catch — a cleanup failure shouldn't mask the actual test failure.

**No `Thread.Sleep` anywhere.** Every wait uses Playwright's auto-waiting (`Locator.WaitForAsync`) or NUnit's constraint-based polling. Hard sleeps slow down green builds and fail to fix flaky ones.

**Decimal, not double, for currency.** Money is parsed straight from the UI text into `decimal` and stays that way through to the database comparison. Float currency is a well-known footgun — using it in checkout tests would have been an obvious flaw on review.

**Money assertions use tolerance.** `Is.EqualTo(expected).Within(0.01m)` — one cent of slack absorbs any rounding the database or UI might apply, without masking a real bug. A real pricing bug would be a difference of dollars, not pennies.

**Mixed-case JSON handled per call, not globally.** The API is slightly inconsistent: `POST /admin/promotions` returns camelCase (`promotionId`), but `GET /admin/promotions/{id}` returns snake_case from the underlying Postgres rows. The `ApiClient` parses each response on its own terms — `JsonPropertyName` attributes on `PromotionResponse` for the GET, a small manual parse for the POST.

**Page Object owns the wait, not the test.** `ApplyPromoCodeAsync` doesn't return until the `#promo-message` div is visible, so tests never have to think about timing. The UI uses the same div for success and error states (just swapping class names), so visibility of that single element is the most reliable "the round-trip is done" signal.

## How to run

```bash
# from 03-full-stack-integration/
docker-compose up -d
curl http://localhost:3000/health   # wait until it returns "healthy"

cd tests
dotnet test
```

## What's deliberately not in scope

A few things I left out and why:

- **Retry policy on API calls.** Polly or similar would be appropriate in production, but adds dependencies and noise for a take-home. The 30-second `HttpClient` timeout covers staging-level slowness.
- **Parallel-fixture isolation.** All four tests share one fixture, but they write to disjoint resources (unique codes), so they'd be safe to mark `[Parallelizable]` if needed. I left the attribute off to keep the default behaviour predictable.
- **Auto-screenshot on failure.** The config has `ScreenshotOnFailure: true` but Playwright's `PageTest` doesn't wire that automatically. In a real project I'd add it via a custom base class or hook; out of scope here.
- **Contract test for the API itself.** This suite tests end-to-end. A separate API-level contract test would catch backend regressions faster, but is its own deliverable.

## Note on the skeleton's `PromotionResponse`

The skeleton declared `PromotionResponse` with PascalCase C# property names but the API returns mixed casing. I added `JsonPropertyName` attributes mapping each property to its snake_case wire name, which is the standard way to handle this in `System.Text.Json` without introducing Newtonsoft.Json as a dependency.
