# Projektleitfaden — Praxis-Plattform Flöckinger

> **Zweck dieses Dokuments:** Vollständiger Projekt-Kontext für die Entwicklung der Praxissoftware. In jeden neuen Chat als Kontext mitschicken, damit Architektur, Stack und Entscheidungen konsistent bleiben. Letzte Aktualisierung: April 2026.

---

## 1. Projekt-Identität

Eine integrierte Praxis- und Therapieverwaltungs-Plattform für die psychotherapeutische Privatpraxis Flöckinger (Innsbruck/AT, Schwerpunkt Verhaltenstherapie). Architektur und Codebase sind von Anfang an so gebaut, dass die Software später als SaaS oder On-Premise-Lizenz an andere Therapeuten und Therapieinstitute (Zielgröße 5–20 Therapeuten pro Tenant) verkauft werden kann.

**Therapieangebot der Erstpraxis (für Datenmodell relevant):**
- Einzeltherapie (Verhaltenstherapie)
- Paartherapie
- Gruppentherapie (geschlossene Gruppen, 4–9 Teilnehmer)
- Erstgespräche
- High Performance & Struktur Coaching
- Achtsamkeitstraining
- Online-Stunden
- Sportbegleitete Stunden (Laufen, Calisthenics)
- Therapie mit Therapiehund
- Therapie mit Pferd (zweite Location)
- Mittagessen-Therapie

**Drei Komponenten der Plattform:**
1. **Public Website + Patientenportal** (Buchung, Fragebögen, Honorarnoten-Download)
2. **Zentrale REST-API** (ASP.NET Core, gehostet bei Hetzner AT)
3. **Interne Praxis-Software** (ASP.NET Core Razor App auf lokalem Mac mini in der Praxis, holt Termine/Stammdaten read-only von API, sensible Doku/Diagnosen verlassen die Praxis nie)

---

## 2. Tech-Stack

