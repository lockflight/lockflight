using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>
/// Builds reproductions into a test project without touching its files or its build output, then runs them. The generated
/// source and a props file live in a fresh temporary directory; the props file (passed as <c>CustomAfterMicrosoftCommonProps</c>)
/// adds the source to that one project only and sends every project of the build to its own <c>bin/lockflight-review/</c> and
/// <c>obj/lockflight-review/</c> (or a review's own folder, <see cref="ReviewBuildFolder"/>), so the developer's sources, <c>bin/Debug</c> and <c>obj/Debug</c> stay as they were and a
/// build the developer runs at the same time does not collide with it. The review output folders are kept between runs, so the
/// next review builds incrementally. Analyzers are off and warnings stay warnings: a reproduction is judged by whether it
/// compiles and fails, not by the project's style rules. For a fix check, the props file also swaps production files for their
/// fixed copies in the temporary directory (in the project that compiles each), and the run includes the tests named to run alongside.
/// </summary>
internal static class ReproductionBuild
{
    public const string Namespace = "LockflightReproductions";
    public const string OutputFolder = "lockflight-review";

    private const string PropsFileName = "Reproductions.props";

    /// <summary>The generated file's lines before its using directives (the auto-generated marker and <c>#nullable enable</c>).</summary>
    private const int LinesBeforeUsings = 2;

    private static readonly string[] BuildProperties = ["-p:TreatWarningsAsErrors=false", "-p:WarningsAsErrors=", "-p:RunAnalyzersDuringBuild=false"];

