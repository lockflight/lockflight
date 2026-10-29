#!/usr/bin/env bash
# A small .NET library project set up with Lockflight and no feature yet. The engine is a stand-in
# (../_fixtures/lockflight-standin.sh) whose `phase check specify` applies the main rules of the real check, so the spec
# writer can reach a verdict.
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
git init -q && git add -A && git -c user.email=eval@example.com -c user.name=eval commit -qm fixture
