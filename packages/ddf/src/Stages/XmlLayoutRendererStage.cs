using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Cadence.Ddf.Stages;

public class XmlLayoutRendererStage
{
    [SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods should be static", Justification = "Pipeline stage instance method")]
    public (string OdmXml, string OpenRosaXml, string FormHash) Render(DDFStudyIR studyIr)
    {
        ArgumentNullException.ThrowIfNull(studyIr);

        string rawOdm = RenderOdmXml(studyIr);
        string rawOpenRosa = RenderOpenRosaXml(studyIr);

        string odmXml = PrettyPrint(rawOdm);
        string openRosaXml = PrettyPrint(rawOpenRosa);

        string formHash = ComputeSha256Hash(openRosaXml);

        return (odmXml, openRosaXml, formHash);
    }

    private static string RenderOdmXml(DDFStudyIR study)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<ODM xmlns=\"http://www.cdisc.org/ns/odm/v1.3\" FileOID=\"ODM.{study.StudyId}\" Originator=\"Cadence DDF Compiler\">");
        sb.AppendLine($"  <Study OID=\"{study.StudyId}\">");
        sb.AppendLine("    <GlobalVariables>");
        sb.AppendLine($"      <StudyName>{EscapeXml(study.Name)}</StudyName>");
        sb.AppendLine($"      <StudyDescription>Compiled USDM protocol version {study.Version ?? "1.0"}</StudyDescription>");
        sb.AppendLine($"      <ProtocolName>{EscapeXml(study.Name)}</ProtocolName>");
        sb.AppendLine("    </GlobalVariables>");
        string mdvVersion = study.Version ?? "1.0";
        sb.AppendLine($"    <MetaDataVersion OID=\"MDV.{mdvVersion}\" Name=\"Version {mdvVersion}\">");

        if (study.Items != null && study.Items.Count > 0)
        {
            sb.AppendLine("      <ItemGroupDef OID=\"IG.CRF\" Name=\"eCRF Data Group\" Repeating=\"No\">");
            foreach (var item in study.Items)
            {
                sb.AppendLine($"        <ItemRef ItemOID=\"{item.Id}\" Mandatory=\"No\" />");
            }
            sb.AppendLine("      </ItemGroupDef>");

            foreach (var item in study.Items)
            {
                string odmDataType = item.Type switch
                {
                    "integer" => "integer",
                    "float" or "decimal" => "float",
                    "date" => "date",
                    "datetime" => "datetime",
                    "boolean" => "boolean",
                    _ => "text"
                };

                bool hasChildren = (item.Localizations != null && item.Localizations.Count > 0) ||
                                  !string.IsNullOrEmpty(item.Relevant) ||
                                  !string.IsNullOrEmpty(item.Constraint);

                if (!hasChildren)
                {
                    sb.AppendLine($"      <ItemDef OID=\"{item.Id}\" Name=\"{EscapeXml(item.Name)}\" DataType=\"{odmDataType}\" />");
                }
                else
                {
                    sb.AppendLine($"      <ItemDef OID=\"{item.Id}\" Name=\"{EscapeXml(item.Name)}\" DataType=\"{odmDataType}\">");

                    if (item.Localizations != null && item.Localizations.Count > 0)
                    {
                        sb.AppendLine("        <Description>");
                        foreach (var (langCode, locDict) in item.Localizations)
                        {
                            string? desc = null;
                            string? label = null;
                            if (locDict.TryGetValue("description", out var dObj) && dObj != null) desc = dObj.ToString();
                            if (locDict.TryGetValue("label", out var lObj) && lObj != null) label = lObj.ToString();

                            string text = desc ?? label ?? string.Empty;
                            if (!string.IsNullOrEmpty(text))
                            {
                                sb.AppendLine($"          <TranslatedText Language=\"{langCode}\">{EscapeXml(text)}</TranslatedText>");
                            }
                        }
                        sb.AppendLine("        </Description>");
                    }

                    if (!string.IsNullOrEmpty(item.Relevant))
                    {
                        sb.AppendLine($"        <Alias Context=\"relevant\" Name=\"{EscapeXml(item.Relevant)}\" />");
                    }

                    if (!string.IsNullOrEmpty(item.Constraint))
                    {
                        sb.AppendLine($"        <Alias Context=\"constraint\" Name=\"{EscapeXml(item.Constraint)}\" />");
                    }

                    sb.AppendLine("      </ItemDef>");
                }
            }
        }

