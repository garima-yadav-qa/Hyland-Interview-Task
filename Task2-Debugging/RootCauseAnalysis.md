# Root Cause Analysis — `TestCheckoutProcessWithDiscount`

## Summary

The test is failing on CI in roughly 40% of runs for **two distinct reasons**, not one. Pattern analysis across the last ten builds shows it's not random flakiness — there are two reproducible failure modes, each with a clear root cause.

The dev team's claim that *"the code is OK, it's probably a problem with the tests"* is **partially correct and partially wrong**:

- ✅ They're right that the test has genuine design flaws (hard timeout, `double` for currency, client-computed expected price).
- ❌ They're wrong that the backend is fine. The network logs show the `pricing-service` returning a `503 degraded` health check with a 3.2s average response time, which is by itself enough to fail an asynchronous flow that expects sub-3s responses.

Both sides need to fix something. Below is the evidence trail.

---

## The two failure modes

Pattern across builds #1840 – #1868:

| Build | Result | Failure type |
|-------|--------|--------------|
| 1847 | FAIL | Timeout on `.discount-applied` |
| 1851 | FAIL | Price assertion (expected 3999.2, got 4000.0) |
| 1856 | FAIL | Timeout on `.discount-applied` |
| 1862 | FAIL | Price assertion (expected 3999.2, got 3999.0) |
| 1868 | FAIL | Timeout on `.discount-applied` |

Three timeouts and two assertion failures. These are not the same bug.

---

## Failure mode A — Timeout on `.discount-applied`

### What the test does

```csharp
await Page.Locator(".discount-applied").WaitForAsync(new LocatorWaitForOptions
{
    Timeout = 3000
});
```

The test waits up to 3 seconds for the discount-applied element to appear after clicking "Apply".

### What the evidence shows

