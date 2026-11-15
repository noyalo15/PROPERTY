/*
  NavEstate database and login on the Contabo SQL Server 2019 instance (the same instance as BC 13).
  Run once in SSMS as an administrator. Replace the password placeholder first.

  The navestate_app login only gets rights on the NavEstate database, never on the BC database.
  It is db_owner of NavEstate because the app applies its own EF Core migrations on start
  (tables, row-level security policy, Hangfire schema).
*/
USE master;
GO

IF DB_ID(N'NavEstate') IS NULL
    CREATE DATABASE NavEstate COLLATE SQL_Latin1_General_CP1_CI_AS;
GO

ALTER DATABASE NavEstate SET RECOVERY FULL;            -- log backups every 15 minutes (see DEPLOY.md)
ALTER DATABASE NavEstate SET READ_COMMITTED_SNAPSHOT ON; -- readers do not block the BC sync worker
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'navestate_app')
    CREATE LOGIN navestate_app WITH PASSWORD = N'__CHANGE_ME_LONG_RANDOM__', CHECK_POLICY = ON, DEFAULT_DATABASE = NavEstate;
GO

USE NavEstate;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'navestate_app')
    CREATE USER navestate_app FOR LOGIN navestate_app;
ALTER ROLE db_owner ADD MEMBER navestate_app;
GO
