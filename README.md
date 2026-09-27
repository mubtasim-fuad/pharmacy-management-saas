# Pharmacy Management SaaS — Inventory MVP

An in-progress Angular + ASP.NET Core project for pharmacy workflows in Bangladesh. This repository currently demonstrates a **medicine inventory slice**: list and search medicines, add stock entries, show low-stock counts, and record a one-unit sale with an atomic database decrement.

The frontend grew from my Angular learning exercise and was upgraded to Angular 22. It can run in an explicitly labeled sample-data mode when the API is offline; those changes reset on refresh. With the API and PostgreSQL running, changes persist. This is a development portfolio project, not a deployed pharmacy product.

There is no verified screenshot of this version yet. `screenshots/` records what to capture after running the UI.

**Live demo:** not deployed yet. Run the frontend locally for a demo preview.

## Stack and current scope

| Part | Technology | Implemented |
| --- | --- | --- |
| Frontend | Angular 22, TypeScript, signals, reactive forms, HTTP | Catalog, search, stock summary, add medicine, sell one |
| API | ASP.NET Core 10, C#, EF Core | List, create, atomic stock decrement |
| Database | PostgreSQL | `medicines` table and versioned initial SQL script |

**Planned:** purchasing, complete sales transactions, expiry and batch tracking, authentication, multi-tenant isolation, reports, and alerts. The current `sell one` action changes inventory only; it does not create a sale invoice or transaction record.

## Run locally

Requirements: Node.js compatible with Angular 22, .NET 10 SDK, Docker with Compose (or a local PostgreSQL instance).

1. From the repository root, start PostgreSQL:

   ```bash
   docker compose up -d db
   ```

   A fresh Docker volume runs `database/001_initial.sql` once. For an existing local PostgreSQL database, create `pharmacy_saas` and run that SQL file manually.

2. In a separate terminal, set the **local demo** connection string and start the API:

   ```bash
   cd backend
   export ConnectionStrings__PharmacyDatabase='Host=localhost;Port=5432;Database=pharmacy_saas;Username=pharmacy;Password=pharmacy_dev'
   dotnet run
   ```

   On PowerShell, set `$env:ConnectionStrings__PharmacyDatabase = 'Host=localhost;Port=5432;Database=pharmacy_saas;Username=pharmacy;Password=pharmacy_dev'` before `dotnet run`. API base URL: `http://localhost:5075`.

3. In another terminal, start Angular:

   ```bash
   cd frontend
   npm install
   npm start
   ```

   Open `http://localhost:4200`. The status pill should say **Connected to API**. If it says **Demo preview**, the app is showing local sample records and will not persist changes. Refresh after starting the API.

## API

| Method | Path | Result |
| --- | --- | --- |
| GET | `/api/medicines` | Medicine list |
| GET | `/api/medicines/{id}` | One medicine |
| POST | `/api/medicines` | Add `{ "name": "Paracetamol", "stock": 12 }` |
| PATCH | `/api/medicines/{id}/sell` | Decrement stock by one, or reject when empty |
| GET | `/health` | Process health |

The local Docker password in `compose.yaml` is only for development. Configure a separate secret and tighten CORS/authentication before any real deployment.

## Repository layout

- `frontend/` — Angular application
- `backend/` — ASP.NET Core API and EF Core model
- `database/` — initial PostgreSQL schema script
- `screenshots/` — local UI capture when available
- `compose.yaml` — local PostgreSQL setup

## Project status

In progress. This repository includes an AI-assisted starter API and database layer extending my Angular learning exercise. The frontend build can be verified independently; the full stack should be run with the setup above before being presented as a production system.
