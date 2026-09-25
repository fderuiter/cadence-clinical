using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cadence.ToolingCore.OpenApi;

public record ServiceMetadata(
    string Title,
    string Domain,
    int Port,
    string Prefix,
    string Desc,
    string Persistence
);

public static class OpenApiToolingService
{
    public static readonly IReadOnlyDictionary<string, ServiceMetadata> ServiceCatalog = new Dictionary<string, ServiceMetadata>(StringComparer.OrdinalIgnoreCase)
    {
        ["gateway"] = new("Central API Gateway & Auth Proxy", "Platform & Gateway Infrastructure", 8000, "/api/v1", "Unified reverse proxy handling Keycloak OIDC authentication, HMAC signature propagation, step-up tokens, rate limiting, and OpenAPI aggregation.", "Stateless / Keycloak OIDC"),
        ["designer"] = new("Study Designer & MDR (USDM)", "Protocol & Study Design", 8001, "/api/v1/designer", "Clinical study metadata authoring, CDISC USDM v3.0/v4.0 graph modeling, SoA matrix generation, and Biomedical Concept libraries.", "Neo4j Graph Database"),
        ["execution"] = new("EDC Execution Engine", "Clinical Operations & Execution", 8002, "/api/v1/execution", "Runtime Electronic Data Capture (EDC), dynamic subject eCRF schema projection, real-time edit checks, medical coding, lab toxicity, and Dataset-JSON exports.", "PostgreSQL Relational DB"),
        ["etmf"] = new("Electronic Trial Master File (eTMF)", "Safety, Quality & Governance", 8004, "/api/v1/etmf", "DIA TMF Reference Model v3.2.0 document classification, Expected Document Lists (EDLs), completeness scoring, and automated QC transitions.", "PostgreSQL / SQLite"),
        ["ctms"] = new("Clinical Trial Management System (CTMS)", "Clinical Operations & Execution", 8005, "/api/v1/ctms", "Site operational tracking, milestone governance, CRA monitoring visit reports (MVR), 6-facet site greenlight, and investigator grant budgets.", "PostgreSQL / SQLite"),
        ["quality"] = new("Quality & CAPA Management", "Safety, Quality & Governance", 8006, "/api/v1/quality", "Protocol deviation tracking, Root Cause Analysis (RCA), CAPA workflows, Centralized Statistical Monitoring (CSM forensics), and pHash image fraud detection.", "PostgreSQL / SQLite"),
        ["interop"] = new("FHIR & eSource Gateway", "Patient Engagement & Interoperability", 8007, "/api/v1/interop", "SMART on FHIR EHR integration, eSource reconciliation drawers, and Open mHealth continuous biometric sensor streaming adapters.", "PostgreSQL / SQLite"),
        ["org"] = new("Organization Directory", "Clinical Operations & Execution", 8008, "/api/v1/org", "Global directory of Sponsor/CRO organizations, clinical trial sites, staff credentials, and Principal Investigator Delegation of Authority (DOA) logs.", "PostgreSQL / SQLite"),
        ["safety"] = new("Safety & Pharmacovigilance Gateway", "Safety, Quality & Governance", 8009, "/api/v1/safety", "ICH E2B(R3) Individual Case Safety Reports (ICSR) XML compilation, NCI-CTCAE v5.0 toxicity grading, and B2B AS2 S/MIME regulatory EDI gateway.", "PostgreSQL / SQLite"),
        ["notifications"] = new("Multi-Channel Notifications Dispatcher", "Patient Engagement & Interoperability", 8010, "/api/v1/notifications", "Automated patient compliance reminders and high-priority GxP alerts across SMTP Email (Jinja2), SMS, HMAC Webhooks, and In-App inbox.", "PostgreSQL / SQLite"),
        ["knowledge"] = new("Knowledge & SOP Engine", "Protocol & Study Design", 8011, "/api/v1/knowledge", "Contextual SOP delivery, dynamic help drawer routing, role-specific clinical guidance, and controlled document versioning.", "PostgreSQL / SQLite"),
        ["tickets"] = new("Clinical Issue Hub & Query Escalation", "Clinical Operations & Execution", 8012, "/api/v1/tickets", "Clinical query escalation, data management issue tracking, dual-target SLA timers, and controlled reopening windows.", "PostgreSQL / SQLite"),
        ["ai_gateway"] = new("Clinical AI Gateway", "Protocol & Study Design", 8013, "/api/v1/ai", "Model tiering (Tier 1–3), PII/PHI de-identification air-gap proxy, medical coding embeddings, and 21 CFR Part 11 dual-attribution AI ledgers.", "Ephemeral / REST"),
        ["payments"] = new("Site Payments & Participant Wallet", "Clinical Operations & Execution", 8014, "/api/v1/payments", "Investigator grant contracts, procedure-fee rules, multi-payee splits, closeout holdbacks, participant expense OCR, and self-billing invoices.", "PostgreSQL / SQLite"),
        ["imaging"] = new("Blinded DICOM Adjudication", "Safety, Quality & Governance", 8015, "/api/v1/imaging", "Zero-footprint WebGL Multi-Planar Reconstruction (MPR), blinded dual-reader RECIST 1.1 oncology evaluation, and 20% discordance triggers.", "PostgreSQL / SQLite"),
        ["eisf"] = new("Electronic Investigator Site Files (eISF)", "Safety, Quality & Governance", 8016, "/api/v1/eisf", "Site-scoped regulatory binder management, DIA eISF taxonomy inheritance, and PI certification audit manifests.", "PostgreSQL / SQLite"),
        ["econsent"] = new("Electronic Consent (eConsent)", "Patient Engagement & Interoperability", 8017, "/api/v1/econsent", "Interactive informed consent authoring, dynamic amendment re-consent gating, readability metrics, and digital signature sealing.", "PostgreSQL / SQLite"),
        ["fileshare"] = new("Object Storage & FileShare Gateway", "Safety, Quality & Governance", 8018, "/api/v1/fileshare", "StoragePort abstraction over MinIO/S3, presigned transfer URLs, cryptographic guest links, and 21 CFR Part 11 document envelopes.", "MinIO / S3 & SQL Envelope"),
        ["compliance"] = new("Compliance & Qualification Engine", "Platform & Gateway Infrastructure", 8019, "/api/v1/compliance", "Centralized regulatory compliance verification, IQ/OQ/PQ execution suites, and automated 21 CFR Part 11 audit trail assertions.", "Pytest + Audit Ledgers")
    };

