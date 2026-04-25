# ADR-0001: Database-per-Tenant Multi-Tenancy Strategy

**Date:** 2026-04-25
**Status:** Accepted
**Deciders:** Tobias Flöckinger

---

## Context

The platform is designed from day one to support multiple tenants (therapy practices),
even though the initial deployment will serve only a single tenant (Praxis Flöckinger).
A multi-tenancy strategy must be chosen that satisfies the following constraints:

- **GDPR / §15 PthG:** Patient data of different practices must be strictly isolated.
  A data breach at one tenant must not expose another tenant's data.
- **DSGVO Art. 17 / DSGVO Art. 20:** "Right to erasure" and "Right to data portability"
  must be implementable per tenant without affecting others.
- **Operational simplicity:** Backups, restores, and offboarding of a single tenant
  (e.g. customer cancellation, on-premise export) must be straightforward.
- **Future SaaS & On-Premise:** The same codebase must support both cloud-hosted
  multi-tenant SaaS and on-premise deployments for enterprise customers
  (where the customer receives only their own database).
- **Developer velocity (solo dev):** The solution must be understandable and
  maintainable without dedicated infrastructure team support.

The three common strategies considered:

1. **Shared DB, shared schema** — all tenants in same tables, discriminated by a `TenantId` column.
2. **Shared DB, separate schemas** — PostgreSQL schemas per tenant, same DB cluster.
3. **Database per tenant** — each tenant gets their own PostgreSQL database.

## Decision

We adopt **Database-per-Tenant** multi-tenancy from day one.

Implementation:
- A **master database** (`tenants_master`) stores only tenant metadata:
  `Tenants`, `SystemAdmins`, `LicenseEvents`. No patient data ever enters this DB.
- Each tenant receives a dedicated PostgreSQL database named `tenant_<guid>`.
- A `TenantResolverMiddleware` reads the subdomain from `HttpContext.Request.Host`,
  looks up the tenant in the master DB, and injects `ITenantContext` into the DI container.
- An `ITenantDbContextFactory` creates the correct `DbContext` instance per request
  using the resolved connection string.
- EF Core Migrations are applied per-tenant database. A `MigrationRunner` service
  handles applying pending migrations on startup and during tenant provisioning.

## Consequences

### Positive
- Maximum data isolation: a bug, misconfiguration, or SQL injection at one tenant
  cannot expose another tenant's data.
- Backup and restore are trivially scoped: `pg_dump tenant_<guid>`.
- Customer offboarding = export the DB, then DROP DATABASE. Clean and auditable.
- On-premise customers receive exactly one database. No shared-infrastructure risk.
- No risk of accidentally leaking data through a missing `WHERE TenantId = ...` clause.
- PostgreSQL-level isolation enables future Row-Level Security or encryption-at-rest
  per tenant without cross-tenant complications.

### Negative / Trade-offs
- Higher operational overhead at scale: 100 tenants = 100 databases, 100 migration runs.
- Connection pooling is more complex (PgBouncer must be configured per-tenant, or
  we use NpgsqlDataSource per tenant with a bounded pool).
- Cross-tenant analytics (if ever needed for platform-level reporting) requires
  either a federation layer or a separate analytics sink.
- Schema migrations must be applied to every tenant DB, not just once globally.

### Neutral
- At the current scale (1 tenant in Phase 1, target ~50 tenants in Phase 3),
  the operational overhead is negligible.
- The `MigrationRunner` can apply migrations lazily (on first request) or eagerly
  (on startup / via CLI). Decision deferred to Infrastructure implementation.

## Alternatives Considered

| Alternative | Why rejected |
|---|---|
| Shared DB, shared schema | A single missing `WHERE TenantId = ...` clause leaks all patient data across tenants. Violates GDPR isolation guarantees. Unacceptable for healthcare data. |
| Shared DB, separate schemas | Better isolation than shared schema, but still within one DB cluster: a cluster-level backup or restore affects all tenants. On-premise export is complicated. PG schema isolation is also weaker than DB isolation. |
