using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The database: a migration path changed (<c>db.migration</c>: <c>**/Migrations/**</c> or <c>routing.dbPaths</c>, generated files
/// included), a SQL file outside test projects and <c>routing.nonProduction</c> (<c>db.sql</c>), and an EF Core or FluentMigrator
/// model statement added, removed or changed (<c>db.model</c>, statement multisets per project), or a mapping attribute
/// (<c>[Table]</c>, <c>[Column]</c>, <c>[MaxLength]</c>, <c>[Key]</c>, …) added, removed or changed on a declaration. Each raises to <c>feature</c>. An entity
/// gaining a property is invisible here: the <c>ef.pendingModelChanges</c> gate step is the interlock for that.
/// </summary>
internal sealed partial class DatabaseSignals : IChangeSignalSource
{
    private const string MigrationsGlob = "**/Migrations/**";
    private const string SqlSuffix = ".sql";

    private static readonly FileRole[] ModelRoles = [FileRole.Production, FileRole.NonProduction];

    public IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy)
    {
        foreach (RoutedFile file in analysis.Files)
        {
            if (new[] { MigrationsGlob }.Concat(policy.Settings.DbPaths).FirstOrDefault(glob => PathRules.Matches(glob, file.Path)) is string glob)
            {
                yield return Signal(SignalIds.DbMigration, file.Path, $"database migration path ({glob})");
            }

            if (file.Path.EndsWith(SqlSuffix, StringComparison.OrdinalIgnoreCase) && file.Role is not (FileRole.Test or FileRole.NonProduction) && file.Move?.Kind != MoveKind.Exact)
            {
                yield return Signal(SignalIds.DbSql, file.Path, "SQL file changed");
            }
        }

        if (StatementSets.FirstDifference(analysis, ModelRoles, ModelLine()) is string model)
        {
            yield return Signal(SignalIds.DbModel, model, "a database model line added, removed or changed");
        }

        foreach (ProjectDeclarations project in DeclarationSets.Of(analysis, ModelRoles))
        {
            if (project.Added.Concat(project.Removed).FirstOrDefault(placed => placed.Declaration.Attributes.Any(attribute => MappingAttribute().IsMatch(attribute))) is PlacedDeclaration mapped)
            {
                yield return Signal(SignalIds.DbModel, mapped.Where, $"a database mapping attribute on {mapped.Declaration.Name} added, removed or changed");
            }
        }
    }

    [GeneratedRegex(@"[\[,]\s*(?:[\w.]+\.)?(?:Table|Column|Key|ForeignKey|InverseProperty|Index|PrimaryKey|MaxLength|StringLength|Required|NotMapped|Owned|Precision|Unicode|DatabaseGenerated|ConcurrencyCheck|Timestamp|Keyless|Comment|DeleteBehavior|EntityTypeConfiguration)(?:Attribute)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex MappingAttribute();

    private static ChangeSignal Signal(string id, string where, string detail) => new(id, ChangeClass.Feature, SignalStrength.Heuristic, where, detail);

    [GeneratedRegex(@"\bbuilder\s*\.\s*(?:Property|Has\w*|With\w*|Owns\w*|Is\w*|Ignore|To\w+|Navigation|ComplexProperty|Entity)\b|\bDbSet\s*<|\bOnModelCreating\b|\bIEntityTypeConfiguration\s*<|\bmodelBuilder\s*\.|\.ToTable\s*\(|\.HasColumnName\s*\(|\.HasIndex\s*\(|\.HasKey\s*\(|:\s*(?:AutoReversingMigration|ForwardOnlyMigration|Migration)\b|\[\s*Migration\s*\(|\b(?:Create|Alter|Delete)\.Table\s*\(|\bCreate\.Column\s*\(|\bExecute\.Sql\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ModelLine();
}
