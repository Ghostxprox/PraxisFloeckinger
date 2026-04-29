# Praxis Flöckinger — Projekt-Kontext für Claude Code

Bevor du irgendetwas tust, lies `docs/Leitfaden_Praxis_Floeckinger.md`
vollständig durch — dort steht die komplette Architektur, der Tech-Stack
und alle Konventionen für dieses Projekt.

## Quick Facts
- ASP.NET Core 9 (C#), Razor Pages/MVC
- PostgreSQL 16, EF Core 9
- Multi-Tenancy: Database-per-Tenant (siehe Leitfaden §4)
- Hosting: Hetzner Cloud AT
- DSGVO-kritisch: Therapiedaten, §15 PthG (10 Jahre Aufbewahrung)
- Soft Deletes mit Global Query Filter, außer Audit-Log und Honorarnoten

## Aktueller Projekt-Stand

- Schritt 1 (Solution-Skeleton): 7 Projekte, Directory.Build.props,
  .editorconfig, ADR-Template, ADR-0001, GitHub-Repo — abgeschlossen.
- Schritt 2 (Core-Domäne): EntityBase, SoftDeletableEntityBase, alle
  Enums, ITenantContext, TenantInfo — abgeschlossen.
- Schritt 3 (Infrastructure / Master-DB): MasterDbContext, Entities,
  erste Migration, Testcontainers-Integration-Tests — abgeschlossen.
- Schritt 4 (Tenant-Resolver-Middleware): TenantResolverMiddleware, TenantResolver
  mit 60s-Cache, RequireTenantFilter, Dev-Header-Override — abgeschlossen, 33 Tests grün.
- Schritt 5 (TenantDbContext + Provisioning): TenantDbContext, Entities, Migration, Seeder — abgeschlossen, 44 Tests grün.
- Schritt 6a (Auth-Foundation: Argon2id + JWT RS256 + Login/Refresh/Logout) — abgeschlossen, 75 Tests grün.
- Aktueller Stand: Schritt 6b (TOTP-2FA: Field-Encryption, zweistufiger Login, MFA-Session-Token, Recovery-Codes) läuft.
- Eine alte ASP.NET MVC-App liegt unter ../Old/ als Referenz (Marketing-Seiten,
  Karriere-Timeline, Coaching mit PriceConfig, Razor-Layout, CSS). Wird erst
  nach der Multi-Tenant-Foundation in PraxisFloeckinger.Web überführt.
- Geplante finale Solution-Struktur (siehe Leitfaden §5 / Roadmap):
    - PraxisFloeckinger.Api          (Web API, Hetzner)
    - PraxisFloeckinger.Web          (Public Site + Patientenportal)
    - PraxisFloeckinger.Praxis       (lokale Praxis-Software, Mac mini)
    - PraxisFloeckinger.Core         (Domain Models, Interfaces)
    - PraxisFloeckinger.Infrastructure (EF Core, Postgres, Repos)
    - PraxisFloeckinger.Shared       (DTOs, Konstanten)
    - PraxisFloeckinger.Tests

## Coding-Konventionen (siehe Leitfaden §10)
- Nullable reference types ON
- TreatWarningsAsErrors = true
- Conventional Commits (feat/fix/refactor/docs/test/chore)
- Migrations: jede Schema-Änderung als EF Core Migration
- Niemals PII in Logs
- Niemals Patientendaten ohne Audit-Log-Eintrag ändern

## Workflow
- Feature-Branches, PRs auch im Solo-Betrieb
- Vor jedem größeren Commit: `dotnet test`
- ADRs in /docs/adr/ für wichtige Architektur-Entscheidungen

## Projekt-Referenz-Regeln

Erlaubte Projekt-Abhängigkeiten (→ = "darf referenzieren"):

```
Core        →  (nichts — reine Domain-Schicht)
Shared      →  (nichts — nur DTOs und Konstanten)
Infrastructure → Core, Shared
Api         → Infrastructure, Shared
Web         → Shared          (holt Daten per HTTP von Api, kein direkter DB-Zugriff)
Praxis      → Core, Shared    ← STRIKTE REGEL: niemals Api oder Infrastructure!
Tests       → Core, Infrastructure, Shared, Api (nur für WebApplicationFactory-Tests)
```

**Warum Praxis nur Core + Shared:** Die lokale Praxis-Software auf dem Mac mini
darf keinerlei WAN-DB-Logik (EF Core Migrations, Connection-Strings zur Hetzner-DB)
enthalten. Sie kommuniziert ausschließlich per mTLS-gesichertem HTTP mit der Api.

**TODO (später):** Architektur-Test mit NetArchTest ergänzen, der diese Regeln
automatisch verifiziert — damit kein versehentliches `ProjectReference` diese
Grenze überschreitet.

## Was du nicht tun sollst
- Keine US-gehosteten Dependencies (DSGVO)
- Keine Telemetrie-Calls einbauen
- Niemals Diagnose-/Verlaufsdaten über die Web-API senden
  (die liegen ausschließlich auf dem lokalen Praxis-Server)
- Nicht ohne Rückfrage bestehenden Code wegwerfen — die Marketing-Seiten
  (Karriere, Coaching) sind wertvoll und sollen in PraxisFloeckinger.Web
  übernommen werden
- Kein "creative scope expansion" — wenn unklar, fragen