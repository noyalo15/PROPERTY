# NavEstate

Property management for the Kenyan market, by Dynamicnav Systems Limited. Residential, commercial
and mixed-use buildings, KES and USD, with **Microsoft Dynamics 365 Business Central 13** (on-premise,
C/AL) as the ledger: every invoice, credit note, receipt and deposit posts to BC through the
`NavEstate` SOAP codeunit (52140613). NavEstate keeps no general ledger.

| Part | Stack |
|---|---|
| Database | Microsoft SQL Server 2019 (database `NavEstate`, schemas `core`, `tenancy`, `billing`, `pay`, `ops`, `app`) |
| API | ASP.NET Core 8, EF Core 8 migrations, ASP.NET Core Identity, Hangfire (SQL Server storage) |
| Web | React 18 + TypeScript + Tailwind (Vite), served by the API from `wwwroot` |
| Hosting | IIS on Windows Server 2022 (the Contabo server, beside BC 13) – see [docs/DEPLOY.md](docs/DEPLOY.md) |

## Build plan

The product spec has six stages. This repository is at **stage 2**.

| Stage | Scope | Status |
|---|---|---|
| 1 | App shell, auth and roles (2FA for staff, phone OTP for tenants), setup pages, properties and units (with Excel/CSV import), tenants and the tenant 360 page, full SQL Server schema, BC sync queue | **Done** |
| 2 | Lease wizard, rent schedules and escalation, notices | **Done** |
| 3 | Billing runs, water readings, penalties, credit notes (PostInvoice, PostCreditMemo) | Next |
| 4 | Payments, M-PESA Daraja C2B and STK push, bank statements, cheques, deposits | |
| 5 | Maintenance, transfers, move-outs, advice slips | |
| 6 | Dashboard, tenant and landlord portals, reports, BC sync log extras | |

## What stage 1 includes

- **Data model**: every table in the spec, translated to SQL Server (sequential GUID keys,
  `decimal(18,2)` money, CHECK constraints for every status/type list, JSON columns with `ISJSON`,
  persisted computed `bills.balance` and `water_readings.consumption`, rowversion on bills, payments,
  leases and the sync queue). Gaps the spec implies are filled: `bank_statement_uploads`,
  `user_property_assignments`, `bills.amount_credited`, `water_tariff_bands`, `maintenance_photos`,
  `payment_channels`, `notification_templates`, `number_series`.
- **Row-level security** in SQL Server on properties and units, keyed on user assignments
  (by landlord or by property). The API sets `SESSION_CONTEXT` on every connection; super admins and
  background jobs bypass. Tenants and leases follow the visible units.
- **Audit log**: every insert, update and delete (except the sync queue) is written to
  `app.audit_log` with old and new values as JSON and the user.
- **Numbering**: gap-free `T0nnnn`, `LSE-nnnn` and the monthly `INV-yyyy-mm-nnnnnn` style series,
  locked inside the saving transaction.