    public static Task<ReproductionBuildResult> RunAsync(
        TestApp app,
        IReadOnlyList<ReproductionCandidate> candidates,
        IReadOnlyList<string> usings,
        string root,
        string outputFolder,
        TimeSpan timeLimit,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        RunAsync(app, candidates, usings, [], [], root, outputFolder, timeLimit, time, cancellationToken);

    /// <param name="fixedFiles">Production files to build as a fix changes them; the files on disk stay as they are.</param>
    /// <param name="alsoRun">Method filters of the project's own tests to run with the reproductions.</param>
    public static async Task<ReproductionBuildResult> RunAsync(
        TestApp app,
        IReadOnlyList<ReproductionCandidate> candidates,
        IReadOnlyList<string> usings,
        IReadOnlyList<FixedFile> fixedFiles,
        IReadOnlyList<string> alsoRun,
        string root,
        string outputFolder,
        TimeSpan timeLimit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (TestProjectProfiles.ProjectFile(app.ProjectDirectory, app.AssemblyName) is not string project)
        {
            return new ReproductionBuildResult(ReproductionBuildStatus.NotRun, [], []);
        }

        DirectoryInfo scratch = Directory.CreateTempSubdirectory("lfr-");
        try
        {
            string source = Path.Combine(scratch.FullName, TestProjectProfiles.GeneratedFileName);
            string props = Path.Combine(scratch.FullName, PropsFileName);
            await File.WriteAllTextAsync(source, Source(candidates, usings, scratch.FullName), cancellationToken);
            foreach (ReproductionCandidate candidate in candidates)
            {
                // The compiler reads the file a #line directive names (for the debug information), so each reproduction gets one.
                await File.WriteAllTextAsync(Path.Combine(scratch.FullName, candidate.FileName), candidate.Source, cancellationToken);
            }

            List<(FixedFile File, string Copy)> copies = [];
            foreach ((FixedFile file, int index) in fixedFiles.Select((file, index) => (file, index)))
            {
                string copy = Path.Combine(scratch.FullName, $"fixed{index + 1}", Path.GetFileName(file.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                await File.WriteAllTextAsync(copy, file.Text, cancellationToken);
                copies.Add((file, copy));
            }

            await File.WriteAllTextAsync(props, Props(project, source, outputFolder, copies), cancellationToken);
            string isolation = $"-p:CustomAfterMicrosoftCommonProps={props}";
            if (await TargetAsync(project, app, isolation, root, cancellationToken) is not { } target)
            {
                return new ReproductionBuildResult(ReproductionBuildStatus.NotRun, [], []);
            }

            (string assembly, string? framework) = target;

            ProcessResult build = await ProcessRunner.RunAsync(
                "dotnet",
                ["build", project, "--nologo", "--interactive", "false", .. framework is null ? Array.Empty<string>() : ["-f", framework], isolation, .. BuildProperties],
                root,
                timeLimit,
                cancellationToken);
            if (build.ExitCode != 0)
            {
                IReadOnlyList<CompileError> errors = CompileErrors.Parse(build.Combined);
                return new ReproductionBuildResult(
                    ReproductionBuildStatus.DidNotCompile, errors.Count > 0 ? errors : [new CompileError(string.Empty, 0, 0, "build", LastLine(build.Combined))], []);
            }

            string[] filters = [.. candidates.Select(candidate => $"{Namespace}.{candidate.ClassName}.{candidate.Method}"), .. alsoRun];
            TestRunOutcome run = await TestRunners.For(app.Mode, time).RunAsync(app with { AssemblyPath = assembly }, filters, root, timeLimit, cancellationToken);
            TestRunReport? report = run.Reported ? run.Report : null;
            return report is null
                ? new ReproductionBuildResult(ReproductionBuildStatus.NotRun, [], [])
                : new ReproductionBuildResult(ReproductionBuildStatus.Ran, [], report.Results);
        }
        finally
        {
            Delete(scratch);
        }
    }

    /// <summary>The test key the runner reports a reproduction under.</summary>
    public static string Key(TestApp app, ReproductionCandidate candidate) => $"{app.AssemblyName}:{Namespace}.{candidate.ClassName}.{candidate.Method}";

    /// <summary>The using directive (its index in the list given to the build) an error in the generated file points at; null for any other error.</summary>
    public static int? UsingAt(CompileError error, int usingCount)
    {
        int index = error.Line - LinesBeforeUsings - 1;
        return error.File == TestProjectProfiles.GeneratedFileName && index >= 0 && index < usingCount ? index : null;
    }

    /// <summary>
    /// The generated file: each reproduction in its own class, its lines mapped to <c>FindingN.cs</c> in <paramref name="directory"/>
    /// so a compiler error names the reproduction it is in and the line within it.
    /// </summary>
    public static string Source(IReadOnlyList<ReproductionCandidate> candidates, IReadOnlyList<string> usings, string directory)
    {
        StringBuilder source = new();
        source.Append("// <auto-generated/>\n#nullable enable\n");
        foreach (string directive in usings)
        {
            source.Append(directive).Append('\n');
        }

        source.Append(CultureInfo.InvariantCulture, $"\nnamespace {Namespace};\n\n");
        foreach (ReproductionCandidate candidate in candidates)
        {
            source.Append(CultureInfo.InvariantCulture, $"public sealed class {candidate.ClassName}\n{{\n#line 1 \"{Path.Combine(directory, candidate.FileName).Replace('\\', '/')}\"\n");
            source.Append(candidate.Source.ReplaceLineEndings("\n")).Append("\n#line default\n}\n\n");
        }

        return source.ToString();
    }

    private static string Props(string project, string source, string outputFolder, IReadOnlyList<(FixedFile File, string Copy)> copies) => $"""
        <Project>
          <PropertyGroup>
            <OutputPath>bin/{outputFolder}/</OutputPath>
            <IntermediateOutputPath>$(BaseIntermediateOutputPath){outputFolder}/</IntermediateOutputPath>
          </PropertyGroup>
          <ItemGroup Condition="'$(MSBuildProjectFullPath)' == '{Escape(Path.GetFullPath(project))}'">
            <Compile Include="{Escape(source)}" />
          </ItemGroup>
        {string.Concat(copies.Select((copy, index) => Swap(copy.File, copy.Copy, index)))}</Project>
        """;

    /// <summary>
    /// A target that replaces one production file by its fixed copy before the compiler's inputs are settled (so the build sees
    /// the change and the next build sees it undone), in the project that compiles the file only.
    /// </summary>
    private static string Swap(FixedFile file, string copy, int index) => $"""
          <Target Name="LockflightReviewFix{index + 1}" BeforeTargets="BeforeCompile" Condition="'$(MSBuildProjectFullPath)' == '{Escape(Path.GetFullPath(file.Project))}'">
            <ItemGroup>
              <Compile Remove="@(Compile)" Condition="'%(Compile.FullPath)' == '{Escape(Path.GetFullPath(file.Path))}'" />
              <Compile Include="{Escape(copy)}" />
            </ItemGroup>
          </Target>

        """;

    /// <summary>The isolated build's test assembly and, for a multi-targeted project, the framework to build; null when MSBuild cannot say.</summary>
    private static async Task<(string Assembly, string? Framework)?> TargetAsync(string project, TestApp app, string isolation, string root, CancellationToken cancellationToken)
    {
        if (await PropertiesAsync(project, [isolation], root, cancellationToken) is not (string targetPath, string frameworks))
        {
            return null;
        }

        if (targetPath.Length > 0)
        {
            return (targetPath, null);
        }

        string built = Path.GetFileName(Path.GetDirectoryName(app.AssemblyPath)) ?? string.Empty;
        string? framework = frameworks.Split(';', StringSplitOptions.TrimEntries).Contains(built, StringComparer.OrdinalIgnoreCase) ? built : TestApps.ChooseFramework(frameworks, null);
        return framework is not null && await PropertiesAsync(project, [isolation, $"-p:TargetFramework={framework}"], root, cancellationToken) is ({ Length: > 0 } path, _)
            ? (path, framework)
            : null;
    }

    private static async Task<(string TargetPath, string Frameworks)?> PropertiesAsync(string project, string[] properties, string root, CancellationToken cancellationToken)
    {
        ProcessResult query = await ProcessRunner.RunAsync(
            "dotnet", ["msbuild", project, "-getProperty:TargetPath", "-getProperty:TargetFrameworks", .. properties], root, cancellationToken);
        int start = query.Output.IndexOf('{', StringComparison.Ordinal);
        if (query.ExitCode != 0 || start < 0)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(query.Output[start..]);
            JsonElement values = document.RootElement.GetProperty("Properties");
            return (values.GetProperty("TargetPath").GetString() ?? string.Empty, values.GetProperty("TargetFrameworks").GetString() ?? string.Empty);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A path as a literal in an MSBuild project file: MSBuild's special characters escaped, then XML's.</summary>
    private static string Escape(string path)
    {
        StringBuilder escaped = new();
        foreach (char character in path)
        {
            if (character is '%' or '$' or '@' or '\'' or ';' or '?' or '*')
            {
                escaped.Append('%').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                escaped.Append(character);
            }
        }

        return SecurityElement.Escape(escaped.ToString());
    }

    private static string LastLine(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "the build failed without output";

    private static void Delete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A compiler server still holding the file: the temporary directory is left for the system to clean.
        }
    }
}