| Schicht | Technologie | Begründung |
|---|---|---|
| Backend | ASP.NET Core 9 (C#) | Stack des Entwicklers, mature für Healthcare |
| Frontend | Razor Pages / MVC, HTML, CSS, JS | konsistent mit Backend |
| Datenbank | PostgreSQL 16 | Row-Level Security, pgcrypto, JSONB, EU-Healthcare-Standard |
| ORM | EF Core 9 | Global Query Filter für Soft Deletes |
| Auth Web (Patient/Mitarbeiter) | ASP.NET Core Identity + TOTP-2FA | 2FA Pflicht für Mitarbeiter-Rollen |
| Auth Praxis-Server ↔ API | mTLS Client-Zertifikat + IP-Allowlist | Praxis-Server kann ausschließlich aus Praxis-LAN auf API zugreifen |
| Auth API-extern (später, SaaS) | OAuth2/OIDC-kompatibel | für Konzern-SSO |
| Mail | Eigener SMTP auf Hetzner | Honorarnoten, Termine, Erinnerungen, Fragebogen-Einladungen |
| SMS | Messente (EU) | Termin-Erinnerung 24h vorher |
| Video-Therapie | Self-hosted Jitsi auf Hetzner (v1: Link, v2: Embed) | DSGVO |
| PDF-Generierung (Honorarnoten) | QuestPDF (.NET-nativ, kostenlos für non-commercial; bezahlte Lizenz beim Verkauf) | sauberer C#-Code |
| Hosting Web/API | Hetzner Cloud AT (CX22 Start, später CCX) | EU-Datenresidenz |
| Container | Docker Compose | Reverse Proxy: Caddy (auto-HTTPS) |
| Lokaler Praxis-Server | Mac mini, FileVault on, ASP.NET Core als launchd-Service | Doku, Diagnosen, sensible Akten |
| Backup intern | restic, clientseitig verschlüsselt → Hetzner Storage Box (3-2-1-Regel) | DSGVO + 10-Jahres-Aufbewahrung §15 PthG |
| IDE | VS Code + C# Dev Kit (gratis); optional Rider (Lizenz beim Verkauf nötig) | flexibel |
| Versionierung | Git, Conventional Commits, GitHub | + GitHub Actions für CI |
| Migrations | EF Core Migrations | jede Migration reviewt |
| Tests | xUnit + FluentAssertions + Testcontainers (echte Postgres) | |
| Monitoring | Uptime Kuma (self-hosted) | EU |
| Logging | Seq (self-hosted) | EU, kein PII in Logs |

---

## 3. Architektur (Überblick)

```
                    ┌────────────────────────────────────────────┐
                    │  PATIENTEN / WEBBESUCHER (Internet)        │
                    └────────────────────────────────────────────┘
                                     │ HTTPS (TLS 1.3)
                    ┌────────────────▼────────────────────────────┐
                    │  www.praxis-floeckinger.at  (Marketing)     │
                    │  app.praxis-floeckinger.at  (Buchung,       │
                    │     Fragebögen, Honorarnoten als PDF)       │
                    │  ASP.NET Core Razor Pages, Hetzner AT       │
                    └────────────────┬────────────────────────────┘
                                     │ JSON / REST
                    ┌────────────────▼────────────────────────────┐
                    │  api.praxis-floeckinger.at                  │
                    │  ASP.NET Core 9, JWT-Auth                   │
                    │  PostgreSQL 16:                             │
                    │    - tenants_master                         │
                    │    - tenant_<n> (eine DB pro Praxis/Kunde)  │
                    │  Termine, Patient-Stammdaten, Honorarnoten, │
                    │  Fragebögen, Audit-Log                      │
                    └────────────────▲────────────────────────────┘
                                     │ mTLS + IP-Allowlist (read-only)
                    ┌────────────────┴────────────────────────────┐
                    │  PRAXIS-LAN — Mac mini "praxis.local"       │
                    │  ASP.NET Core Razor App                     │
                    │  PostgreSQL 16 lokal (verschlüsselt)        │
                    │  Doku, Diagnosen (ICD-10/11 + Freitext),    │
                    │  Verlauf, Therapieziele                     │
                    │  reMarkable PDF-Sync via Bluetooth (manuell)│
                    │  Backup → Hetzner Storage Box (restic, e2e) │
                    │  Zugriff nur durch Therapeut (Mac, 2FA)     │
                    └─────────────────────────────────────────────┘
```

**Kritische Regel:** Diagnose-/Verlaufsdaten verlassen das Praxis-LAN niemals. Was über die API geht, sind nur: Stammdaten, Termine, Honorarnoten, Fragebogen-Antworten, Einwilligungen.

---

## 4. Multi-Tenancy — Database-per-Tenant

**Entscheidung:** Database-per-Tenant ab Tag 1. Auch wenn anfangs nur 1 Tenant (eigene Praxis) existiert.

**Implementation:**
- Master-DB `tenants_master`: Tabelle `Tenants` (Id, Subdomain, CustomDomain, ConnectionStringRef, LicenseStatus, CreatedAt, ...)
- Pro Tenant eine eigene PostgreSQL-Datenbank `tenant_<guid>`
- ASP.NET Core Middleware `TenantResolverMiddleware`:
  1. Liest Subdomain aus `HttpContext.Request.Host` (z.B. `flöckinger.app.com` oder via CNAME `app.praxis-fischer.at`)
  2. Schlägt in Master-DB nach
  3. Setzt `ITenantContext` für den Request
- DbContext-Factory liefert pro Request den passenden ConnectionString
- Vorteile: maximale Datenisolation, einfache Backups pro Kunde, einfaches "Kunde kündigt → DB exportieren + löschen", einfaches On-Prem (Konzern bekommt nur seine eine DB)

**White-Label:** CustomDomain pro Tenant (eigene Domain via CNAME → `app.{subdomain}.praxis-floeckinger.at`). Logo, Farben, Texte konfigurierbar pro Tenant.

---

## 5. Datenmodell-Kern (Auszug)

**Master-DB (`tenants_master`):**
- `Tenants` (Id, Subdomain, CustomDomain, DbConnectionRef, LicenseTier, IsActive, CreatedAt)
- `SystemAdmins` (du als Plattform-Betreiber)
- `LicenseEvents` (Audit für Vertragsverläufe)

**Pro-Tenant-DB (`tenant_<guid>`):**

Stammdaten:
- `Users` (Id, Email, PasswordHash, Role, FirstName, LastName, Phone, CreatedAt, IsDeleted, DeletedAt, DeletedBy)
- `PatientProfiles` (UserId, BirthDate, Address, EmergencyContact, InsuranceInfo, Notes-public)
- `TherapistProfiles` (UserId, Specializations[], Bio, IsSupervisor)

Therapie-Konfiguration:
- `TherapyTypes` (Einzel, Doppel, Paar, Gruppe, Coaching, Achtsamkeit, Erstgespräch, Online; Dauer, Preis, IstSportbegleitet, etc.)
- `TherapyModes` (Tag-System: `anzug`, `business-casual`, `casual`, `mit-hund`, `mit-pferd`, `online`, `mittagessen`, `sportbegleitet`)
- `Locations` (Praxis Innsbruck, Pferdestall xy)
- `WeeklyScheduleTemplate` (TherapistId, Wochentag, Startzeit, Endzeit, ModeIds[], LocationId, Pufferzeit, MaxConsecutiveAnimalSessions)
- `Slots` (Id, Date, StartTime, Duration, TherapistId, TherapyTypeId, ModeIds[], LocationId, Status: Free/Booked/Blocked, MaxParticipants)
  - Auto-generiert 4 Monate im Voraus per Background-Job

Buchungen:
- `Appointments` (Id, SlotId, PatientId, TherapistId, Status, BookedAt, CancelledAt, CancellationReason, HonorarnoteId, IsDeleted, ...)
- `Groups` (Id, Name, TherapistId, RecurrenceRule (z.B. "jeden Do 10:00–11:00"), MaxMembers)
- `GroupMemberships` (GroupId, PatientId, JoinedAt, LeftAt)
- `GroupSessions` (GroupId, Date, ConductedBy)
- `EmergencyRequests` (Id, PatientId, RequestedDateTime, RequestedMode, Note, Status: Pending/Approved/Rejected/CounterProposed, TherapistResponse)

Fragebögen & Einwilligungen:
- `QuestionnaireTemplates` (Type: Kurz/Lang, JsonSchema, Version)
- `QuestionnaireResponses` (PatientId, TemplateId, Answers as JSONB, SubmittedAt, encrypted)
- `Consents` (PatientId, Type: Datenschutz/Schweigepflicht/AGB, Version, AcceptedAt, IpAddress)

Buchhaltung:
- `Honorarnotes` (Id, RunningNumber-fortlaufend-pro-Jahr, Date, PatientId, AppointmentId, Items, Subtotal, Total, Status, PdfBlobId, IsCancellation, RefersToHonorarnoteId)
  - **NIE löschbar** (RKSV/§132 BAO)

Compliance:
- `AuditLogs` (Id, UserId, Action, ResourceType, ResourceId, OldValueJson, NewValueJson, Timestamp, IpAddress, UserAgent)
  - **NIE löschbar**, append-only, optional Hash-Chain

**Lokale Praxis-DB (Mac mini, getrennt von Tenant-DB):**
- `PatientFiles` (PatientId-aus-Tenant-DB, OpenedAt, ICD10Codes[], ICD11Codes[], FreeText, encrypted)
- `SessionEntries` (AppointmentId, Date, Verlaufsnotiz encrypted, Therapieziele, Hausaufgaben, Stimmungsskala 0-10)
- `RemarkablePdfs` (PatientId, FileBlob, UploadedAt, Tags)

**Soft Deletes:**
- Tabellen mit `IsDeleted` + `DeletedAt` + `DeletedBy`
- EF Core Global Query Filter: `modelBuilder.Entity<T>().HasQueryFilter(e => !e.IsDeleted);`
- Audit-Log und Honorarnoten **niemals** soft-deletable
- Recht auf Löschung (Art. 17 DSGVO) ist durch §15 PthG (10 Jahre Aufbewahrung) für Therapie-Doku eingeschränkt → "Anonymisieren" oder "Locken" statt Löschen

---

## 6. Rollen-Matrix

| Resource | Patient | Sekretärin | Therapeut (eigene Patienten) | Therapeut (Supervisor) | PraxisAdmin | SystemAdmin |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| Eigene Termine | RW | — | — | — | — | — |
| Alle Termine (Praxis) | — | RW | RW (eigene) | R (zugeordnete) | RW | — |
| Patient-Stammdaten | R (eigene) | RW | RW (eigene) | R (zugeordnete) | RW | — |
| Diagnose / Verlauf (intern) | — | — | RW (eigene) | R (zugeordnete) | — | — |
| Honorarnoten | R (eigene) | RW | R (eigene) | — | RW | — |
| Slot-Templates | — | — | RW (eigene) | — | RW | — |
| Therapieformen / Preise | — | — | — | — | RW | — |
| Tenants verwalten | — | — | — | — | — | RW |
| Audit-Log | — | — | R (eigene Aktionen) | R (Supervisee) | R | R |
| Sonderwunsch-Anfragen (intern "Emergency") | RW (eigene) | R | RW (eigene) | — | R | — |
| Fragebogen-Antworten | — | — | R (eigene Patienten) | R (zugeordnete) | — | — |

**Kernprinzipien:**
- **Sekretärin** sieht NIE Diagnosen oder Verlaufsdaten. Hat keinen Zugriff auf interne Praxis-Software.
- **Therapeut (eigen)** sieht standardmäßig nur seine eigenen Patienten. Kann von einem Supervisor "dazugeschaltet" werden.
- **Patient** kann eigene Akte/Diagnose NICHT online einsehen — Auskunft auf Nachfrage in der Praxis (Druck der Doku).
- **PraxisAdmin** ≠ **SystemAdmin**. PraxisAdmin verwaltet eine Praxis (Tenant). SystemAdmin verwaltet die ganze Plattform und sieht keine Patientendaten.

---

## 7. DSGVO & Compliance

**Rechtsgrundlagen:**
- DSGVO Art. 6 (1) b (Vertragserfüllung) + Art. 9 (2) h (Gesundheitsversorgung) + Art. 9 (3) (Berufsgeheimnis)
- Psychotherapiegesetz Österreich §15 (10 Jahre Aufbewahrung der Doku)
- Bundesabgabenordnung §132 (Belegerteilungs- und Aufbewahrungspflicht)

**Pflicht-Maßnahmen:**
- Hosting EU (Hetzner AT) — keine US-Cloud
- Auftragsverarbeitungsvertrag (AVV) mit Hetzner und allen Sub-Dienstleistern
- AVV-Vorlage für eigene SaaS-Kunden (wenn Verkauf startet)
- Datenschutzfolgenabschätzung (DSFA) — bei Therapiedaten Pflicht; Vorlage für Kunden bereitstellen
- Verzeichnis von Verarbeitungstätigkeiten (Art. 30 DSGVO)
- Datenschutzbeauftragter (DSB): bei Einzelpraxis je nach Mitarbeiterzahl nicht zwingend, aber bei SaaS-Verkauf bald nötig — **offen, später klären**
- Einwilligungen versioniert (welche Version? wann angenommen? IP?)
- Recht auf Auskunft (Art. 15) — Export-Funktion (JSON + PDF)
- Recht auf Löschung (Art. 17) — eingeschränkt durch §15 PthG, ansonsten Soft-Delete + 10 Jahre dann Hard-Delete
- Recht auf Datenübertragbarkeit (Art. 20) — JSON-Export
- Audit-Log lückenlos, append-only, manipulationssicher (optional Hash-Chain)
- Keine PII in Application-Logs (Pseudonymisierung)
- Cookie-Banner (Consent Mode v2-konform), Datenschutzerklärung, Impressum
- Schweigepflichtsentbindung als digitale Einwilligung mit Versionierung
- Hinweis auf Krankenkassen-Rückerstattung (Wahltherapeut-Tarife) auf Honorarnote

---

## 8. Sicherheits-Baseline

**Transport & Auth:**
- TLS 1.3 only, HSTS preload, Modern Cipher Suites
- Argon2id für Passwort-Hashing (id, m=64MB, t=3, p=4)
- 2FA Pflicht: TOTP für alle Mitarbeiter-Rollen, optional WebAuthn als Upgrade
- Session-Timeouts:
  - Interne Software (Therapeut): 15min Inaktivität
  - Web-Backend (Sek/Admin): 60min
  - Patient-Portal: 24h
- Rate Limiting: Login 5/min/IP, API 100/min/Tenant
- CSRF-Token in allen Formularen, Anti-XSS, strikte Content-Security-Policy
- mTLS für Praxis-Server ↔ API + statische IP-Allowlist auf Hetzner-Firewall

**Daten:**
- Field-Level Encryption für Diagnosen, Verlaufsnotizen, sensible Felder (pgcrypto bzw. lokaler Key auf Mac mini)
- Verschlüsselung at rest auf Disk-Ebene (FileVault auf Mac mini, LUKS/disk-encryption auf Hetzner)
- Backups verschlüsselt off-site (restic / borg), Schlüssel niemals auf Backup-Server
- Schlüsselrotation jährlich

**Operations:**
- Dependency-Scanning (Dependabot, Renovate)
- Pen-Test vor jedem major release (mind. 1×/Jahr)
- Incident-Response-Plan dokumentiert
- Disaster-Recovery getestet (alle 6 Monate Restore-Test)
- Sentry self-hosted (EU) für Error-Tracking, ohne PII

---

## 9. API-Konventionen

- REST + JSON
- OpenAPI/Swagger automatisch generiert
- URL-Versionierung: `/api/v1/...`
- Auth: JWT (Web), mTLS (Praxis-Client), später OAuth2/OIDC
- Naming: kebab-case in URLs (`/api/v1/honorarnotes`), camelCase in JSON, PascalCase in C#
- Errors: RFC 7807 Problem Details
- Pagination: cursor-based für lange Listen
- Idempotenz-Keys für POST (Buchungen!)
- Tenant-Resolution per Subdomain (transparent für API-User)
- Standard-Headers: `X-Request-Id` (für Audit-Log-Korrelation)

**Wichtige Endpoints (Auszug):**
- `POST /api/v1/auth/login`
- `GET /api/v1/slots?from=...&to=...&modes=anzug,!hund` (Filter inkl./exkl.)
- `POST /api/v1/appointments` (mit Idempotency-Key)
- `DELETE /api/v1/appointments/{id}` (Stornierung — 24h-Regel serverseitig erzwingen)
- `POST /api/v1/emergency-requests`
- `GET /api/v1/honorarnotes/{id}/pdf`
- `GET /api/v1/sync/patients` (für Praxis-Server, mTLS only, read-only)

---

## 10. Coding Standards & Tooling

- .NET 9 LTS
- `<Nullable>enable</Nullable>`
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- EditorConfig + StyleCop.Analyzers
- xUnit + FluentAssertions + Testcontainers
- Conventional Commits (feat/fix/refactor/docs/test/chore)
- Git-Flow: `main` + Feature-Branches + PRs (auch im Solo-Betrieb für Versionsspur)
- ADRs (Architecture Decision Records) im `/docs/adr` Ordner
- Migrations: EF Core Migrations, niemals Schema-Änderungen ohne Migration
- Konfiguration: `appsettings.json` + Environment Variables (keine Secrets in Git → Azure KeyVault-kompatibel oder doppenv-style)

**Refactoring-Hinweise gegenüber bisherigem Code:**
- `PriceConfig` als statische Klasse → später in DB-Tabelle `TherapyTypes` (mit Preis pro Tenant), denn jeder zukünftige Kunde hat eigene Preise.

---

## 11. Deployment / Hosting

**Web/API (Hetzner Cloud, AT):**
- Server CX22 für Start (3 vCPU, 4GB RAM, 40GB SSD), später CCX
- Docker Compose: `caddy` (TLS), `aspnet-app`, `postgres`, `seq`, `uptime-kuma`
- Caddy übernimmt automatisches HTTPS via Let's Encrypt
- Nightly `pg_dump` + restic-Push zu Hetzner Storage Box (separater Tenant-Account)
- DNS direkt beim Registrar (kein Cloudflare wegen DSGVO und Health-Daten-Sensibilität)

**Praxis-Server (Mac mini, lokal):**
- Mac mini M2/M4, FileVault on, Touch ID + 2FA
- ASP.NET Core 9 als launchd-Service, läuft 24/7
- Lokale Postgres in Docker (oder native via Postgres.app)
- Time Machine + restic offsite (encrypted) zu Hetzner Storage Box
- mTLS-Client-Zertifikat lokal in Keychain
- Lokale TLS via mkcert für `https://praxis.local`
- Bluetooth zum reMarkable für PDF-Sync (manuell durch Therapeut)

---

## 12. MVP-Scope & Roadmap

### Phase 1 — Eigenbetrieb startklar (~3–4 Monate)

**Public Website:**
- Marketing-Seiten (Therapieangebot, Coaching, Über mich, Karriere-Timeline, Kontakt)
- Mehrsprachig DE/EN
- Honorartabelle (aus DB)
- Hinweis auf Krankenkassen-Wahltherapeuten-Rückerstattung

**Patient-Onboarding:**
- Registrierung mit Pflicht-Einwilligungen (Datenschutz, Schweigepflicht, AGB)
- Kurz-Fragebogen (selbst-formuliert)
- Lang-Fragebogen (selbst-formuliert)
- Erstgespräch-Buchung

**Buchungssystem:**
- WeeklyScheduleTemplate konfigurierbar pro Therapeut (Wochentag-basiert: Mo=Anzug, Di=Business-Casual, Mi=Hund, Do=Pferd-Location, Fr=Casual, etc.)
- Auto-Generierung der Slots 4 Monate vorab via Background-Job
- Modus-Filter beim Buchen: "anzug", "kein anzug", "egal"; analog für Hund, Pferd, Sport, Online, Mittagessen
- Doppelstunden-Logik (2 Slots zusammenführen, Modus muss konsistent sein)
- Stornierung: 24h-Regel serverseitig, danach 80% Verrechnung automatisch
- "Sonderwunsch"-Funktion (intern: EmergencyRequest) für Termine außerhalb der Slots oder mit anderem Modus → Therapeut bestätigt/lehnt ab/macht Gegenvorschlag

**Honorarnoten:**
- Auto-Erstellung nach abgehaltenem Termin
- Fortlaufende Nummer pro Kalenderjahr (RKSV-konform)
- PDF-Generierung (QuestPDF)
- Stornorechnung als Negativbeleg, referenziert Original
- Versand per Email
- Hinweis auf Kassenrückerstattung dynamisch

**Kommunikation:**
- Email via eigenem SMTP (Hetzner)
- SMS-Erinnerung 24h vor Termin (Messente)
- Termin-Bestätigung, Stornierung, Honorarnote, Gruppen-Erinnerung

**Interne Praxis-Software:**
- ASP.NET Core Razor App auf Mac mini
- Patient-Liste (read-only sync von API)
- Termin-Übersicht (read-only)
- Lokale Verlaufs-/Diagnose-Eingabe (ICD-10 + ICD-11 + Freitext)
- Manueller PDF-Upload für reMarkable-Dokumente
- Stimmungsskala 0–10 pro Sitzung (CBT-typisch)
- Hausaufgaben-Tracker (CBT-typisch)
- Lokale Backups via restic

**Sicherheit:**
- 2FA für alle Mitarbeiter-Rollen
- Audit-Log
- Soft Deletes
- Multi-Tenant-Struktur aktiv (auch wenn nur 1 Tenant)

### Phase 2 — Reife für Eigenbetrieb (+2 Monate)

- Gruppen-Verwaltung (1–3 Gruppen, fixe Termine, Mitglieder-Verwaltung, Auto-Erinnerungen)
- Video-Therapie via Jitsi-Link (selbstgehostete Instanz)
- Patient-Self-Service: Honorarnoten-Download, Termin-Historie
- DSGVO-Auskunfts-Export (Art. 15)
- Datenportabilität-Export (Art. 20)

### Phase 3 — Verkaufsreife (+3–4 Monate)

- Tenant-Onboarding-Flow (Self-Service: Praxis registriert sich, eigene DB wird provisioniert)
- White-Label (Logo, Farben, Texte konfigurierbar; eigene Domain via CNAME)
- AVV/DSFA-Vorlagen als Download für neue Kunden
- Lizenz-Management & Billing (Stripe oder Mollie EU)
- SystemAdmin-Panel
- Audit-Reports für DSB

### Phase 4 — Skalierung

- On-Premise-Deployment-Paket (Docker-Compose-Bundle für Konzerne)
- SSO via OIDC (Microsoft Entra etc.)
- API für Drittsysteme (Buchhaltung, Steuerberater)
- Embedded Video-Therapie (statt Link)
- Reporting-Dashboard für Praxisleiter
- Embedded Fragebogen-Bibliothek (eigene CBT-Tools, weiterhin keine lizenzpflichtigen Standard-Instrumente)

---

## 13. Offene Punkte / noch zu entscheiden

- [ ] Domain final wählen: Vorschlag `praxis-floeckinger.at` (+ `floeckinger.at` als Redirect falls frei) — "Ordination" ist berufsrechtlich Ärzten vorbehalten, daher nicht verwenden
- [ ] DSB-Frage (intern) klären, spätestens vor erstem SaaS-Verkauf
- [ ] Lizenzmodell SaaS vs. On-Prem vs. beides
- [ ] Demo-Tenant für Verkaufsgespräche (ja/nein, später)
- [ ] Konkrete Belastungs-Limits Hund (Stunden/Tag, Tage/Woche, Pausen) — Therapeut definiert, ins SlotTemplate
- [ ] Konkrete Belastungs-Limits Pferd
- [ ] Kurz- und Lang-Fragebogen-Inhalte ausformulieren (eigene CBT-orientierte Fragen, keine lizenzpflichtigen Instrumente wie BDI/GAD-7)
- [ ] Honorarnoten-Layout (Logo, Fußzeile, Bankverbindung, ggf. UID)
- [ ] Liste der konkreten Krankenkassen + Rückerstattungs-Hinweise (oder generisch "ÖGK/SVS/BVAEB — bitte beim eigenen Träger erfragen")
- [ ] Logos und Praxis-Fotos final erstellen
- [ ] Brand-Farben + Schriftarten festlegen
- [ ] AGB-Text erstellen lassen (Anwalt/Notar)
- [ ] Datenschutzerklärung erstellen lassen (DSB oder spezialisierter Anwalt)
- [ ] Versicherung Berufshaftpflicht + Cyber-Versicherung prüfen

---

## 14. Wichtige Referenzen

**Recht (Österreich):**
- Psychotherapiegesetz (PthG), insbesondere §15 (Aufbewahrung), §17 (Verschwiegenheit)
- Datenschutzgesetz (DSG)
- DSGVO Art. 6, 9, 15, 17, 20, 30, 32
- Bundesabgabenordnung §132 (RKSV)
- Konsumentenschutzgesetz (für Patient-AGBs)

**Technik:**
- ASP.NET Core 9 Docs
- EF Core 9 Docs
- PostgreSQL 16 Docs (insb. Row-Level Security, pgcrypto)
- OWASP Top 10 + ASVS
- BSI-Grundschutz für Praxis-Hosting
- ENISA Healthcare Cybersecurity Guidelines

**Berufsorganisation:**
- Österreichischer Bundesverband für Psychotherapie (ÖBVP)
- WKO Psychotherapeuten

---

## 15. Tonalität & Markenprofil (für Frontend-Design)

Aus dem Lebenslauf des Praxisinhabers ergibt sich ein klares Profil:
- militärische Struktur (Theresianische Militärakademie, Kommando Landstreitkräfte, Französisch-Guayana-Überlebenstraining)
- Hochleistungs-Hintergrund (Ultramarathon, paralleles Doppelstudium)
- analytisch-wirtschaftliche Vorbildung (HTL Wirtschaftsingenieurwesen)
- Schwerpunkt Verhaltenstherapie + High-Performance-Coaching

**Design-Implikation:** klar, strukturiert, reduziert, Vertrauen durch Klarheit statt durch Wärme-Kitsch. Typografie geometrisch (z.B. Inter, IBM Plex Sans, GT Pressura). Farbpalette gedeckt, hochwertig (Anthrazit, gebrochenes Weiß, ein einziger Akzent in z.B. tiefem Burgund oder Forest Green). Keine Stockfotos mit "lächelnde Personen am Sofa". Echte Fotos der Praxis und des Therapeuten in seinen verschiedenen Settings (Anzug, Casual, mit Hund/Pferd, beim Laufen) als USP — das ist authentisch und differenzierend.

---

*Ende des Leitfadens. Bei Aktualisierungen Versionsdatum oben anpassen.*
