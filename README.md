# Hyland Software — Test Engineer 3 Take-Home Assessment

Submission by **Kumari Garima Yadav**

## Repository Structure

| Folder | Task | Deliverable |
|--------|------|-------------|
| `Task1-CodeReview/` | Code review of `LoginTests.cs` | `Review.md` with 12 prioritised findings |
| `Task2-Debugging/` | Root cause analysis of flaky checkout test | `RootCauseAnalysis.md` identifying two distinct failure modes |
| `Task3-Integration/` | Full-stack integration test implementation | 5 implemented C# files + design notes |

## How to Review

Each task folder contains its own README or analysis document explaining the approach and design decisions. Original files provided by Hyland are included alongside my work for reference.

## Approach

- **Task 1** — Reviewed as a senior QA would approve or block a PR: prioritised findings (Critical → Low), each citing exact code and offering a concrete fix.
- **Task 2** — Treated as a real production incident: walked through CI logs and network traces to separate genuine backend issues from test design flaws, with diplomatic recommendations for both teams.
- **Task 3** — Implemented end-to-end coverage with idempotent tests, decimal arithmetic for currency, and a Page Object Model that keeps tests readable and resilient.

## Contact

Available immediately. Reach me at garimay3004@gmail.com.
