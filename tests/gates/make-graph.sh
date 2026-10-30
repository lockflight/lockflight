#!/usr/bin/env bash
# A synthetic .NET solution of ten projects with a layered project-reference graph, for the incremental Stop gate:
#   Core <- Domain <- Data <- Api ; Domain <- Worker ; Core <- Cli (a leaf without tests)
#   Core.Tests -> Core ; Domain.Tests -> Domain ; Api.Tests -> Api ; Worker.Tests -> Worker
#   bash tests/gates/make-graph.sh <dir> [mtp|vstest]
# Creates <dir> as a git repository with one commit and a Lockflight manifest; mtp (default) uses xUnit v3 on
# Microsoft.Testing.Platform, vstest uses xUnit v2. Needs network access to restore the test packages once.
set -euo pipefail

root="${1:?usage: make-graph.sh <dir> [mtp|vstest]}"
runner="${2:-mtp}"
mkdir -p "$root"
cd "$root"

project() { # name, references (space separated names), test (yes|no)
  local name="$1" refs="$2" test="$3" dir
  if [ "$test" = yes ]; then dir="tests/$name"; else dir="src/$name"; fi
  mkdir -p "$dir"
  {
    echo '<Project Sdk="Microsoft.NET.Sdk">'
    echo '  <PropertyGroup>'
    if [ "$test" = yes ] && [ "$runner" = mtp ]; then echo '    <OutputType>Exe</OutputType>'; fi
    echo '    <IsPackable>false</IsPackable>'
    echo '  </PropertyGroup>'
    if [ "$test" = yes ]; then
      echo '  <ItemGroup>'
      if [ "$runner" = mtp ]; then
        echo '    <PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" />'
      else
        echo '    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />'
        echo '    <PackageReference Include="xunit" Version="2.9.2" />'
        echo '    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />'
      fi
      echo '    <Using Include="Xunit" />'
      echo '  </ItemGroup>'
    fi
    if [ -n "$refs" ]; then
      echo '  <ItemGroup>'
      for ref in $refs; do
        case "$ref" in *.Tests) echo "    <ProjectReference Include=\"../../tests/$ref/$ref.csproj\" />" ;; *) echo "    <ProjectReference Include=\"../../src/$ref/$ref.csproj\" />" ;; esac
      done
      echo '  </ItemGroup>'
    fi
    echo '</Project>'
  } > "$dir/$name.csproj"
  local ns="${name//./}"
  if [ "$test" = yes ]; then
    local target="${name%.Tests}"
    cat > "$dir/${ns}Checks.cs" <<CS
namespace ${ns};

public sealed class ${ns}Checks
{
    [Fact]
    public void ItsLayerAnswers() => Assert.Equal("${target}", ${target}.${target}Layer.Name);
}
CS
  else
    if [ "$name" = Cli ]; then
      printf 'System.Console.WriteLine(Core.CoreLayer.Name);\n' > "$dir/Program.cs"
      sed -i.bak 's|<IsPackable>false</IsPackable>|<IsPackable>false</IsPackable>\n    <OutputType>Exe</OutputType>|' "$dir/$name.csproj" && rm "$dir/$name.csproj.bak"
    else
      cat > "$dir/${name}Layer.cs" <<CS
namespace ${name};

public static class ${name}Layer
{
    public static string Name => "${name}";
}
CS
    fi
  fi
}

project Core "" no
project Domain "Core" no
project Data "Domain" no
project Api "Data" no
project Worker "Domain" no
project Cli "Core" no
project Core.Tests "Core" yes
project Domain.Tests "Domain" yes
project Api.Tests "Api" yes
project Worker.Tests "Worker" yes

cat > Directory.Build.props <<'XML'
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
XML
{
  echo '<Solution>'
  for dir in src/Core src/Domain src/Data src/Api src/Worker src/Cli tests/Core.Tests tests/Domain.Tests tests/Api.Tests tests/Worker.Tests; do
    echo "  <Project Path=\"$dir/$(basename "$dir").csproj\" />"
  done
  echo '</Solution>'
} > Graph.slnx
if [ "$runner" = mtp ]; then
  printf '{\n  "sdk": { "version": "10.0.102", "rollForward": "latestFeature" },\n  "test": { "runner": "Microsoft.Testing.Platform" }\n}\n' > global.json
else
  printf '{\n  "sdk": { "version": "10.0.102", "rollForward": "latestFeature" }\n}\n' > global.json
fi
printf 'version: 1\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/local/\n' > .gitignore
git init -q
git add -A
git -c user.email=graph@example.com -c user.name=graph commit -qm "graph fixture"
