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

- Workspace ist leer (frische .NET 9 Solution wird gerade aufgebaut).
- Eine alte ASP.NET MVC-App existiert im Verzeichnis `../Old/` außerhalb
  dieses Workspaces. Sie enthält bereits:
    - Marketing-Seiten (Karriere-Timeline, Coaching mit PriceConfig)
    - Razor-Layout und CSS
  Diese Inhalte sollen später in das neue `PraxisFloeckinger.Web`-Projekt
  übernommen werden, aber nicht jetzt — erst wenn die Solution-Struktur
  und Multi-Tenant-Foundation steht.
- Geplante finale Solution-Struktur (siehe Leitfaden §5 / Roadmap):
    - PraxisFloeckinger.Api          (Web API, Hetzner)
    - PraxisFloeckinger.Web          (Public Site + Patientenportal)
    - PraxisFloeckinger.Praxis       (lokale Praxis-Software, Mac mini)
    - PraxisFloeckinger.Core         (Domain Models, Interfaces)
    - PraxisFloeckinger.Infrastructure (EF Core, Postgres, Repos)
    - PraxisFloeckinger.Shared       (DTOs, Konstanten)
    - PraxisFloeckinger.Testsdotnet --version

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
Tests       → Core, Infrastructure, Shared
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