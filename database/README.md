# PostgreSQL schema

`001_initial.sql` creates the original inventory table. `002_transactions_and_accounts.sql` adds tenants, users, product fields, sales, purchase records and indexes. Docker Compose applies both, in order, only on a fresh volume. CI applies both in order on a fresh PostgreSQL instance.

If the older database volume already exists, back it up, then run `002_transactions_and_accounts.sql` once with `psql`. The script is safe to rerun. Existing medicine rows are assigned to a legacy tenant (ID 1) with no login; they are preserved but cannot be accessed by a newly registered pharmacy. If you need them in a new account, create the account, inspect both tenant IDs, and update those medicine rows to the intended tenant with SQL after verifying ownership. Do not guess an ID on a shared deployment.

The schema keeps a current stock count on medicines and immutable price/name snapshots on transaction lines. The API uses a database transaction and conditional update for each sale to prevent stock from dropping below zero. Product cost is the latest purchase cost, so stock valuation is an estimate. Batch-specific expiry, supplier ledgers and tax invoices remain future work.
