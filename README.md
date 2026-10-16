# EmployeeData API

EmployeeData is an ASP.NET Core Web API for managing employees, projects, project
membership, and tasks. It keeps the original three-project design and business
rules while using current .NET, secure authentication, migrations, tests, and CI.

## What the API does

- JWT authentication with `Employee` and `Administrator` roles.
- Administrators manage users, roles, projects, membership, and tasks.
- Employees update their own profile and work with projects they belong to.
- Employees can create tasks only inside their projects and assign them only to
  members of the same project.
- Employees can update only tasks assigned to them.
- Administrators cannot delete a project while it has unfinished tasks.
- Collection endpoints are paginated and access is filtered before data is
  returned.

## Requirements

For the simplest Windows setup, install:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server Express LocalDB, included with the SQL Server Express download
- Git

Visual Studio and SQL Server Management Studio are optional. The command-line
steps below are enough to run the application.

## Run locally

From the repository root:

1. Create your local configuration. This file is ignored by Git.

   ```powershell
   Copy-Item EmployeeData/appsettings.example.json EmployeeData/appsettings.json
   ```

2. Generate a signing key:

   ```powershell
   $keyBytes = New-Object byte[] 64
   [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($keyBytes)
   [Convert]::ToBase64String($keyBytes)
   ```

3. Open `EmployeeData/appsettings.json` and:

   - replace the value in `Jwt:SigningKeys` with the generated key;
   - set `BootstrapAdmin:Enabled` to `true`;
   - choose an initial administrator username;
   - choose a unique password between 15 and 128 characters.

   The example connection string already targets Windows LocalDB. If you use a
   different SQL Server, replace `ConnectionStrings:DefaultConnection`.

4. Restore packages and the repository-pinned Entity Framework tool:

   ```powershell
   dotnet restore EmployeeData.sln --locked-mode
   dotnet tool restore
   ```

5. Create or update the database:

   ```powershell
   dotnet ef database update --project EmployeeData --startup-project EmployeeData
   ```

6. Run the API:

   ```powershell
   dotnet run --project EmployeeData
   ```

7. Open `https://localhost:5001/swagger`. If HTTPS is not trusted locally, run
   `dotnet dev-certs https --trust` once or use `http://localhost:5000/swagger`.

Sign in with `POST /api/User/login`. In Swagger, select **Authorize** and paste
the returned token. After the first successful startup, stop the API, set
`BootstrapAdmin:Enabled` to `false`, and clear its username and password.
Enabling bootstrap again never resets an existing password.

## Configuration

`EmployeeData/appsettings.example.json` documents every application setting:

- `ConnectionStrings:DefaultConnection` - SQL Server connection string.
- `Jwt` - issuer, audience, token lifetime, active signing-key ID, and signing
  keys. Keys must be base64-encoded and at least 32 bytes.
- `BootstrapAdmin` - guarded, first-run administrator creation. Disabled by
  default outside your private local configuration.
- `LoginRateLimit` - login attempts allowed per client IP and time window.
- `Cors:AllowedOrigins` - exact browser origins allowed to call the API. An empty
  list denies cross-origin browser requests.

Environment variables can override JSON settings by replacing `:` with `__`,
for example `Jwt__SigningKeys__local=<key>`. Never commit a real connection
string, signing key, or bootstrap password.

The historical signing key once committed to this repository must be considered
compromised and must not be reused. Production credentials also need to be
rotated in their hosting systems.

## API behavior

- Authentication is required by default. Only login and `/health` are public.
- Swagger is available only in the Development environment.
- Errors use Problem Details JSON and do not expose server stack traces.
- List endpoints accept `pageNumber` and `pageSize`. Defaults are page 1 and 50
  records; the maximum page size is 100.
- Pagination metadata is returned in `X-Page-Number`, `X-Page-Size`, and
  `X-Total-Count` response headers.
- `/health` reports application and database readiness without returning
  connection details.
- Usernames, project names, and task titles are unique under the database's
  normal case-insensitive SQL Server collation.

## Architecture

The solution is a pragmatic layered monolith:

```text
EmployeeData (API)  --->  EmployeeData.Services  --->  EmployeeData.Models
       |___________________________________________________________^

EmployeeData.IntegrationTests  API, security, and business-rule tests
```

Controllers own HTTP concerns, services own business/data behavior, and explicit
mapping keeps persistence entities out of public response contracts. This shape
fits the application's size; extra repository, CQRS, or microservice layers would
add ceremony without improving its current responsibilities.

## Verify changes

Run the same important checks used by CI:

```powershell
dotnet restore EmployeeData.sln --locked-mode
dotnet tool restore
dotnet format EmployeeData.sln --no-restore --verify-no-changes
dotnet build EmployeeData.sln --configuration Release --no-restore
dotnet test EmployeeData.sln --configuration Release --no-build --no-restore
dotnet ef migrations has-pending-model-changes `
  --project EmployeeData --startup-project EmployeeData `
  --configuration Release --no-build
dotnet package list --project EmployeeData.sln `
  --vulnerable --include-transitive --no-restore
```

The integration tests use an isolated SQLite database, so SQL Server is not
required to run the test suite.

## Existing databases

The API deliberately does not apply migrations during startup. It fails fast if
the configured schema is behind, keeping database changes explicit.

Fresh databases only need the `dotnet ef database update` command above. If a
database came from the old `EnsureCreated` version or still contains legacy
plaintext password data, follow the backed-up, one-way procedure in
[Legacy database upgrade](docs/legacy-database-upgrade.md).

## Continuous integration

GitHub Actions verifies locked dependency restore, formatting, Release build,
all tests, migration-model consistency, and direct/transitive package advisories
on every push and pull request.

## License

See [LICENSE](LICENSE).
