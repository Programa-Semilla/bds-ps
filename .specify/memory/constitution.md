<!--
Sync Impact Report
==================
Version change: 1.0.0 → 1.1.0
Modified principles: N/A
Modified sections:
  - Technology Standards — Runtime updated .NET 8+ → .NET 10 (LTS); File Storage updated
    from "local file system (initial)" to the shipped pluggable IObjectStorage model
    (Azurite dev / Azure Blob prod / local-filesystem fallback, spec 014).
Added/removed sections: None
Templates requiring updates:
  - .specify/templates/plan-template.md — ✅ no update needed (Constitution Check section is generic)
  - .specify/templates/spec-template.md — ✅ no update needed
  - .specify/templates/tasks-template.md — ✅ no update needed
  - .specify/templates/checklist-template.md — ✅ no update needed
  - .specify/templates/agent-file-template.md — ✅ no update needed
Follow-up TODOs: None
-->

# FundingPlatform Constitution

## Core Principles

### I. Clean Architecture

All features MUST follow Clean Architecture with four core layers and strict dependency rules:

- **Domain**: Entities, value objects, enums, repository interfaces. Zero external dependencies.
- **Application**: Use cases (commands/queries), DTOs, service interfaces. Depends only on Domain.
- **Infrastructure**: EF Core persistence, file storage, Identity configuration. Depends on Application (and transitively Domain).
- **Web**: ASP.NET MVC controllers, views, view models. Depends on Application and Infrastructure.

The dependency rule is non-negotiable: dependencies MUST point inward. Web and Infrastructure MUST NOT be referenced by Domain or Application. Cross-cutting concerns (Aspire ServiceDefaults) are the sole exception.

### II. Rich Domain Model

Domain entities MUST encapsulate their own business rules and invariants. Validation logic, state transitions, and collection management belong in the entity, not in service or controller layers.

- State transitions (e.g., Draft → Submitted) MUST be gated by domain validation methods on the entity itself.
- Entities MUST expose behavior methods (e.g., `AddItem()`, `Submit()`, `Validate()`) rather than exposing raw state for external manipulation.
- Anemic models with logic scattered across services are prohibited.

### III. End-to-End Testing (NON-NEGOTIABLE)

Every feature MUST have Playwright end-to-end tests that validate user flows through the browser. This is the primary quality gate.

- E2E tests run against the full Aspire-orchestrated stack (AppHost → Web → SQL Server).
- Every user story MUST have corresponding E2E tests covering the golden path and key error scenarios.
- Page Object Model pattern MUST be used for test maintainability.
- Tests MUST be independently runnable per user story.
- Unit and integration tests complement E2E tests but do not replace them.

### IV. Schema-First Database Management

The SQL Server Database Project (dacpac) is the single source of truth for ALL database schema, including ASP.NET Identity tables.

- EF Core is used for data access only. EF migrations and `EnsureCreated` are prohibited.
- Schema changes MUST be made by editing `.sql` files in the Database project.
- The dacpac handles deployment, diffing, and schema upgrades across environments.
- Seed data MUST be managed via post-deployment scripts in the Database project.

### V. Specification-Driven Development

Every feature MUST follow the structured specification workflow before implementation begins:

- **Spec** (`spec.md`): User stories with priorities, acceptance scenarios, functional requirements, success criteria.
- **Plan** (`plan.md`): Technical design, project structure, constitution check, complexity tracking.
- **Tasks** (`tasks.md`): Phased task list organized by user story with explicit dependencies and parallel opportunities.
- **Implementation**: Code written against the spec and plan, not ad-hoc.

User stories MUST be independently testable and deliverable. Each story MUST be a standalone slice of functionality that can be developed, tested, and demonstrated independently.

### VI. Simplicity and Progressive Complexity

Start with the simplest viable approach. Complexity MUST be justified and tracked.

- YAGNI: Do not build for hypothetical future requirements. Defer explicitly (e.g., "deferred to a later spec").
- Sensible defaults MUST be provided for all configurable values; missing configuration MUST NOT crash the system.
- Abstractions MUST serve a current need, not a speculative one. The interface-based file storage design is justified because storage backend changes are planned, not speculative.
- When complexity is unavoidable, document the justification in the plan's Complexity Tracking section.

## Technology Standards

The following technology stack is mandated for all features in this project:

| Component | Technology | Constraint |
|-----------|-----------|------------|
| Runtime | .NET 10 (LTS) | All projects target `net10.0` (EF Core 10) |
| Web Framework | ASP.NET MVC | Server-side rendering, no SPA frameworks |
| Orchestration | .NET Aspire | Service discovery, health checks, telemetry |
| Database | SQL Server | Aspire-managed container for dev, persistent instance for prod |
| Schema Management | SQL Server Database Project (dacpac) | No EF migrations |
| ORM | Entity Framework Core | Data access only, Code First model mapping |
| Authentication | ASP.NET Identity | Self-managed users with role-based authorization |
| E2E Testing | Playwright for .NET | NUnit test runner, Page Object Model pattern |
| File Storage | Pluggable `IObjectStorage` | Azurite (dev) / Azure Blob (prod) / local-filesystem fallback (spec 014) |

Adding new technologies or frameworks MUST be documented in the feature plan and justified against existing stack capabilities.

## Development Workflow

### Feature Lifecycle

1. **Brainstorm** → Capture requirements and constraints
2. **Specify** → Write spec.md with user stories, acceptance scenarios, requirements
3. **Plan** → Write plan.md with technical design, constitution check, structure
4. **Tasks** → Write tasks.md organized by user story with dependencies
5. **Implement** → Code against spec and plan, commit after each task or logical group
6. **Test** → E2E tests MUST pass for every user story before the feature is complete
7. **Review** → Verify all acceptance scenarios and success criteria are met

### Quality Gates

- Constitution Check MUST pass before research begins and again after design (documented in plan.md).
- Every user story MUST have passing E2E tests covering golden path and error scenarios.
- All validation errors MUST be collected and displayed at once (not one at a time).
- Optimistic concurrency MUST be used for entities with concurrent edit risk.
- Authorization checks MUST verify resource ownership (e.g., applicant owns the application).

### Commit Discipline

- Commit after each task or logical group of tasks.
- Each user story should be independently completable and testable at its checkpoint.
- Stop at any checkpoint to validate the story independently before proceeding.

## Governance

This constitution supersedes all ad-hoc practices. All development work MUST comply with the principles defined above.

### Amendment Procedure

1. Propose the change with rationale in a constitution update.
2. Document the version bump (MAJOR for incompatible changes, MINOR for additions, PATCH for clarifications).
3. Update the Sync Impact Report with affected templates and artifacts.
4. Propagate changes to dependent templates and active specs as needed.

### Versioning Policy

The constitution version follows semantic versioning:

- **MAJOR**: Backward-incompatible principle removals or redefinitions.
- **MINOR**: New principle or section added, or materially expanded guidance.
- **PATCH**: Clarifications, wording, typo fixes, non-semantic refinements.

### Compliance Review

- The plan.md Constitution Check section MUST evaluate all principles before implementation begins.
- Complexity violations MUST be tracked in plan.md's Complexity Tracking table.
- Refer to CLAUDE.md for runtime development guidance and active command reference.

**Version**: 1.1.0 | **Ratified**: 2026-04-15 | **Last Amended**: 2026-06-21