    public static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    public static int ValidateOpenApiDrift(string repoRoot)
    {
        Console.WriteLine("--- Running OpenAPI Specification & Documentation Drift Sentinel ---");

        Console.WriteLine("\n[GATE 1/2] Checking docs/SDLC/03_API_Integration_Specification.md...");
        int specExit = SyncOpenApiSpec(repoRoot, validateOnly: true);

        Console.WriteLine("\n[GATE 2/2] Checking docs/api/*.md documentation parity...");
        int docsExit = GenerateApiDocs(repoRoot, validateOnly: true);

        if (specExit != 0 || docsExit != 0)
        {
            Console.Error.WriteLine("\n" + new string('=', 80));
            Console.Error.WriteLine("[ERROR] OpenAPI Specification Drift Detected!");
            Console.Error.WriteLine(new string('=', 80));
            Console.Error.WriteLine("Published API documentation has drifted from backend routes/models.");
            Console.Error.WriteLine("To automatically resynchronize specifications and documentation, run:");
            Console.Error.WriteLine("    make fix");
            Console.Error.WriteLine("  or");
            Console.Error.WriteLine("    cadence fix");
            Console.Error.WriteLine(new string('=', 80) + "\n");
            return 1;
        }

        Console.WriteLine("\n[SUCCESS] OpenAPI Specification Drift Sentinel passed cleanly.");
        return 0;
    }

