namespace Lockflight.Cli.Routing;

/// <summary>The ids of the signals and routing failures, as evidence and the pull request comment spell them.</summary>
internal static class SignalIds
{
    public const string SizeLines = "size.lines";
    public const string SizeFiles = "size.files";
    public const string SizeLarge = "size.large";
    public const string FileAdded = "file.added";
    public const string FileDeleted = "file.deleted";
    public const string ConfigChanged = "config.changed";
    public const string BinaryChanged = "binary.changed";
    public const string Moved = "moved";
    public const string DepsChanged = "deps.changed";
    public const string ProjectChanged = "project.changed";
    public const string ProjectPolicyProperty = "project.policyProperty";
    public const string ProjectUnreadable = "project.unreadable";
    public const string TestsConfigChanged = "tests.configChanged";
    public const string TestsRemoved = "tests.removed";
    public const string TestsChanged = "tests.changed";
    public const string TestsSkipped = "tests.skipped";
    public const string TypeAdded = "type.added";
    public const string ApiEndpointAttribute = "api.endpointAttribute";
    public const string ApiMinimalEndpoint = "api.minimalEndpoint";
    public const string ApiControllerAction = "api.controllerAction";
    public const string ApiControllerAdded = "api.controllerAdded";
    public const string ApiPageDirective = "api.pageDirective";
    public const string ApiContractFile = "api.contractFile";
    public const string ApiPublicMember = "api.publicMember";
    public const string DbMigration = "db.migration";
    public const string DbSql = "db.sql";
    public const string DbModel = "db.model";
    public const string CriticalPath = "critical.path";
    public const string CriticalPolicy = "critical.policy";
    public const string CiWorkflowChanged = "ci.workflowChanged";
    public const string StructureProjectAdded = "structure.projectAdded";
    public const string StructureProjectKindChanged = "structure.projectKindChanged";
    public const string StructureBuildWide = "structure.buildWide";
    public const string StructureSolution = "structure.solution";
    public const string StructureLayerViolation = "structure.layerViolation";
    public const string StructureTestReference = "structure.testReference";
    public const string StructureReferenceAdded = "structure.referenceAdded";
    public const string CriticalMove = "routing.criticalMove";
}