- **Auth**: seven roles. Staff sign in with email and password plus TOTP (enforced by
  `Auth:RequireStaffTwoFactor`); tenants sign in with an SMS code (Africa's Talking); landlords with
  email and password. Lockout after 5 failures, rate limits on sign-in and OTP, HttpOnly SameSite
  cookie plus a required `X-NavEstate` header on writes.
- **BC sync**: `app.bc_sync_queue` rows are written in the same transaction as the business change.
  A Hangfire job drains the queue every minute, one call at a time, honouring `depends_on_id`
  (tenant before bill, bill before payment), retrying after 1, 5, 15 and 60 minutes (5 attempts),
  and writing BC document numbers back. Ping every 5 minutes drives the "BC 13 connected" pill; the
  USD rate is read from BC daily at 06:00. With `BusinessCentral:UseMock=true` (the default until
  the server is ready) a mock answers like the codeunit, idempotent on `externalRef`.
- **Demo data** (development only): 2 landlords; Riverside Apartments Kilimani (120 units),
  Mombasa Road Business Park (42 units, USD), Ngong View Court Karen, Westlands Corner Plaza (mixed)
  and Thika Greens Estate; about 220 tenants with Kenyan names, active leases, charges, meters and
  deposits. Bills and payments are seeded once stages 3 and 4 exist.

## What stage 2 includes

- **Lease wizard** (Leases → New lease, or "Let this unit" on a vacant unit, or "New lease" on a tenant):
  tenant → vacant unit → term, currency, billing day and frequency → charges copied from the unit
  defaults → deposits, grace days, penalty rule, VAT, WHT → escalation rule → review. Save as a draft
  or activate. Blacklisted tenants need an explicit override.
- **Activation** sets the unit occupied, creates the rent and water deposit records (held, awaiting
  receipt) and queues UpsertTenant again so BC gets the new PROPERTY and UNIT dimensions. A filtered
  unique index stops two active leases on one unit.
- **Rent schedule (rule 2)**: rows for the whole term. Fixed % compounding gives 4,200.00 → 4,410.00 →
  4,630.50 → 4,862.03; also simple %, fixed amount, stepped and market review (alert 60 days before).
  A daily Hangfire job (00:30 Nairobi) marks rows "notice due" `notice_days` before they start and
  applies approved rows on their date (the RENT charge follows). The accountant approves with
  **Approve & send notice**, which sends the `escalation_notice` SMS and records it in `ops.notices`.
  Unapproved escalations never take effect; the accountant can also change or decline a future row.
- **Lease detail page**: lease card (Edit, Statement, Transfer unit for stage 5, Renew, End lease), the
  rent schedule with the yellow notice banner, recurring charges with the BC G/L account from the
  charge code, deposits and notices. Active leases keep tenant, unit, dates, currency and rent fixed.
- **Renewal** extends the end date and keeps the schedule, adding rows for the extra term.
  **Ending** a lease before its end date marks it terminated (on or after: ended), frees the unit and
  drops later schedule rows. Deposits stay held for the stage 5 move-out.
- **Leases list** with filters for escalations due, expiring in 30/60/90 days, status, property and currency.

## Run it locally

Requirements: .NET 8 SDK, Node.js 22, and SQL Server 2019 or later. The quickest SQL Server is Docker:

```bash
docker run -d --name navsql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Dev_Passw0rd!' \
  -e MSSQL_COLLATION=SQL_Latin1_General_CP1_CI_AS -p 1433:1433 mcr.microsoft.com/mssql/server:2019-latest
```

```bash
# API on http://localhost:5080 (creates and seeds the NavEstate database on first start)
cd src/NavEstate.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5080

# Web on http://localhost:5173 (proxies /api to the API)
cd web
npm install
npm run dev
```

Demo sign-ins (development seed only, password `NavEstate#2026`): `admin@`, `pm@`, `accounts@`,
`cashier@`, `caretaker@`, `landlord@` and `tenant@navestate.local`. In development the tenant SMS
code is not sent; staff 2FA is off (`Auth:RequireStaffTwoFactor=false`).

## Tests

```bash
dotnet test        # unit tests + API tests against a throwaway SQL Server database
cd web && npm test # front-end tests
```

The API tests create and drop their own database on the server in `NAVESTATE_TEST_SQL`
(default: the Docker container above). CI runs both on every pull request with a SQL Server 2019
service container.

## Repository layout

```
src/NavEstate.Api/        ASP.NET Core API (Domain, Data + Migrations, Auth, Bc, Features)
tests/NavEstate.Tests/    xUnit tests
web/                      React front end (builds into src/NavEstate.Api/wwwroot)
deploy/                   SQL script for the database and login, deploy.ps1 for IIS
docs/DEPLOY.md            Server setup, settings, BC connection, backups
```

## Business Central

The BC side is the C/AL extension `NavEstate_BC13_CAL_52140613.txt` (version NAVESTATE1.1):
tables and pages 52140613 to 52140616, codeunit 52140613 *NavEstate Integration* (published as the
`NavEstate` SOAP service) and 52140614 *NavEstate Post Handler*. Functions: Ping, UpsertTenant,
PostInvoice, PostCreditMemo, PostReceipt, ReverseReceipt, PostDeposit, TransferTenantUnit,
GetExchangeRate, GetCustomerBalance. See [docs/DEPLOY.md](docs/DEPLOY.md#4-connect-business-central-13).
