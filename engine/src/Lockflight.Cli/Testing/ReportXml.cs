using System.Xml;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Loads a report the pull request's test host wrote (a TRX, an xUnit XML report): no DTD and no resolver, so an entity in it
/// can neither reach a file nor expand without bound. A report with a DTD is an <see cref="XmlException"/>, as a malformed one is.
/// </summary>
internal static class ReportXml
{
    public static XmlDocument Load(string xml)
    {
        XmlDocument document = new() { XmlResolver = null };
        using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        document.Load(reader);
        return document;
    }
}