        sb.AppendLine("    </MetaDataVersion>");
        sb.AppendLine("  </Study>");
        sb.AppendLine("</ODM>");
        return sb.ToString();
    }

    private static string RenderOpenRosaXml(DDFStudyIR study)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<h:html xmlns=\"http://www.w3.org/2002/xforms\" xmlns:h=\"http://www.w3.org/1999/xhtml\" xmlns:ev=\"http://www.w3.org/2001/xml-events\" xmlns:or=\"http://openrosa.org/xforms\" xmlns:orx=\"http://openrosa.org/xforms\" xmlns:odk=\"http://www.opendatakit.org/xforms\" xmlns:jr=\"http://openrosa.org/javarosa\">");
        sb.AppendLine("  <h:head>");
        sb.AppendLine($"    <h:title>{EscapeXml(study.Name)}</h:title>");
        sb.AppendLine("    <model>");
        sb.AppendLine("      <instance>");
        sb.AppendLine($"        <data id=\"{study.StudyId}\">");

        if (study.Items != null)
        {
            foreach (var item in study.Items)
            {
                sb.AppendLine($"          <{GetXmlTagName(item.Name)} />");
            }
        }

        sb.AppendLine("        </data>");
        sb.AppendLine("      </instance>");

        if (study.Items != null)
        {
            foreach (var item in study.Items)
            {
                string relAttr = !string.IsNullOrEmpty(item.Relevant) ? $" relevant=\"{EscapeXml(item.Relevant)}\"" : "";
                string constAttr = !string.IsNullOrEmpty(item.Constraint) ? $" constraint=\"{EscapeXml(item.Constraint)}\"" : "";
                string msgAttr = !string.IsNullOrEmpty(item.ConstraintMessage) ? $" jr:constraintMsg=\"{EscapeXml(item.ConstraintMessage)}\"" : "";

                sb.AppendLine($"      <bind nodeset=\"/data/{GetXmlTagName(item.Name)}\" type=\"{item.Type}\"{relAttr}{constAttr}{msgAttr} />");
            }
        }

        sb.AppendLine("    </model>");
        sb.AppendLine("  </h:head>");
        sb.AppendLine("  <h:body>");

        if (study.Items != null)
        {
            foreach (var item in study.Items)
            {
                string appAttr = !string.IsNullOrEmpty(item.Appearance) ? $" appearance=\"{EscapeXml(item.Appearance)}\"" : "";
                sb.AppendLine($"    <input ref=\"/data/{GetXmlTagName(item.Name)}\"{appAttr}>");

                if (item.Localizations != null && item.Localizations.Count > 0)
                {
                    foreach (var (langCode, locDict) in item.Localizations)
                    {
                        if (locDict.TryGetValue("label", out var lObj) && lObj != null && !string.IsNullOrEmpty(lObj.ToString()))
                        {
                            sb.AppendLine($"      <label xml:lang=\"{langCode}\">{EscapeXml(lObj.ToString()!)}</label>");
                        }
                    }

                    foreach (var (langCode, locDict) in item.Localizations)
                    {
                        if (locDict.TryGetValue("hint", out var hObj) && hObj != null && !string.IsNullOrEmpty(hObj.ToString()))
                        {
                            sb.AppendLine($"      <hint xml:lang=\"{langCode}\">{EscapeXml(hObj.ToString()!)}</hint>");
                        }
                    }
                }
                else
                {
                    sb.AppendLine($"      <label>{EscapeXml(item.Name)}</label>");
                }

                sb.AppendLine("    </input>");
            }
        }

        sb.AppendLine("  </h:body>");
        sb.AppendLine("</h:html>");
        return sb.ToString();
    }

    private static string GetXmlTagName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "item";
        return rawName.Trim().Replace(" ", "_");
    }

    private static string PrettyPrint(string xmlString)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlString);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false)
            };

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, settings))
            {
                doc.Save(writer);
            }
            return sb.ToString();
        }
        catch
        {
            return xmlString;
        }
    }

    private static string ComputeSha256Hash(string rawData)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string EscapeXml(string unescaped)
    {
        if (string.IsNullOrEmpty(unescaped)) return string.Empty;
        return unescaped
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
