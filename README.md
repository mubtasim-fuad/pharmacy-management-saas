# MedLedger — Pharmacy Management SaaS

A portfolio MVP for a small pharmacy in Bangladesh. The Angular interface supports a browser-only interactive demo; the ASP.NET Core API and PostgreSQL schema support separate pharmacy accounts with persistent records. **The demo is sample data, not a connected pharmacy service.** Do not enter patient or real business information in it.

## What works

- Create a pharmacy account and sign in when the API is configured. Bearer tokens expire after eight hours; every catalog, transaction and report query is scoped to the account's pharmacy.
- Add and edit medicines, SKU, selling price and reorder threshold. A new medicine begins with zero stock.
- Record a purchase with supplier, quantity and unit cost. The transaction increases stock and captures the purchase total.
- Record a sale with customer, quantity and the price and cost at the time of sale. An atomic conditional update prevents negative stock. The entire transaction rolls back if any line fails.
- See low stock alerts, recent transactions and reports for 7, 30 or 90 days.
- Explore those flows in the live frontend without an account. Demo changes persist only in the current browser and can be reset.

The API supports up to 50 distinct lines per sale or purchase; the current UI submits one medicine per transaction. This is a portfolio MVP, not a production pharmacy system: it has no batch/expiry tracking, invoice numbering, VAT handling, email verification, password recovery, payment integration, or audit trail for user actions. The demo should not be used for regulated dispensing.

| Layer | Stack |
| --- | --- |
| Web | Angular 22, TypeScript, signals, reactive forms |
| API | ASP.NET Core 10, EF Core, JWT authentication |
| Database | PostgreSQL 17 |
| CI | GitHub Actions: Angular and .NET builds plus account/stock smoke test |

## Run the full stack locally

Requirements: Node.js 24, .NET 10 SDK, Docker Compose (or PostgreSQL 17).

```bash
docker compose up -d db
cd backend
export ConnectionStrings__PharmacyDatabase='Host=localhost;Port=5432;Database=pharmacy_saas;Username=pharmacy;Password=pharmacy_dev'
export JWT__Key='replace-this-with-a-long-random-secret-at-least-32-characters'
export Cors__AllowedOrigins='http://localhost:4200'
dotnet run
```

In another terminal:

```bash
cd frontend
npm ci
npm start
```

Open `http://localhost:4200`. Choose **Create pharmacy** to use the persistent API or **Open interactive demo** for sample data. Health endpoint: `http://localhost:5075/health`. For PowerShell, replace `export KEY=value` with `$env:KEY='value'`.

The Docker entrypoint runs `001_initial.sql` and `002_transactions_and_accounts.sql` on a **new** database volume. If you already created a volume from the earlier inventory MVP, run the upgrade manually from the repository root: `PGPASSWORD=pharmacy_dev psql -h localhost -U pharmacy -d pharmacy_saas -f database/002_transactions_and_accounts.sql`. See [database/README.md](database/README.md) for existing records.

## API routes

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/auth/register`, `/api/auth/login` | Pharmacy account and token |
| GET, POST | `/api/medicines` | Catalog (protected) |
| PUT | `/api/medicines/{id}` | Edit product information (protected) |
| GET, POST | `/api/purchases` | Recent purchases / receive stock (protected) |
| GET, POST | `/api/sales` | Recent sales / deduct stock (protected) |
| GET | `/api/reports/summary?days=30` | Period totals and inventory health (protected) |
| GET | `/health` | Database connectivity |

Requests to protected routes need `Authorization: Bearer <token>`. Example payloads:

```json
{"name":"Paracetamol 500 mg","sku":"P500","salePrice":5.00,"reorderLevel":10}
{"supplier":"Supplier A","items":[{"medicineId":1,"quantity":20,"unitCost":3.00}]}
{"customer":"Walk-in","items":[{"medicineId":1,"quantity":2}]}
```

## Deploy

The static Angular frontend and the Dockerized .NET API are separate services. The frontend can be deployed on Vercel with **Root Directory `frontend`**; its `vercel.json` sets the build and output paths. With `frontend/public/config.js` left empty, the site is an explicitly labeled browser demo.

To enable real accounts, provision PostgreSQL and a host that runs `backend/Dockerfile` with the **repository root** as Docker build context. Apply `database/001_initial.sql`, then `database/002_transactions_and_accounts.sql` once to the database. Set `ConnectionStrings__PharmacyDatabase`, a random `JWT__Key` of at least 32 bytes, and `Cors__AllowedOrigins=https://YOUR-FRONTEND-DOMAIN`. Use TLS for both services. Replace `window.PHARMACY_API_URL` in `frontend/public/config.js` with the HTTPS API origin (no trailing slash) and redeploy the frontend. Never put database credentials or the JWT signing key in the frontend or repository.

The CI workflow builds both layers and exercises tenant isolation, a purchase, a sale, an insufficient stock rollback and report totals against disposable PostgreSQL. The frontend build can be run locally with `npm ci && npm run build` from `frontend/`.

## Repository layout

- `frontend/` — Angular app and Vercel configuration
- `backend/` — ASP.NET Core API and Dockerfile
- `database/` — versioned PostgreSQL scripts
- `scripts/smoke_test.py` — API integration smoke test used by CI
- `screenshots/` — interface screenshots

The code started as my Angular inventory learning exercise and was extended into a full stack portfolio MVP with AI assistance. See the source and CI checks for the implemented scope.