    public static int SyncOpenApiSpec(string repoRoot, bool validateOnly = false)
    {
        if (!validateOnly)
        {
            Console.WriteLine("Generating and merging OpenAPI specifications statically...");
        }

        string openapiDir = Path.Combine(repoRoot, "docs", "openapi");
        string cdiscFile = Path.Combine(openapiDir, "cdisc_openapi.json");

        JsonObject? aggregatedSpec = null;
        if (File.Exists(cdiscFile))
        {
            try
            {
                string text = File.ReadAllText(cdiscFile);
                aggregatedSpec = JsonNode.Parse(text) as JsonObject;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to load cdisc_openapi.json: {ex.Message}");
            }
        }

        if (aggregatedSpec == null)
        {
            var aggregatedPaths = new JsonObject();
            var aggregatedComponents = new JsonObject();

            if (Directory.Exists(openapiDir))
            {
                var files = Directory.GetFiles(openapiDir, "*_openapi.json")
                    .Where(f => !Path.GetFileName(f).Equals("cdisc_openapi.json", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f);

                foreach (var file in files)
                {
                    try
                    {
                        var doc = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                        if (doc?["paths"] is JsonObject paths)
                        {
                            foreach (var p in paths)
                            {
                                aggregatedPaths[p.Key] = p.Value?.DeepClone();
                            }
                        }
                        if (doc?["components"] is JsonObject comps)
                        {
                            foreach (var compKv in comps)
                            {
                                string compType = compKv.Key;
                                if (!aggregatedComponents.ContainsKey(compType))
                                {
                                    aggregatedComponents[compType] = new JsonObject();
                                }
                                if (compKv.Value is JsonObject compVals && aggregatedComponents[compType] is JsonObject targetGroup)
                                {
                                    foreach (var cItem in compVals)
                                    {
                                        targetGroup[cItem.Key] = cItem.Value?.DeepClone();
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Warning: Failed to parse {file}: {ex.Message}");
                    }
                }
            }

            aggregatedSpec = new JsonObject
            {
                ["paths"] = aggregatedPaths,
                ["components"] = aggregatedComponents
            };
        }

        string brandDomain = Environment.GetEnvironmentVariable("BRAND_DOMAIN") ?? "ccrsoft.com";

        var mergedSpec = new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "Cadence Clinical Unified Gateway API",
                ["description"] = "Unified microservices API contract for Cadence Clinical Platform.\nEnforces OIDC/Keycloak authentication, RFC 7807 problem details, and ISO 14155:2020 regulatory compliance.",
                ["version"] = "1.0.0-PROD"
            },
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = $"https://api.{brandDomain}/api/v1",
                    ["description"] = "Production API Gateway"
                },
                new JsonObject
                {
                    ["url"] = "http://localhost:8000/api/v1",
                    ["description"] = "Local Dev Gateway Proxy"
                }
            },
            ["paths"] = aggregatedSpec["paths"]?.DeepClone() ?? new JsonObject(),
            ["components"] = aggregatedSpec["components"]?.DeepClone() ?? new JsonObject()
        };

        string yamlContent = JsonNodeToYaml(mergedSpec);

        string markdownPath = Path.Combine(repoRoot, "docs", "SDLC", "03_API_Integration_Specification.md");
        if (!File.Exists(markdownPath))
        {
            Console.Error.WriteLine($"Error: Spec file not found at {markdownPath}");
            return 1;
        }

        string content = File.ReadAllText(markdownPath, Encoding.UTF8);
        string secTitle = "## 7. Complete OpenAPI 3.0 Contract Specification";
        int idx = content.IndexOf(secTitle, StringComparison.Ordinal);
        if (idx == -1)
        {
            Console.Error.WriteLine($"Error: Could not find section title '{secTitle}'");
            return 1;
        }

        string beforeSec = content[..idx];
        string secContentAndAfter = content[idx..];

        string startFence = "```yaml";
        int startIdx = secContentAndAfter.IndexOf(startFence, StringComparison.Ordinal);
        if (startIdx == -1)
        {
            Console.Error.WriteLine("Error: Could not find start of ```yaml block in Section 7");
            return 1;
        }

        int endIdx = secContentAndAfter.IndexOf("```", startIdx + startFence.Length, StringComparison.Ordinal);
        if (endIdx == -1)
        {
            Console.Error.WriteLine("Error: Could not find end of ```yaml block in Section 7");
            return 1;
        }

        string newSecContentAndAfter = secContentAndAfter[..(startIdx + startFence.Length)]
            + "\n"
            + yamlContent.Trim()
            + "\n"
            + secContentAndAfter[endIdx..];

        string finalContent = beforeSec + newSecContentAndAfter;

        if (validateOnly)
        {
            if (!string.Equals(content, finalContent, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("[ERROR] OpenAPI specification drift detected in docs/SDLC/03_API_Integration_Specification.md!\nPlease run 'cadence fix' to synchronize.");
                return 1;
            }
            Console.WriteLine("[SUCCESS] OpenAPI specification in docs/SDLC/03_API_Integration_Specification.md is up to date.");
            return 0;
        }

        File.WriteAllText(markdownPath, finalContent, Encoding.UTF8);
        Console.WriteLine("Successfully updated OpenAPI Specification in docs/SDLC/03_API_Integration_Specification.md!");
        return 0;
    }

    public static int GenerateApiDocs(string repoRoot, bool validateOnly = false)
    {
        string apiDocsDir = Path.Combine(repoRoot, "docs", "api");
        Directory.CreateDirectory(apiDocsDir);

        if (!validateOnly)
        {
            Console.WriteLine("--- Starting Programmatic OpenAPI Documentation Generator ---");
        }
        else
        {
            Console.WriteLine("--- Validating OpenAPI API Documentation Parity ---");
        }

        var driftedFiles = new List<string>();
        var availableServices = new List<(string Name, ServiceMetadata Meta, JsonObject Spec)>();

        foreach (var (serviceName, meta) in ServiceCatalog)
        {
            var spec = LoadServiceSpec(repoRoot, serviceName, meta);
            string mdContent = GenerateServiceMarkdown(meta, spec);
            string targetPath = Path.Combine(apiDocsDir, $"{serviceName}.md");

            if (validateOnly)
            {
                if (!File.Exists(targetPath) || File.ReadAllText(targetPath, Encoding.UTF8) != mdContent)
                {
                    driftedFiles.Add($"docs/api/{serviceName}.md");
                }
            }
            else
            {
                File.WriteAllText(targetPath, mdContent, Encoding.UTF8);
                int routeCount = (spec["paths"] as JsonObject)?.Count ?? 0;
                Console.WriteLine($"[SUCCESS] Generated API documentation: docs/api/{serviceName}.md ({routeCount} routes)");
            }

            availableServices.Add((serviceName, meta, spec));
        }

        string indexContent = GenerateIndexMarkdown(availableServices);
        string indexPath = Path.Combine(apiDocsDir, "index.md");

        if (validateOnly)
        {
            if (!File.Exists(indexPath) || File.ReadAllText(indexPath, Encoding.UTF8) != indexContent)
            {
                driftedFiles.Add("docs/api/index.md");
            }

            if (driftedFiles.Count > 0)
            {
                Console.Error.WriteLine($"[ERROR] API documentation drift detected in {driftedFiles.Count} file(s):\n" +
                    string.Join("\n", driftedFiles.Select(f => $"  - {f}")) +
                    "\nPlease run 'cadence fix' to synchronize.");
                return 1;
            }

            Console.WriteLine($"[SUCCESS] All API documentation files in docs/api/ ({availableServices.Count} services) are up to date.");
            return 0;
        }

        File.WriteAllText(indexPath, indexContent, Encoding.UTF8);
        Console.WriteLine($"[SUCCESS] Generated Master API Index: docs/api/index.md ({availableServices.Count} microservices cataloged)");
        return 0;
    }

    private static JsonObject LoadServiceSpec(string repoRoot, string serviceName, ServiceMetadata meta)
    {
        string openapiDir = Path.Combine(repoRoot, "docs", "openapi");
        if (serviceName == "gateway")
        {
            foreach (var fn in new[] { "gateway_openapi.json", "cdisc_openapi.json" })
            {
                string fp = Path.Combine(openapiDir, fn);
                if (File.Exists(fp))
                {
                    try
                    {
                        if (JsonNode.Parse(File.ReadAllText(fp)) is JsonObject obj) return obj;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[DEBUG] Exception parsing {fn}: {ex.Message}");
                    }
                }
            }
        }

        string file = Path.Combine(openapiDir, $"{serviceName}_openapi.json");
        if (File.Exists(file))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject obj) return obj;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DEBUG] Exception parsing {serviceName}_openapi.json: {ex.Message}");
            }
        }

        // Fallback health spec
        return new JsonObject
        {
            ["paths"] = new JsonObject
            {
                [$"{meta.Prefix}/health"] = new JsonObject
                {
                    ["get"] = new JsonObject
                    {
                        ["summary"] = $"{meta.Title} Health Check",
                        ["description"] = $"Verifies live operational status and database connectivity for {meta.Title}.",
                        ["responses"] = new JsonObject
                        {
                            ["200"] = new JsonObject
                            {
                                ["description"] = "Service is healthy and operational"
                            }
                        }
                    }
                }
            },
            ["components"] = new JsonObject()
        };
    }

    private static string GenerateServiceMarkdown(ServiceMetadata meta, JsonObject spec)
    {
        string title = meta.Title;
        int port = meta.Port;
        string prefix = meta.Prefix;
        string desc = meta.Desc;
        string persistence = meta.Persistence;
        string domain = meta.Domain;

        var pathsNode = spec["paths"] as JsonObject ?? new JsonObject();
        var componentsSchemasNode = (spec["components"] as JsonObject)?["schemas"] as JsonObject ?? new JsonObject();

        var lines = new List<string>
        {
            $"# {title}",
            "",
            $"> **Functional Domain:** `{domain}`  ",
            $"> **Service Host Port:** `localhost:{port}`  ",
            $"> **Gateway Proxy Prefix:** `{prefix}`  ",
            $"> **Persistence Backend:** `{persistence}`  ",
            "",
            "## Overview",
            "",
            desc,
            "",
            "---",
            "",
            "## Endpoints Summary",
            "",
            "| Method | Path | Summary | GxP Auth & Signatures |",
            "| :--- | :--- | :--- | :--- |"
        };

        var pathsSorted = pathsNode.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        foreach (var pathKv in pathsSorted)
        {
            string path = pathKv.Key;
            if (pathKv.Value is not JsonObject ops) continue;

            foreach (var opKv in ops)
            {
                string method = opKv.Key.ToUpperInvariant();
                if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE"))
                    continue;

                if (opKv.Value is not JsonObject op) continue;

                string summary = op["summary"]?.GetValue<string>() ?? "No summary";
                bool isMutating = method is "POST" or "PUT" or "PATCH" or "DELETE";
                string gxpBadge = "👀 Read-Only";
                if (isMutating)
                {
                    gxpBadge = path.Contains("sign", StringComparison.OrdinalIgnoreCase)
                        ? "🔒 Step-Up Sig Token"
                        : "✍️ Audited Write";
                }

                string methodBadge = $"`{method}`";
                string anchor = $"{opKv.Key.ToLowerInvariant()}-{path.Trim('/').Replace('/', '-').Replace("{", "").Replace("}", "")}";
                string pathLink = $"[{path}](#{anchor})";
                lines.Add($"| {methodBadge} | {pathLink} | {summary} | {gxpBadge} |");
            }
        }

        lines.Add("");
        lines.Add("---");
        lines.Add("");
        lines.Add("## Granular Endpoint Specifications");
        lines.Add("");

        foreach (var pathKv in pathsSorted)
        {
            string path = pathKv.Key;
            if (pathKv.Value is not JsonObject ops) continue;

            foreach (var opKv in ops)
            {
                string method = opKv.Key.ToUpperInvariant();
                if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE"))
                    continue;

                if (opKv.Value is not JsonObject op) continue;

                string opId = op["operationId"]?.GetValue<string>() ?? $"{opKv.Key.ToLowerInvariant()}_{path}";
                string summary = op["summary"]?.GetValue<string>() ?? opId;
                string description = op["description"]?.GetValue<string>() ?? "No detailed description provided.";
                bool isMutating = method is "POST" or "PUT" or "PATCH" or "DELETE";

                string sigRequirement = "Not required (read-only query)";
                if (isMutating)
                {
                    sigRequirement = path.Contains("sign", StringComparison.OrdinalIgnoreCase)
                        ? "Mandatory 60s `sig_token` required for Part 11 sign-off"
                        : "Required if mutating locked records";
                }

                lines.Add($"### `{method}` `{path}`");
                lines.Add("");
                lines.Add($"**Summary:** {summary}  ");
                lines.Add($"**Operation ID:** `{opId}`  ");
                lines.Add("");
                lines.Add("**Clinical & GxP Rationale:**  ");
                lines.Add($"{description}");
                lines.Add("");
                lines.Add("#### Security & Access Control (21 CFR Part 11)");
                lines.Add("");
                lines.Add("- **Authentication:** OIDC Bearer Token (Keycloak)");
                lines.Add("- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)");
                lines.Add($"- **Step-Up Electronic Signature:** {sigRequirement}");
                lines.Add("");

                // Parameters
                if (op["parameters"] is JsonArray paramsArr && paramsArr.Count > 0)
                {
                    lines.Add("#### Request Parameters");
                    lines.Add("");
                    lines.Add("| Name | In | Type | Required | Description |");
                    lines.Add("| :--- | :--- | :--- | :--- | :--- |");

                    foreach (var pNode in paramsArr)
                    {
                        if (pNode is not JsonObject p) continue;
                        string pName = p["name"]?.GetValue<string>() ?? "";
                        string pIn = p["in"]?.GetValue<string>() ?? "query";
                        bool pReqBool = p["required"]?.GetValue<bool>() ?? false;
                        string pReq = pReqBool ? "✔ Yes" : "No";
                        string pSchema = FormatSchemaType(p["schema"] as JsonObject);
                        string pDesc = p["description"]?.GetValue<string>() ?? "—";
                        lines.Add($"| `{pName}` | `{pIn}` | {pSchema} | {pReq} | {pDesc} |");
                    }
                    lines.Add("");
                }

                // Request Body
                if (op["requestBody"] is JsonObject reqBody)
                {
                    var content = reqBody["content"] as JsonObject;
                    var jsonContent = content?["application/json"] as JsonObject;
                    var bodySchema = jsonContent?["schema"] as JsonObject;
                    if (bodySchema != null)
                    {
                        lines.Add("#### Request Payload Schema (`application/json`)");
                        lines.Add("");
                        lines.Add($"**Schema Type:** {FormatSchemaType(bodySchema)}");
                        lines.Add("");

                        if (bodySchema["$ref"]?.GetValue<string>() is string refStr)
                        {
                            var parts = refStr.Split('/');
                            string refName = parts[parts.Length - 1];
                            if (componentsSchemasNode[refName] is JsonObject targetSchema && targetSchema["properties"] is JsonObject props)
                            {
                                lines.Add("| Field | Type | Required | Description |");
                                lines.Add("| :--- | :--- | :--- | :--- |");

                                var reqPropsList = targetSchema["required"] is JsonArray reqArr
                                    ? reqArr.Select(x => x?.GetValue<string>()).Where(x => x != null).ToList()
                                    : new List<string?>();

                                foreach (var propKv in props)
                                {
                                    string fieldName = propKv.Key;
                                    var fieldDef = propKv.Value as JsonObject;
                                    string fType = FormatSchemaType(fieldDef);
                                    string fReq = reqPropsList.Contains(fieldName) ? "✔ Yes" : "No";
                                    string fDesc = fieldDef?["description"]?.GetValue<string>() ?? "—";
                                    lines.Add($"| `{fieldName}` | {fType} | {fReq} | {fDesc} |");
                                }
                                lines.Add("");
                            }
                        }
                    }
                }

                // Responses
                if (op["responses"] is JsonObject responses)
                {
                    lines.Add("#### Response Codes & Schemas");
                    lines.Add("");
                    lines.Add("| HTTP Status | Description | Response Schema |");
                    lines.Add("| :--- | :--- | :--- |");

                    foreach (var respKv in responses)
                    {
                        string code = respKv.Key;
                        var resp = respKv.Value as JsonObject;
                        string rDesc = resp?["description"]?.GetValue<string>() ?? "Standard response";
                        var rContent = (resp?["content"] as JsonObject)?["application/json"] as JsonObject;
                        string rSchema = FormatSchemaType(rContent?["schema"] as JsonObject);
                        lines.Add($"| `{code}` | {rDesc} | {rSchema} |");
                    }
                    lines.Add("");
                }

                // Client Code Examples
                string bodySnippet = isMutating ? "  body: JSON.stringify(payload),\n});" : "});";
                string pythonJsonSnippet = isMutating ? "        json=payload,\n    )" : "    )";

                lines.Add("#### Client Code Examples");
                lines.Add("");
                lines.Add("::: code-group");
                lines.Add("```typescript [TypeScript (Fetch)]");
                lines.Add($"const response = await fetch('http://localhost:8000{prefix}{path}', {{");
                lines.Add($"  method: '{method}',");
                lines.Add("  headers: {");
                lines.Add("    'Authorization': `Bearer ${userToken}`,");
                lines.Add("    'Content-Type': 'application/json',");
                lines.Add("    'X-Change-Reason': 'Clinical routine data capture',");
                lines.Add("  },");
                lines.Add(bodySnippet);
                lines.Add("const data = await response.json();");
                lines.Add("```");
                lines.Add("");
                lines.Add("```python [Python (HTTPX)]");
                lines.Add("import httpx");
                lines.Add("");
                lines.Add("async with httpx.AsyncClient(base_url='http://localhost:8000') as client:");
                lines.Add($"    response = await client.{opKv.Key.ToLowerInvariant()}(");
                lines.Add($"        '{prefix}{path}',");
                lines.Add("        headers={");
                lines.Add("            'Authorization': f'Bearer {user_token}',");
                lines.Add("            'X-Change-Reason': 'Clinical routine data capture',");
                lines.Add("        },");
                lines.Add(pythonJsonSnippet);
                lines.Add("    data = response.json()");
                lines.Add("```");
                lines.Add("");
                lines.Add("```bash [cURL]");
                lines.Add($"curl -X {method} 'http://localhost:8000{prefix}{path}' \\");
                lines.Add("  -H 'Authorization: Bearer <TOKEN>' \\");
                lines.Add("  -H 'Content-Type: application/json' \\");
                lines.Add("  -H 'X-Change-Reason: Routine clinical mutation'");
                lines.Add("```");
                lines.Add(":::");
                lines.Add("");
                lines.Add("---");
                lines.Add("");
            }
        }

        return string.Join("\n", lines);
    }

    private static string FormatSchemaType(JsonObject? schema)
    {
        if (schema == null) return "`any`";
        if (schema.ContainsKey("$ref") && schema["$ref"]?.GetValue<string>() is string refVal)
        {
            var parts = refVal.Split('/');
            string refName = parts[parts.Length - 1];
            return $"`{refName}`";
        }
        if (schema.ContainsKey("type") && schema["type"]?.GetValue<string>() is string t)
        {
            if (t == "array")
            {
                var items = schema["items"] as JsonObject;
                string itemType = FormatSchemaType(items).Replace("`", "");
                return $"`Array<{itemType}>`";
            }
            return $"`{t}`";
        }
        if (schema.ContainsKey("anyOf") && schema["anyOf"] is JsonArray anyOfArr)
        {
            var types = anyOfArr.Select(s => FormatSchemaType(s as JsonObject));
            return string.Join(" | ", types);
        }
        return "`object`";
    }

    private static string GenerateIndexMarkdown(List<(string Name, ServiceMetadata Meta, JsonObject Spec)> services)
    {
        var lines = new List<string>
        {
            "# Cadence Clinical REST API Reference",
            "",
            "> **Unified Microservice Architecture & GxP Endpoint Contract.**  ",
            "> _All endpoints exposed through the central API Gateway (`localhost:8000/api/v1/*`) with Keycloak OIDC authentication and HMAC signature verification._",
            "",
            "---",
            "",
            "## 🏛️ Architectural Overview & Gateway Topology",
            "",
            "The Cadence Clinical platform routes all client requests through a centralized reverse proxy (`apps/gateway`). Incoming requests undergo:",
            "",
            "1. **Keycloak OIDC JWT Token Verification**: Validates user credentials, roles (`sponsor_designer`, `site_crc`, `cra_monitor`, etc.), and tenant/site scoping.",
            "2. **Gateway Signature Injection**: Automatically generates and appends canonical `X-Gateway-Signature` (HMAC-SHA256) headers before dispatching to downstream services.",
            "3. **21 CFR Part 11 Electronic Signature Step-Up**: Mutating operations (e.g. batch approvals, subject randomization, unblinding) require a short-lived (60-second) `sig_token` obtained via TOTP + password re-authentication.",
            "4. **Standardized RFC 7807 Error Responses**: All errors emit RFC 7807 `application/problem+json` envelopes with canonical type URIs, status codes, and detail strings.",
            "",
            "---",
            "",
            "## 📁 Layered Microservice API Catalog",
            ""
        };

        var domains = new Dictionary<string, List<(string Name, ServiceMetadata Meta, int EndpointCount)>>();
        foreach (var (name, meta, spec) in services)
        {
            string d = meta.Domain;
            if (!domains.TryGetValue(d, out var list))
            {
                list = [];
                domains[d] = list;
            }

            int count = 0;
            if (spec["paths"] is JsonObject paths)
            {
                foreach (var pKv in paths)
                {
                    if (pKv.Value is JsonObject ops)
                    {
                        count += ops.Count;
                    }
                }
            }
            list.Add((name, meta, count));
        }

        foreach (var (domainName, sList) in domains)
        {
            lines.Add($"### {domainName}");
            lines.Add("");
            lines.Add("| Microservice | Base Route | Port | Endpoints | Persistence | Documentation |");
            lines.Add("| :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var (name, meta, count) in sList)
            {
                lines.Add($"| **{meta.Title}** | `{meta.Prefix}` | `{meta.Port}` | `{count}` | `{meta.Persistence}` | [View Reference](./{name}.md) |");
            }
            lines.Add("");
        }

        lines.AddRange(new[]
        {
            "---",
            "",
            "## 🔒 Authentication & Standard Gateway Headers",
            "",
            "All API requests dispatched to the Gateway must include standard Bearer authorization headers:",
            "",
            "```http",
            "GET /api/v1/execution/subjects HTTP/1.1",
            "Host: localhost:8000",
            "Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
            "X-Change-Reason: Routine monitoring inspection",
            "Content-Type: application/json",
            "```",
            "",
            "### Injected Downstream Service Headers",
            "",
            "| Header | Source | Description |",
            "| :--- | :--- | :--- |",
            "| `X-User-Id` | Gateway Proxy | Authenticated Keycloak Subject Identifier (UUID) |",
            "| `X-User-Roles` | Gateway Proxy | Comma-separated list of assigned GxP user roles |",
            "| `X-Gateway-Signature` | Gateway Proxy | Canonical HMAC-SHA256 signature verifying internal traffic origin |",
            "| `X-Change-Reason` | Client Caller | Mandatory 21 CFR Part 11 justification string for all mutating writes |",
            "| `X-Signature-Token` | Client Caller | 60-second step-up TOTP re-authentication token for signature gates |",
            "",
            "---",
            "",
            "## ⚠️ Standard RFC 7807 Problem Detail Format",
            "",
            "In the event of an API error, Cadence returns standardized RFC 7807 error envelopes:",
            "",
            "```json",
            "{",
            "  \"type\": \"https://cadence.clinical/errors/resource-locked\",",
            "  \"title\": \"Resource Locked\",",
            "  \"status\": 423,",
            "  \"detail\": \"Site SITE-101 has not achieved 6-facet greenlight activation. Randomization blocked.\",",
            "  \"instance\": \"/api/v1/execution/subjects/SUBJ-001/randomize\"",
            "}",
            "```"
        });

        return string.Join("\n", lines);
    }

    private static string JsonNodeToYaml(JsonNode node, int indent = 0)
    {
        var sb = new StringBuilder();
        AppendYaml(node, indent, sb, isArrayItem: false);
        return sb.ToString();
    }

    private static void AppendYaml(JsonNode node, int indent, StringBuilder sb, bool isArrayItem)
    {
        string indentStr = new string(' ', indent);

        if (node is JsonObject obj)
        {
            bool first = true;
            foreach (var kv in obj)
            {
                if (!first && isArrayItem)
                {
                    sb.Append(indentStr);
                }

                sb.Append(kv.Key).Append(':');

                if (kv.Value is JsonValue val)
                {
                    sb.Append(' ').AppendLine(FormatYamlValue(val));
                }
                else if (kv.Value is JsonObject childObj)
                {
                    sb.AppendLine();
                    AppendYaml(childObj, indent + 2, sb, isArrayItem: false);
                }
                else if (kv.Value is JsonArray childArr)
                {
                    sb.AppendLine();
                    AppendYaml(childArr, indent + 2, sb, isArrayItem: false);
                }
                else
                {
                    sb.AppendLine(" null");
                }

                first = false;
                isArrayItem = false;
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                sb.Append(indentStr).Append("- ");
                if (item is JsonValue val)
                {
                    sb.AppendLine(FormatYamlValue(val));
                }
                else if (item is JsonObject itemObj)
                {
                    AppendYaml(itemObj, indent + 2, sb, isArrayItem: true);
                }
                else if (item is JsonArray itemArr)
                {
                    sb.AppendLine();
                    AppendYaml(itemArr, indent + 2, sb, isArrayItem: false);
                }
            }
        }
        else if (node is JsonValue val)
        {
            sb.AppendLine(FormatYamlValue(val));
        }
    }

    private static string FormatYamlValue(JsonValue val)
    {
        if (val.TryGetValue<string>(out var s))
        {
            if (s.Contains('\n'))
            {
                var lines = s.Split('\n');
                var sb = new StringBuilder("|\n");
                foreach (var l in lines)
                {
                    sb.Append("  ").AppendLine(l);
                }
                return sb.ToString().TrimEnd();
            }

            if (s.Length == 0 || s.Contains(':') || s.Contains('#') || s.Contains('{') || s.Contains('}') ||
                s.Contains('[') || s.Contains(']') || s.Contains(',') || s.Contains('&') || s.Contains('*') ||
                s.Contains('?') || s.Contains('|') || s.Contains('>') || s.Contains('!') || s.Contains('%') ||
                s.Contains('@') || s.Contains('`') || s.StartsWith('-') || s.StartsWith(' ') || s.EndsWith(' '))
            {
                return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n")}\"";
            }

            return s;
        }

        return val.ToJsonString();
    }
}
