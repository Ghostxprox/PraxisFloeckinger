# Praxis Flöckinger — Plattform

Integrierte Praxis- und Therapieverwaltungs-Plattform.
Stack: ASP.NET Core 9 · PostgreSQL 16 · EF Core 9 · Multi-Tenant (Database-per-Tenant)

---

## Local Setup

### Voraussetzungen

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9) (wird via `global.json` erzwungen)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (für lokale Postgres)
- `dotnet tool restore` — installiert gepinnte Tools (u.a. `dotnet-ef 9.0.x`)

### Einrichtung

```bash
# 1. Repo klonen und lokale Tools installieren
git clone https://github.com/Ghostxprox/PraxisFloeckinger.git
cd PraxisFloeckinger
dotnet tool restore

# 2. Umgebungsvariablen setzen
cp .env.example .env
# .env öffnen und POSTGRES_PASSWORD setzen
# Das Passwort muss mit src/PraxisFloeckinger.Api/appsettings.Development.json übereinstimmen

# 3. Postgres starten
docker compose up -d postgres

# 4. Solution bauen
dotnet build

# 5. Datenbank-Migrations ausführen (Master-DB)
dotnet tool run dotnet-ef database update \
  --project src/PraxisFloeckinger.Infrastructure \
  --startup-project src/PraxisFloeckinger.Api \
  --context MasterDbContext

# 6. Tests ausführen
dotnet test
```

### Nützliche Befehle

```bash
# Postgres-Logs
docker compose logs -f postgres

# Neue Migration anlegen
dotnet tool run dotnet-ef migrations add <MigrationName> \
  --project src/PraxisFloeckinger.Infrastructure \
  --startup-project src/PraxisFloeckinger.Api \
  --context MasterDbContext \
  --output-dir Persistence/Master/Migrations

# Postgres stoppen (Daten bleiben im Volume)
docker compose stop postgres

# Postgres inkl. Volume zurücksetzen
docker compose down -v
```

### Sicherheitshinweise

- `.env` enthält lokale Dev-Credentials und wird **nie** committet
- `appsettings.Development.json` enthält ein hartcodiertes Dev-Passwort (nur für lokale DB, kein Prod-Bezug)
- Produktions-Credentials kommen ausschließlich aus sicheren Umgebungsvariablen / Secret-Management

---

*Vollständige Architektur und Konventionen: siehe `docs/Leitfaden_Praxis_Floeckinger.md`*
