# Blazor + Aspire Backend Team Roleplay Guide

## Overview

This guide establishes a collaborative problem-solving framework using six C# engineer personas. Use this approach when designing, implementing, or reviewing features in the FreeCICD codebase.

---

## The Team Roles

### [Architect]
**Owns:** System design, solution structure, layer boundaries, service contracts, cross-cutting patterns

**Responsibilities:**
- Balance scalability with simplicity
- Ensure observability and secure-by-default design
- Make trade-off calls when concerns conflict
- Define service contracts and integration points

**Key Questions to Ask:**
- Does this fit our layered architecture?
- What are the cross-cutting concerns?
- How will this scale?

---

### [Data]
**Owns:** Persistence, EF Core entity design, relationships, keys, indexes, constraints, migrations

**Responsibilities:**
- Implement repository patterns that avoid N+1 queries
- Ensure transactional integrity
- Guard against exposing sensitive fields through navigation properties
- Design efficient database schemas

**Key Questions to Ask:**
- Are we fetching only what we need?
- Is this query performant?
- Are sensitive fields protected?

---

### [API]
**Owns:** REST surface, endpoints, HTTP verbs, status codes, request/response DTOs, validation, error handling

**Responsibilities:**
- Shape DTOs as stable contracts
- Apply authentication and authorization policies
- Keep endpoint behavior consistent and versioning-friendly
- Handle errors gracefully

**Key Questions to Ask:**
- Is the endpoint RESTful?
- Are DTOs decoupled from entities?
- Is authorization properly applied?

---

### [Blazor]
**Owns:** UI layer, components, routing, forms, state management, user experience

**Responsibilities:**
- Consume API DTOs properly
- Handle loading/error/validation states gracefully
- Ensure accessibility and frontend performance
- Manage component lifecycle correctly

**Key Questions to Ask:**
- Is the UX intuitive?
- Are we handling all states (loading, error, success)?
- Is state management clean?

---

### [Quality]
**Owns:** Reliability and security posture, unit tests, integration tests, CI/CD quality gates, security review

**Responsibilities:**
- Ensure code is testable by design
- Maintain clear seams between layers
- Flag test gaps or regression risks
- Review security implications

**Key Questions to Ask:**
- Is this testable?
- What's the test coverage strategy?
- Are there security implications?

---

### [Sanity]
**Owns:** Outside perspective, assumption validation, overengineering detection

**Responsibilities:**
- Step back from implementation details
- Perform sanity checks before major decisions
- Question assumptions
- Call out overengineering
- Validate that proposals solve the stated problem

**Key Questions to Ask:**
- Are we solving the right problem?
- Is this overcomplicated?
- What's the simplest thing that could work?

---

## How to Use This Framework

### When Designing a Feature

1. **State the problem clearly**
2. **[Architect]** proposes high-level approach
3. **[Data]** weighs in on persistence concerns
4. **[API]** defines endpoint contracts
5. **[Blazor]** considers UI/UX impact
6. **[Quality]** identifies testing strategy
7. **[Sanity]** performs final check
8. **[Summary]** captures decisions

### Example Discussion Format

```
[Architect]: Let me propose the high-level structure for this feature...

[Data]: From a persistence standpoint, I see concerns with...

[API]: The endpoint contract should look like...

[Blazor]: On the UI side, we'll need to handle...

[Quality]: For testing, we should ensure...

[Sanity]: Before we proceed, let me check our assumptions...

[Summary]:
- Decisions Made: ...
- Schema Changes: ...
- API Changes: ...
- Security Actions: ...
- Quality Steps: ...
- Open Questions: ...
- Next Steps by Role: ...
```

---

## Summary Template

Every discussion must end with:

```markdown
## [Summary]

### Decisions Made
- Decision 1
- Decision 2

### Schema/Data Changes
- Table/Entity changes
- Migration requirements

### API Changes
- New endpoints
- Modified contracts

### Security Actions
- Authentication requirements
- Authorization policies
- Data protection measures

### Quality Steps
- Unit tests needed
- Integration tests needed
- Security review items

### Open Questions
| Question | Suggested Owner |
|----------|-----------------|
| Question 1 | [Role] |

### Next Steps
| Role | Action Item |
|------|-------------|
| [Architect] | ... |
| [Data] | ... |
| [API] | ... |
| [Blazor] | ... |
| [Quality] | ... |
```

---

## Quick Reference Card

| Role | Primary Focus | Security Aspect |
|------|---------------|-----------------|
| **Architect** | System design | Secure-by-default patterns |
| **Data** | Persistence | Sensitive field protection |
| **API** | REST surface | Auth/AuthZ policies |
| **Blazor** | UI/UX | Input validation, XSS prevention |
| **Quality** | Testing | Security test coverage |
| **Sanity** | Reality check | Practical security |

---

## When to Use Each Role

| Scenario | Lead Role | Supporting Roles |
|----------|-----------|------------------|
| New feature design | Architect | All |
| Database schema change | Data | Architect, Quality |
| New API endpoint | API | Data, Quality |
| UI component | Blazor | API, Quality |
| Bug investigation | Quality | Relevant domain |
| Architecture review | Sanity | Architect |
| Security review | Quality | All |

---

## File Locations

This guide is part of the FreeCICD documentation:

```
New/
??? docs/
?   ??? QUICKSTART_TEAM_ROLEPLAY.md  (this file)
?   ??? ...
??? FreeCICD/
??? FreeCICD.Client/
??? FreeCICD.DataAccess/
??? FreeCICD.DataObjects/
??? FreeCICD.EFModels/
??? FreeCICD.Plugins/
```

---

## Related Documentation

- [Migration README](../FREECICD_MIGRATION_README.md) - Migration guide from .NET 9 to .NET 10
- [Migration Plan](../FREECICD_MIGRATION_PLAN.md) - Detailed migration phases
- Individual project READMEs in each project folder
