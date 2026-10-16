# Legacy database upgrade

Use this procedure only for a database created by an older EmployeeData version.
Back up the database and stop every old application instance first.

## Adopt an `EnsureCreated` database

Older releases created tables without Entity Framework migration history. Run
the adoption script before applying migrations so EF does not try to recreate
those tables.

With Windows authentication and `sqlcmd`:

```powershell
sqlcmd -S <server> -d <database> -E `
  -i scripts/adopt-legacy-database.sql -b
```

The transactional script records the historical baseline only after validating
the expected columns, primary keys, foreign keys, and indexes. It refuses empty
or incompatible schemas without changing them. It is safe to rerun.

## Convert legacy passwords

First migrate only to the final schema that still contains the plaintext column:

```powershell
dotnet ef database update 20261014191020_EnforceBoundedDomainValues `
  --project EmployeeData --startup-project EmployeeData
```

Validate the data without writing, then apply the conversion:

```powershell
dotnet run --project EmployeeData --configuration Release -- `
  --legacy-password-migration=dry-run
dotnet run --project EmployeeData --configuration Release -- `
  --legacy-password-migration=apply
```

The apply command hashes usable legacy passwords, preserves valid existing
hashes, clears plaintext, and is safe to repeat.

If the historical public `admin` credential exists, temporarily configure:

```text
BootstrapAdmin__RotateLegacySeed=true
BootstrapAdmin__UserName=admin
BootstrapAdmin__Password=<new unique password of 15-128 characters>
```

Use those settings for both maintenance commands. Then disable
`RotateLegacySeed` and remove the password before normal startup.

Finish by applying the remaining migrations:

```powershell
dotnet ef database update --project EmployeeData --startup-project EmployeeData
```

The final migration refuses to remove plaintext storage until every user has a
nonblank password hash and every plaintext value is cleared. This conversion is
one-way: if deployment fails, roll forward or restore the backup.
