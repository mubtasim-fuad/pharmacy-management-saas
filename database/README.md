# Database

`001_initial.sql` is the first versioned PostgreSQL schema script for the inventory MVP. Docker runs it only when creating a fresh `pharmacy_db` volume. It creates the `medicines` table with a nonnegative stock constraint and a name index.

Later schema changes should be new numbered scripts or EF Core migrations. The sales, purchasing, multi-tenant, reporting and alert schemas have not been implemented.
