#!/usr/bin/env bash
# A small Lockflight-shaped .NET project: one approved story, its stub declared, an xUnit v3 test project. The engine is not
# installed, so the agent cannot run the red itself and has to report that.
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
git init -q && git add -A && git -c user.email=eval@example.com -c user.name=eval commit -qm fixture
