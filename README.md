# MedLedger — local pharmacy manager

A simple app for keeping a medicine catalog, receiving stock, recording sales, and seeing stock and sales reports. It runs on your own computer. Records are saved in the SQLite file `backend/pharmacy.db`; the API creates it automatically on first run.

## Run on Windows

Install [Node.js 24](https://nodejs.org/) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Then download this repository (GitHub **Code → Download ZIP**) and extract it. Double-click `run-windows.bat` in the extracted folder. It opens an API window and a web window. When the web window says it is ready, visit **http://localhost:4200**.

Keep both windows open while using the app. The first start installs the web packages and can take a minute. On later starts it reuses them. To stop the app, close both windows.

## Run in two terminals (Windows, macOS, or Linux)

Install Node.js 24 and the .NET 10 SDK. In the first terminal:

```sh
cd backend
dotnet run
```

In a second terminal from the repository root:

```sh
cd frontend
npm ci
npm start
```

Open **http://localhost:4200**. On later runs, `npm start` is enough. The API health check is at **http://localhost:5075/health**. If you opened the page before the API was ready, use **Retry**.

## Use

1. Add a medicine with a selling price and reorder level. Its stock starts at zero.
2. Record a purchase to add quantity and set the current unit cost.
3. Record a sale to deduct quantity. The API rejects a sale if there is not enough stock.
4. See low stock alerts, recent transactions, and 7/30/90-day reports.

There is one local workspace, without accounts or passwords. Each purchase and sale form records one medicine at a time. SKU is optional but must be unique if entered. Amounts are in Bangladeshi taka.

Your data stays in `backend/pharmacy.db`. Back up that file while the API is **closed**; restoring it restores your records. Deleting it while the API is closed starts a fresh workspace on next launch. The file is ignored by Git.

This is an inventory and sales MVP for local use, not a regulated dispensing system. It does not track batches, expiry dates, prescriptions, taxes, or audit logs. It has no login, so do not expose port 5075 to other computers or enter patient details.

## Render portfolio demo

`render.yaml` and the root `Dockerfile` package the Angular interface and the API as **one** Render web service. The free demo starts with sample medicines, and the browser calls the API at the same site address. The local two-terminal setup above still works.

[Deploy the free demo to Render](https://render.com/deploy?repo=https%3A%2F%2Fgithub.com%2Fmubtasim-fuad%2Fpharmacy-management-saas) using the reviewed `render.yaml` Blueprint. This creates a free service named `medledger-demo`; later code changes can be deployed manually from Render.

Render's free web services erase local files when they sleep or restart. **The online demo is temporary and shared among visitors.** Changes made there do not sync with `backend/pharmacy.db` on your PC. Do not enter real customer, patient, or business information. A persistent online pharmacy would need access control and durable storage before it could be used for real records.

## Development

- `frontend/`: Angular 22 interface
- `backend/`: ASP.NET Core 10 API and EF Core SQLite storage
- `scripts/smoke_test.py`: API workflow check used by GitHub Actions

To run the frontend build: `cd frontend && npm ci && npm run build`. The database path can be changed by setting the `DatabasePath` environment variable before starting the API; relative paths resolve from the API's working directory.