From `network-logs.json` (build #1868):

- `POST /api/v1/discount/validate` → returned `200` in **2847 ms**
- `POST /api/v1/pricing/calculate` → status `null`, state `PENDING`, **timeout after 3000 ms**
- `GET /api/v1/health/pricing-service` → returned `503` with:

  ```json
  {
    "status": "degraded",
    "message": "High load detected",
    "queueDepth": 234,
    "avgResponseTime": 3200,
    "recommendedAction": "retry_with_backoff"
  }
  ```

The screenshot confirms it: the UI is frozen on **"Processing…"** and **"Discount: Calculating…"** because the second API call (`/pricing/calculate`) never returned.

### Why it fails

This is a textbook race condition. The pricing service's *average* response time is **3200 ms**. The test's timeout is **3000 ms**. The test will fail any time the service responds at or above average — roughly 50% of calls by definition. CI runs are slower than local runs because of shared infrastructure, network hops, and concurrent jobs — which explains why it passes locally and fails on CI ~40% of the time.

The service itself is also telling us it's unhealthy and asking clients to back off with retries. The test ignores that signal entirely.

### Verdict for mode A

**Primary cause: backend degradation.** The pricing service is operating outside its SLA. No test framework should have to compensate for a service that openly reports itself as degraded.

**Secondary cause: test design.** Even if the backend were healthy, hardcoding a 3-second timeout for an external service call is fragile.

---

## Failure mode B — Price assertion mismatch

### What the test does

```csharp
var originalPrice = double.Parse(priceText.Replace("$", "").Replace(",", "").Trim());
// ...
var expectedPrice = originalPrice * 0.8;
Assert.AreEqual(expectedPrice, finalPrice, ...);
```

The test parses the price as a `double`, multiplies by 0.8 client-side, and asserts the server returned the same number.

### What the evidence shows

| Build | Original price | Server returned | Test expected | Difference |
|-------|---------------|-----------------|---------------|------------|
| 1851 | 4999.00 | 4000.00 | 3999.20 | $0.80 |
| 1862 | 4999.00 | 3999.00 | 3999.20 | $0.20 |

The server is returning different rounded values across runs (4000.00 once, 3999.00 another time), while the test always expects 3999.20.

### Why it fails

Two compounding problems:

1. **`double` is the wrong type for currency.** Floating-point can't represent 0.2, 0.8, or most decimal fractions exactly. `4999.0 * 0.8` in `double` produces `3999.2000000000003`, not `3999.2`. This isn't causing *this* particular failure (the gap is bigger than a floating-point epsilon), but it's a latent bug that will eventually cause a different one.

2. **The test recomputes the expected price client-side**, then asserts the server matches. This assumes the test and the server apply identical rounding rules. They clearly don't — the server is rounding to the nearest dollar in build #1851 and to the nearest dollar-down in #1862. That inconsistency on the server side is suspicious in its own right, but the test shouldn't be the authority on what the discounted price *should* be; the server is.

### Verdict for mode B

**Primary cause: test design.** The test shouldn't be computing the expected price independently of the server. It should either:

- Read the discounted price from a contract / fixture (e.g., the discount API response itself returns the calculated price), or
- Assert a tolerance (`Within(0.01)`) if some rounding drift is acceptable.

**Secondary concern: backend inconsistency.** The server returning `4000.00` in one run and `3999.00` in another for the same input is itself a bug worth a Jira ticket. The test is correct to flag that something is off — it's just flagging it the wrong way.

---

## Root cause summary

| Layer | Issue | Owner |
|-------|-------|-------|
| Backend | `pricing-service` degraded, 3.2s avg response, returns 503 health check | Dev / SRE |
| Backend | Inconsistent rounding in price calculation | Dev |
| Test | 3s timeout below known service response time | QA |
| Test | `double` used for currency | QA |
| Test | Expected price computed client-side instead of verified against contract | QA |

The dev team owns the first two. We own the last three.

---

## Recommended fixes

### Immediate (unblock CI today)

1. **Increase the discount-applied timeout to 10 seconds**, matching the order-confirmation timeout already in the file. This is a band-aid, not a fix, but it stops the bleeding.
2. **Switch the price assertion to use a tolerance**:

   ```csharp
   Assert.That(finalPrice, Is.EqualTo(expectedPrice).Within(0.01));
   ```

3. **File a Jira ticket** on the pricing-service degradation, attaching the network log and health check response. Don't let this get buried as "test flakiness."

### Short-term (this sprint)

4. **Convert money handling from `double` to `decimal`** throughout the test:

   ```csharp
   var originalPrice = decimal.Parse(priceText.Replace("$", "").Replace(",", "").Trim());
   ```

5. **Stop recomputing the expected price client-side.** Read the discount response from the API call (Playwright's `Page.WaitForResponseAsync(...)` can capture it) and assert the UI matches the value the server claims, not the value the test thinks the server should return.

6. **Add a retry policy** for the discount step — Playwright's `Expect()` with a generous polling timeout, or NUnit's `[Retry(2)]` on the test. Retry masks symptoms but is appropriate here because the underlying service openly recommends `retry_with_backoff`.

### Long-term (architecture / process)

7. Get pricing-service into an SLA with measurable response-time targets. CI tests shouldn't be the first place we find out a service is unhealthy.
8. Add a contract test for the discount API that runs independently of the UI flow, so when this fails again we know immediately whether the bug is in the API or the UI.
9. Hook the test suite into the service health endpoint as a pre-flight check — if pricing-service is degraded before the run, mark the test as `Inconclusive` rather than `Failed`. That keeps the signal clean.

---

## Verification plan

After applying fixes 1–3:

- Run the test 20 times on CI back-to-back. Pass rate should rise from the current 50% to ≥95%.
- If it doesn't, the remaining failures will be much more diagnosable because we've removed the two known issues.

After applying fixes 4–6:

- The test should produce identical results regardless of how the server rounds, because it's reading the expected value from the server response.
- Any future price drift will surface as a backend assertion (`server returned $4000, but discount API claimed $3999.20`) rather than a confusing test failure.

---

## Suggested response to the dev team

> Pulled together the evidence for the checkout flakiness. There are actually two separate failure modes here, not one.
>
> **The timeout failures are a real backend issue** — the network logs show `pricing-service` returning a 503 health check with a 3.2s average response time, which is above our test's 3s timeout. The service is asking clients to back off and retry. I've raised [JIRA-XXXX] for that.
>
> **The price-mismatch failures are a real test issue** — we're computing the expected discount client-side in `double`, which is fragile, and we should be reading the expected value from the discount API response instead. I'll have a PR up for that today.
>
> Both fixes need to land for the suite to be reliable. Happy to walk through the network logs together if useful.

This frames the disagreement as a shared problem with a shared fix, not a blame exchange. That's usually the difference between getting the backend ticket prioritised and getting it dismissed as "QA noise."
