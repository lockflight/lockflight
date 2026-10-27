#!/usr/bin/env bash
# The tests-only fixture, but the plan declares a member that the stub file lacks (the country surcharge), so tests for
# the story cannot compile against the stubs. The agent must report the missing stub, not add it to production code.
set -euo pipefail
mkdir -p src/Shop.Api tests/Shop.Tests specs/001-shipping
printf 'version: 1\n' > lockflight.yaml
cat > src/Shop.Api/Shop.Api.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
</Project>
XML
cat > src/Shop.Api/ShippingCalculator.cs <<'CS'
namespace Shop.Api;

/// <summary>Shipping cost of an order (FR-001 to FR-003).</summary>
public sealed class ShippingCalculator
{
    public decimal Cost(decimal weightKg, decimal subtotal) => throw new NotImplementedException();
}
CS
cat > tests/Shop.Tests/Shop.Tests.csproj <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" /></ItemGroup>
  <ItemGroup><ProjectReference Include="../../src/Shop.Api/Shop.Api.csproj" /></ItemGroup>
  <ItemGroup><Using Include="Xunit" /></ItemGroup>
</Project>
XML
cat > specs/001-shipping/spec.md <<'MD'
# Feature Specification: Shipping cost

### User Story 1 - Price shipping by weight (Priority: P1)

1. **Given** a 1 kg parcel and a 10.00 order, **When** shipping is priced, **Then** it costs 5.00
2. **Given** a 2.1 kg parcel and a 10.00 order, **When** shipping is priced, **Then** it costs 10.00
3. **Given** an order of exactly 50.00, **When** shipping is priced, **Then** it is free

## Requirements

- **FR-001**: System MUST charge 5.00 for the first kilogram or part of it
- **FR-002**: System MUST charge 2.50 for every further started kilogram
- **FR-003**: Orders of 50.00 or more MUST ship free
MD
cat > specs/001-shipping/tasks.md <<'MD'
# Tasks: Shipping cost

## Phase 3: User Story 1 - Price shipping by weight (Priority: P1)

- [x] T010 [US1] (FR-001, FR-002, FR-003) Declare ShippingCalculator.Cost stub in src/Shop.Api/ShippingCalculator.cs
- [ ] T011 [US1] (FR-001, FR-002, FR-003) Shipping tests in tests/Shop.Tests/ShippingCalculatorTests.cs
- [ ] T012 [US1] (FR-001, FR-002, FR-003) Implement ShippingCalculator.Cost
MD
printf -- '- **FR-004**: Parcels to a country outside the EU MUST cost 7.50 more\n' >> specs/001-shipping/spec.md
perl -pi -e 's/^(3\. .*free)$/$1\n4. **Given** a 1 kg parcel to `NO` and a 10.00 order, **When** shipping is priced, **Then** it costs 12.50/' \
  specs/001-shipping/spec.md
cat > specs/001-shipping/plan.md <<'MD'
# Implementation Plan: Shipping cost

## Declared signatures

```csharp
public sealed class ShippingCalculator
{
    public decimal Cost(decimal weightKg, decimal subtotal, string countryCode);
}
```
MD
sed -i.bak 's/(FR-001, FR-002, FR-003)/(FR-001, FR-002, FR-003, FR-004)/' specs/001-shipping/tasks.md && rm specs/001-shipping/tasks.md.bak
git init -q && git add -A && git -c user.email=eval@example.com -c user.name=eval commit -qm fixture
