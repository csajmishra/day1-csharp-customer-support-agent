# Repository guidance

- Keep business rules in `Day1Agent.Core`; keep Azure SDK details in the console adapter.
- Use synthetic data only. Never add secrets, customer personal data, API keys, or connection strings.
- Require a separate, explicit console approval before any write tool runs. Never let model arguments represent approval.
- Scope every order operation to the current customer and return a generic not-found result for inaccessible orders.
- Use cancellation tokens for asynchronous work and return structured, safe tool results.
- Do not log prompts, order contents, customer identifiers, credentials, or model responses.
- Add deterministic tests that need no Azure account or network for core behavior.
- Preserve nullable analysis and resolve warnings instead of suppressing analyzers.
