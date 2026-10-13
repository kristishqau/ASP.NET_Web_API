## REST API

## Project Description:

**This is a Rest API for the administration of employee records consisting of:**
- Simple Login (JWT authentication) (no registration page, administrator will create new users)
- Two User Roles: Employee, Administrator
- User Profile
- Projects and Tasks
![API1](https://github.com/user-attachments/assets/0be594ac-e450-4bf2-8707-55328cffaf9d)
![API2](https://github.com/user-attachments/assets/be449770-0b03-4715-bcd0-dd11432d427f)
![API3](https://github.com/user-attachments/assets/aec8b1ae-2a3f-460a-b2df-8719775c0240)
- Employee can update his profile data and profile picture, create tasks that belong to the projects he is part of (also can assign these tasks to other employees that are part of the project), mark his tasks as completed etc.
Employee can view all tasks that are related to the projects he is part of (in read-only mode). Employee can not modify tasks which are not assigned to him.
![API5](https://github.com/user-attachments/assets/450a991f-3d26-4001-9426-512da5e6912b)
- Administrator can create/update/remove users, projects and tasks. Administrator can add employees to projects (also remove employees from them), create new tasks and assign them to other employees, mark tasks as completed or remove them. Administrator cannot remove projects that have even open tasks.
![API5](https://github.com/user-attachments/assets/77601c5b-b340-42c7-980c-f2ba9195552e)

## Technologies used:
- .NET SDK 10.0 LTS
- Entity Framework
- Microsoft SQL Server 2019
- Microsoft SQL Server Management Studio 19 (Database Management)
- Microsoft Visual Studio 2022

Models of the project are separated into a diferent project of type Class Library and then is added as reference to the WEB API solution. Model’s properties are enriched with Annotations for validation and documentation purposes.

Web Api project structure consist of Controllers, Services and Models with their repective DTOs. Each of the Service classes have their own Interface which is used with Dependency Injection in the Controller classes.

## JWT configuration

The API does not keep signing keys in source control. Start from
`EmployeeData/appsettings.example.json` for the non-secret configuration shape and
supply the real connection string and JWT keys through user secrets, environment
variables, or the deployment platform's secret store.

Required environment variables use this shape:

```text
ConnectionStrings__DefaultConnection=<secret connection string>
Jwt__Issuer=EmployeeData.Api
Jwt__Audience=EmployeeData.Client
Jwt__AccessTokenMinutes=60
Jwt__ActiveKeyId=2026_10
Jwt__SigningKeys__2026_10=<base64-encoded random key>
```

Generate a 32-byte development signing key in PowerShell without committing it:

```powershell
[Convert]::ToBase64String(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

The historical repository key must be treated as compromised and must never be
reused. Deploying this version intentionally invalidates every previously issued
token, so users must sign in again.

For routine rotation, add the new and previous keys to `Jwt__SigningKeys`, change
`Jwt__ActiveKeyId` to the new key, restart the API, wait one maximum token lifetime
plus clock skew, and then remove the previous key.

## Database lifecycle

The API validates the migration history at startup but never creates or migrates
the production schema itself. This keeps schema changes explicit, reviewable, and
separate from application startup. The process stops with the pending migration
names if the configured database is not current.

Restore the repository-pinned Entity Framework tool before managing the schema:

```powershell
dotnet tool restore
```

For a new, empty database—or one already managed by EF migrations—apply the
checked-in migrations with:

```powershell
dotnet ef database update `
  --project EmployeeData `
  --startup-project EmployeeData
```

Earlier versions of this application used `EnsureCreated`. Such databases have
the application tables but usually lack `__EFMigrationsHistory`; applying the
initial migration directly would then try to recreate those tables. Back up the
database and run [scripts/adopt-legacy-database.sql](scripts/adopt-legacy-database.sql)
against that database first. The script is transactional and will record the
historical baseline only after validating every expected application column,
primary key, foreign key, and index. It refuses empty or structurally
incompatible schemas without changing them; compatible extra indexes are
preserved.

With Windows authentication and `sqlcmd`, the adoption command has this shape:

```powershell
sqlcmd -S <server> -d <database> -E `
  -i scripts/adopt-legacy-database.sql -b
```

After adoption, apply pending migrations with the same `dotnet ef database
update` command. Re-running the adoption script is safe: if its baseline entry
already exists, it exits without making changes.

## Initial administrator bootstrap

The API no longer creates a fixed administrator or stores a demonstration
password in production code. Administrator creation is disabled by default and
is allowed only as an explicit first-run operation against an empty database.

Supply the bootstrap credential through the deployment secret store, after the
database migrations have been applied:

```text
BootstrapAdmin__Enabled=true
BootstrapAdmin__RotateLegacySeed=false
BootstrapAdmin__UserName=<initial administrator user name>
BootstrapAdmin__Password=<unique password of 15-128 characters>
BootstrapAdmin__ProfilePictureUrl=<optional URL>
```

Start the API once, confirm the administrator can sign in, then set
`BootstrapAdmin__Enabled=false` and remove the bootstrap password from the
runtime environment. Re-running an enabled bootstrap never resets a password.
If the database is already populated and does not contain the configured
administrator, startup fails instead of silently granting privileges.

An existing database created by an older version may still contain the
historical demonstration administrator and its public credential. This version
detects that exact credential and refuses to start until it is rotated. Perform
the one-time rotation with `BootstrapAdmin__Enabled=false`,
`BootstrapAdmin__RotateLegacySeed=true`, `BootstrapAdmin__UserName=admin`, and a
new unique `BootstrapAdmin__Password`. After a successful start and login, set
`BootstrapAdmin__RotateLegacySeed=false` and remove the password from the
environment. The rotation is idempotent for the same replacement password and
never resets a different credential.

## Password migration behavior

New credentials are stored with ASP.NET Core's password hasher. During the
transition from the historical schema, a user with no `PasswordHash` can sign in
with the existing password once; a successful login writes a hash and clears the
legacy plaintext value before issuing a token. A non-null hash is authoritative,
so an empty, malformed, or non-matching hash never falls back to plaintext.

This is an additive database migration, but the data upgrade is intentionally
one-way: an older application binary cannot authenticate an account after its
plaintext value has been cleared. Stop all older instances and deploy this
version as a coordinated maintenance release. Roll forward or restore a
pre-migration backup on failure; the down migration refuses to discard stored
hashes. Stop every application instance before applying or rolling back this
migration.
