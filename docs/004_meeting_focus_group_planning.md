# 004 — Meeting: Focus Group Planning (Dashboard & Wizard Review)

> **Document ID:** 004  
> **Category:** Meeting  
> **Purpose:** Determine who should attend the Dashboard & Wizard code review focus group  
> **Attendees:** [Architect], [Quality], [Sanity]  
> **Date:** 2024-12-19  
> **Predicted Outcome:** Defined attendee list with clear roles  
> **Actual Outcome:** ✅ Team assembled  
> **Resolution:** Proceed to focus group meeting (doc 005)

---

## Context

We need to run a focus group / code review session for the two main user-facing features:
1. **Pipeline Dashboard** — The main view showing all pipelines
2. **Pipeline Wizard** — The multi-step form for creating/editing pipelines

The goal is to get diverse perspectives on UX, code quality, and potential improvements.

---

## Discussion

**[Architect]:** Alright team, we need to assemble the right group for reviewing the Dashboard and Wizard. These are our two main user touchpoints. Who should be in the room?

**[Quality]:** For a thorough review, I'd suggest we need representation from:
- Someone who understands the overall architecture
- Frontend expertise for the Blazor components
- Backend knowledge for the DataAccess layer
- A fresh perspective to catch things we've become blind to
- Someone focused on user experience

**[Architect]:** Agreed. Let me map that to our team structure:

| Perspective Needed | Role | Why |
|-------------------|------|-----|
| System overview | [Architect] | Understands how pieces fit together |
| Blazor/UI | [Frontend] | Component patterns, state management |
| API/Data | [Backend] | DataAccess methods, performance |
| Testing/Docs | [Quality] | Coverage gaps, documentation |
| Fresh eyes | [JrDev] | Will ask "why" questions we forgot to ask |
| Complexity check | [Sanity] | Keeps us from overengineering |

**[Sanity]:** That's six perspectives. Is that too many for a focused review?

**[Architect]:** Good point. Let's keep it focused:

### Core Attendees (Must Have)
- **[Architect]** — Frame discussions, keep scope
- **[Frontend]** — Primary code owner for Blazor components
- **[Backend]** — Primary code owner for DataAccess
- **[Quality]** — Document findings, check coverage

### Optional/Rotational
- **[JrDev]** — Fresh perspective, learning opportunity
- **[Sanity]** — Complexity checks (can chime in as needed)

**[Quality]:** What about actual users? Should we include someone who uses the tool daily?

**[Architect]:** Good call. Let's add:
- **[User]** — Representative voice for "how it actually feels to use this"

**[Sanity]:** Mid-check: Are we overcomplicating this? It's a code review, not a summit.

**[Architect]:** Fair. Let's keep it to 5 active voices max, with [Sanity] available to jump in.

---

## Final Attendee List

| Role | Focus During Review | Key Questions |
|------|---------------------|---------------|
| **[Architect]** | Structure, patterns, boundaries | "Does this fit the architecture?" |
| **[Frontend]** | Blazor components, UX, state | "Is this the right component pattern?" |
| **[Backend]** | DataAccess, API, performance | "Is this efficient? Secure?" |
| **[Quality]** | Tests, docs, edge cases | "How do we test this? What's missing?" |
| **[JrDev]** | Clarifying questions | "Why does it work this way?" |

**[Sanity]** will provide periodic complexity checks.

---

## Review Scope

### Dashboard Review
- `Pipelines.App.FreeCICD.razor` — Main orchestrator
- `PipelineTableView.App.FreeCICD.razor` — Table view
- `PipelineCard.App.FreeCICD.razor` — Card view
- `BranchBadge.razor` — Reusable component
- Supporting filter/control components

### Wizard Review
- `Index.App.FreeCICD.razor` — Wizard orchestrator
- Step components (PAT, Project, Repo, Branch, etc.)
- `DataAccess.App.FreeCICD.cs` — API methods

---

## Logistics

- **Format:** Async-friendly code review (can be run as discussion)
- **Duration:** ~45 min Dashboard, ~45 min Wizard
- **Output:** Documented feedback, prioritized action items

---

## Next Steps

| Action | Owner | Priority |
|--------|-------|----------|
| Run Dashboard & Wizard focus group | [Quality] | P1 |
| Document findings | [Quality] | P1 |
| Synthesize feedback into action plan | [Architect] | P2 |

---

*Created: 2024-12-19*  
*Maintained by: [Quality]*
