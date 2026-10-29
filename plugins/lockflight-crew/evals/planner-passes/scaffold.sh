#!/usr/bin/env bash
# The library project with an approved two-story spec (the stand-in engine reads .lockflight/standin-spec-approved as the
# approval) and no plan or tasks yet. The planner must write plan.md and tasks.md until the stand-in's `phase check tasks`
# passes, and leave spec.md and the code alone.
set -euo pipefail
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
standin="$here/../_fixtures/lockflight-standin.sh"
[ -f "$standin" ] || { echo "scaffold: engine stand-in not found at $standin" >&2; exit 1; }
mkdir -p src/Library tests/Library.Tests .lockflight/bin
printf 'version: 1\n' > lockflight.yaml
printf '.lockflight/\n' > .gitignore
cp "$standin" .lockflight/bin/lockflight && chmod +x .lockflight/bin/lockflight
cat > src/Library/Library.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
</Project>
XML
cat > src/Library/Loan.cs <<'CS'
namespace Library;

/// <summary>A book lent to a member: when it was due and when it came back.</summary>
public sealed record Loan(string MemberId, DateOnly DueOn, DateOnly? ReturnedOn, CardType Card);
CS
cat > src/Library/CardType.cs <<'CS'
namespace Library;

/// <summary>The kind of library card a member holds.</summary>
public enum CardType
{
    /// <summary>An adult card.</summary>
    Standard = 0,

    /// <summary>A student card.</summary>
    Student = 1,
}
CS
cat > tests/Library.Tests/Library.Tests.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" /></ItemGroup>
  <ItemGroup><ProjectReference Include="../../src/Library/Library.csproj" /></ItemGroup>
  <ItemGroup><Using Include="Xunit" /></ItemGroup>
</Project>
XML
mkdir -p specs/001-overdue-fines
cat > specs/001-overdue-fines/spec.md <<'MD'
# Feature Specification: Overdue fines

**Feature**: `001-overdue-fines`

## User Scenarios & Testing

### User Story 1 - Fine a late return (Priority: P1)

A librarian sees the fine for a book returned late.

**Acceptance Scenarios**:

1. **Given** a loan due on 2026-03-10 returned on 2026-03-10, **When** the fine is computed, **Then** it is 0.00
2. **Given** a loan due on 2026-03-10 returned on 2026-03-13, **When** the fine is computed, **Then** it is 0.60
3. **Given** a loan returned 40 days late, **When** the fine is computed, **Then** it is 5.00

### User Story 2 - Students pay half (Priority: P2)

**Acceptance Scenarios**:

1. **Given** a student loan returned 3 days late, **When** the fine is computed, **Then** it is 0.30

## Requirements

### Functional Requirements

- **FR-001**: System MUST charge 0.20 for every day a loan is returned after its due date, and nothing on or before it
- **FR-002**: System MUST cap a fine at 5.00
- **FR-003**: System MUST halve the fine for a student card, after the cap
MD
touch .lockflight/standin-spec-approved
git init -q && git add -A && git -c user.email=eval@example.com -c user.name=eval commit -qm fixture
