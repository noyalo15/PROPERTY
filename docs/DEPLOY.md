# Deploying NavEstate on the Contabo server

NavEstate runs on the same Windows server as Business Central 13 and SQL Server 2019
(VMI1399981, Windows Server 2022). IIS serves the React app and the ASP.NET Core 8 API from one
site; the API talks to SQL Server and to BC's SOAP port on `localhost`, so port 7047 never has to
be open to the internet.

```
Browser / phone ──HTTPS 443──> IIS ── NavEstate (ASP.NET Core 8 + React)
                                        ├── SQL Server 2019: database NavEstate (+ Hangfire jobs)
                                        └── http://localhost:7047 BC 13 SOAP: codeunit 52140613 "NavEstate"
```

## 1. One-time server setup

1. **IIS**: Server Manager → Add roles → Web Server (IIS). Keep the defaults.
2. **ASP.NET Core 8 Hosting Bundle**: install the latest 8.0 *Hosting Bundle* from
   https://dotnet.microsoft.com/download/dotnet/8.0, then run `iisreset`.
3. **Build tools** (only if you build on the server with `deploy.ps1`): .NET 8 SDK and Node.js 22 LTS.
4. **SQL Server**:
   - Install the latest SQL Server 2019 cumulative update in a maintenance window, after a full
     backup of the BC database (the server is on RTM 15.0.2000.5).
   - Set *max server memory* to about 40 GB so BC and IIS keep about 20 GB.
   - Open `deploy/sql/01_create_database.sql` in SSMS, replace the password placeholder with a long
     random password, and run it. It creates the `NavEstate` database (same collation as BC) and the
     `navestate_app` login, which has rights on NavEstate only.
5. **Folders**: create `D:\NavEstate\app` (the site) and `D:\NavEstate\files` (uploaded documents).
   Give the app pool identity `IIS AppPool\NavEstate` modify rights on `D:\NavEstate\files`.
6. **IIS site** (IIS Manager):
   - Application pool `NavEstate`: .NET CLR version *No Managed Code*, Start mode *AlwaysRunning*,
     Idle time-out *0*. (Hangfire runs the BC sync every minute, so the app must not go to sleep.)
   - Site `NavEstate`: physical path `D:\NavEstate\app`, app pool `NavEstate`, *Preload enabled* = True,
     bindings `http :80` and `https :443` with your domain name.
   - TLS: install win-acme (https://www.win-acme.com) and request a Let's Encrypt certificate for
     the domain; it renews automatically.
7. **Firewall**: allow only 80 and 443 inbound for NavEstate. Do not open 1433 or 7047.

## 2. Settings and secrets

Secrets never go in git. Set them as environment variables on the app pool
(IIS Manager → Configuration Editor → `system.applicationHost/applicationPools` →
NavEstate → `environmentVariables`), or in `D:\NavEstate\app\appsettings.Production.json`
(the deploy script leaves that file alone). Use `__` (two underscores) for nesting in variable names.

| Setting | Example | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `ConnectionStrings__NavEstate` | `Server=localhost;Database=NavEstate;User Id=navestate_app;Password=…;TrustServerCertificate=True;Encrypt=True` | |
| `Seed__AdminEmail` / `Seed__AdminPassword` | your email / a strong password | Creates the first super admin on first start; remove the password afterwards |
| `Files__Root` | `D:\NavEstate\files` | |
| `BusinessCentral__UseMock` | `true` until step 4, then `false` | |
| `BusinessCentral__SoapUrl` | `http://localhost:7047/BC130/WS/<Company>/Codeunit/NavEstate` | URL-encode spaces in the company name as `%20` |
| `BusinessCentral__Username` / `BusinessCentral__Password` | `NAVESTATE-SVC` / … | NavUserPassword user |
| `BusinessCentral__CustPostingGroup` / `BusinessCentral__VatBusPostingGroup` | `TENANTS` / `DOMESTIC` | Must exist in BC |
| `AfricasTalking__Username` / `AfricasTalking__ApiKey` / `AfricasTalking__SenderId` | | Empty key = SMS is only logged (tenant OTP will not arrive) |
| `Daraja__ConsumerKey` … | | Used from stage 4 |
| `Auth__RequireStaffTwoFactor` | `true` | Staff must set up an authenticator app before using the system |

## 3. Deploy (first time and every update)

On the server, in an elevated PowerShell:

```powershell
git clone https://github.com/noyalo15/PROPERTY.git D:\NavEstate\src   # first time
cd D:\NavEstate\src
git pull
.\deploy\deploy.ps1
```

The script builds the React app, publishes the API to `D:\NavEstate\app`, restarts the app pool and
calls `/api/health`. On start the app applies any new database migrations, creates the roles and
reference data (charge codes, payment channels, settings, templates), and the first super admin.

Then browse to `https://<your-domain>/`, sign in as the super admin, set up two-factor sign-in,
and add users under **Setup → Users & roles**. The Hangfire dashboard is at `/hangfire`
(super admins only).

To deploy automatically from GitHub on every merge, register a GitHub Actions self-hosted runner
on the server and have a workflow run `deploy\deploy.ps1`; ask and we will add it.

## 4. Connect Business Central 13

1. In the C/SIDE development environment, import `NavEstate_BC13_CAL_52140613.txt`
   (objects 52140613 to 52140616, version NAVESTATE1.1) and compile all ten objects.
2. Fill in **NavEstate Setup** (posting groups, PROPERTY and UNIT dimension codes, deposit liability
   account), **NavEstate Charge Mapping** (one line per charge code in NavEstate Setup → Charge codes),
   and **NavEstate Payment Channels** (MPESA, BANK, CHEQUE, CASH balancing accounts).
3. Web Services page: add Codeunit 52140613 with service name `NavEstate`, Published = Yes.
   Make sure SOAP services are enabled on the BC service tier.
4. Create the user `NAVESTATE-SVC` with authentication NavUserPassword and a permission set
   limited to the objects the codeunits use.
5. Check the URL in a browser on the server:
   `http://localhost:7047/BC130/WS/<Company>/Codeunit/NavEstate` should return the WSDL after a login prompt.
6. Set `BusinessCentral__UseMock=false` and the URL, user and password, then restart the app pool.
   Open **BC Sync Log** and press **Ping BC**: the top bar pill turns green "BC 13 connected".
   Every tenant already in NavEstate is in the queue; press **Process queue now** or wait a minute.

## 5. Backups

- SQL Server Agent: full backup of NavEstate nightly and a log backup every 15 minutes, copied off
  the server (Contabo Object Storage or another site), alongside the BC database backups.
- Back up `D:\NavEstate\files` on the same schedule.
