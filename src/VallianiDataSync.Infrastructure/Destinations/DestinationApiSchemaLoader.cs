using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Infrastructure.Destinations;

/// <summary>
/// Discovers destination field keys from the project's API URL (schema/fields/template/sample JSON).
/// Import-only endpoints often need the API key; without it the call returns 401.
/// </summary>
public static class DestinationApiSchemaLoader
{
    private static readonly string[] SchemaPathSuffixes =
    {
        "schema", "fields", "columns", "template", "format", "meta", "metadata",
        "import/schema", "import/fields", "import/template", "import/columns"
    };

    public static async Task<(bool Ok, string Message, List<DestinationFormatField> Fields, string BodyFormatHint)> LoadAsync(
        string apiUrl,
        string? apiKey,
        string authHeaderName = "Authorization",
        string authHeaderPrefix = "Bearer ",
        HttpClient? httpClient = null,
        string? fieldsUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiUrl) || !Uri.TryCreate(apiUrl.Trim(), UriKind.Absolute, out var baseUri))
            return (false, "Valid API URL required first.", new(), "JsonArray");

        var ownClient = httpClient is null;
        var client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var tried = new List<string>();
            var saw401 = false;

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(fieldsUrl) && Uri.TryCreate(fieldsUrl.Trim(), UriKind.Absolute, out _))
                candidates.Add(fieldsUrl.Trim());
            candidates.AddRange(BuildCandidateUrls(baseUri));

            foreach (var url in candidates)
            {
                tried.Add(url);
                var (status, body, contentType) = await GetAsync(client, url, apiKey, authHeaderName, authHeaderPrefix, cancellationToken)
                    .ConfigureAwait(false);
                if (status == 401 || status == 403)
                {
                    saw401 = true;
                    continue;
                }
                if (status is < 200 or >= 300 || string.IsNullOrWhiteSpace(body))
                    continue;

                var parsed = ParseResponseBody(body, contentType);
                if (parsed.Fields.Count > 0)
                    return (true,
                        $"Loaded {parsed.Fields.Count} destination field(s) from API ({ShortUrl(url)}).",
                        parsed.Fields,
                        parsed.BodyHint);
            }

            // Probe import URL with empty payload — some APIs echo required keys in the error JSON.
            var (postStatus, postBody, _) = await PostAsync(
                    client, baseUri.AbsoluteUri, "[]", apiKey, authHeaderName, authHeaderPrefix, cancellationToken)
                .ConfigureAwait(false);
            if (postStatus == 401 || postStatus == 403)
                saw401 = true;
            else if (!string.IsNullOrWhiteSpace(postBody))
            {
                var fromError = TryParseValidationFields(postBody);
                if (fromError.Count > 0)
                    return (true,
                        $"Loaded {fromError.Count} field(s) from API validation response.",
                        fromError.Select(FormatTemplateImporter.FromKey).ToList(),
                        "JsonArray");
            }

            if (saw401 && string.IsNullOrWhiteSpace(apiKey))
                return (false,
                    "API returned 401 — enter API key / token, then load fields again.",
                    new(),
                    "JsonArray");

            if (saw401)
                return (false,
                    "API key rejected (401/403). Check the token, then load fields again.",
                    new(),
                    "JsonArray");

            return (false,
                "Could not load fields from the API. Need a schema/fields/template endpoint or a sample JSON response. Tried: "
                + string.Join(", ", tried.Take(6).Select(ShortUrl)),
                new(),
                "JsonArray");
        }
        catch (Exception ex)
        {
            return (false, $"API field load failed: {ex.Message}", new(), "JsonArray");
        }
        finally
        {
            if (ownClient)
                client.Dispose();
        }
    }

    private static IEnumerable<string> BuildCandidateUrls(Uri apiUri)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        void Add(string u)
        {
            if (seen.Add(u))
                list.Add(u);
        }

        Add(apiUri.AbsoluteUri);

        // query variants on same URL
        Add(apiUri.AbsoluteUri.TrimEnd('?', '&') + (apiUri.Query.Length == 0 ? "?" : "&") + "schema=1");
        Add(apiUri.AbsoluteUri.TrimEnd('?', '&') + (apiUri.Query.Length == 0 ? "?" : "&") + "fields=1");

        var path = apiUri.AbsolutePath.TrimEnd('/');
        var origin = $"{apiUri.Scheme}://{apiUri.Authority}";

        // Drop last segment (e.g. /import) then append schema suffixes
        var parent = path;
        var slash = path.LastIndexOf('/');
        if (slash > 0)
            parent = path[..slash];

        foreach (var root in new[] { path, parent })
        {
            foreach (var suffix in SchemaPathSuffixes)
                Add($"{origin}{root}/{suffix}");
        }

        // Common OpenAPI locations
        Add($"{origin}/swagger/v1/swagger.json");
        Add($"{origin}/openapi.json");
        Add($"{origin}/api/openapi.json");
        Add($"{origin}/api/v2/openapi.json");

        return list;
    }

    private static async Task<(int Status, string Body, string? ContentType)> GetAsync(
        HttpClient client,
        string url,
        string? apiKey,
        string authHeaderName,
        string authHeaderPrefix,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(req, apiKey, authHeaderName, authHeaderPrefix);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
        try
        {
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ((int)resp.StatusCode, body, resp.Content.Headers.ContentType?.MediaType);
        }
        catch
        {
            return (0, "", null);
        }
    }

    private static async Task<(int Status, string Body, string? ContentType)> PostAsync(
        HttpClient client,
        string url,
        string jsonBody,
        string? apiKey,
        string authHeaderName,
        string authHeaderPrefix,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(req, apiKey, authHeaderName, authHeaderPrefix);
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        try
        {
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ((int)resp.StatusCode, body, resp.Content.Headers.ContentType?.MediaType);
        }
        catch
        {
            return (0, "", null);
        }
    }

    private static void ApplyAuth(HttpRequestMessage req, string? apiKey, string authHeaderName, string authHeaderPrefix)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return;
        var name = string.IsNullOrWhiteSpace(authHeaderName) ? "Authorization" : authHeaderName.Trim();
        var prefix = authHeaderPrefix ?? "";
        req.Headers.TryAddWithoutValidation(name, prefix + apiKey.Trim());
    }

    /// <summary>Parse schema / sample / CSV header body into destination keys (also used by tests).</summary>
    public static (List<DestinationFormatField> Fields, string BodyHint) ParseResponseBody(string body, string? contentType)
    {
        var trimmed = body.TrimStart();
        if (trimmed.Length == 0)
            return (new(), "JsonArray");

        // CSV template
        if ((contentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) == true)
            || (!trimmed.StartsWith('{') && !trimmed.StartsWith('[') && trimmed.Contains(',')))
        {
            var header = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "";
            var keys = SplitCsvHeader(header);
            if (keys.Count > 0)
                return (keys.Select(FormatTemplateImporter.FromKey).ToList(), "Csv");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var keys = ExtractKeys(doc.RootElement);
            if (keys.Count > 0)
                return (keys.Select(FormatTemplateImporter.FromKey).ToList(), "JsonArray");
        }
        catch
        {
            /* not JSON */
        }

        return (new(), "JsonArray");
    }

    private static List<string> ExtractKeys(JsonElement root)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? k)
        {
            if (string.IsNullOrWhiteSpace(k))
                return;
            k = k.Trim();
            if (seen.Add(k))
                keys.Add(k);
        }

        void FromObject(JsonElement obj)
        {
            foreach (var p in obj.EnumerateObject())
            {
                // Skip envelope keys; prefer nested field lists
                if (p.NameEquals("fields") || p.NameEquals("columns") || p.NameEquals("keys")
                    || p.NameEquals("properties") || p.NameEquals("attributes") || p.NameEquals("schema"))
                {
                    CollectFromValue(p.Value);
                    continue;
                }
                if (p.NameEquals("data") || p.NameEquals("rows") || p.NameEquals("items") || p.NameEquals("sample"))
                {
                    CollectFromValue(p.Value);
                    continue;
                }
            }

            // If no nested list, treat object itself as sample row
            if (keys.Count == 0)
            {
                foreach (var p in obj.EnumerateObject())
                    Add(p.Name);
            }
        }

        void CollectFromValue(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Array:
                    if (el.GetArrayLength() == 0)
                        return;
                    var first = el[0];
                    if (first.ValueKind == JsonValueKind.String)
                    {
                        foreach (var item in el.EnumerateArray())
                            Add(item.GetString());
                    }
                    else if (first.ValueKind == JsonValueKind.Object)
                    {
                        // fields: [{ destinationKey/name/key: "sku" }, ...] OR sample rows
                        var looksLikeFieldDefs = first.TryGetProperty("destinationKey", out _)
                            || first.TryGetProperty("key", out _)
                            || first.TryGetProperty("name", out _)
                            || first.TryGetProperty("column", out _)
                            || first.TryGetProperty("field", out _);
                        if (looksLikeFieldDefs)
                        {
                            foreach (var item in el.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.Object)
                                    continue;
                                var name = item.TryGetProperty("destinationKey", out var dk) ? dk.GetString()
                                    : item.TryGetProperty("key", out var k) ? k.GetString()
                                    : item.TryGetProperty("name", out var n) ? n.GetString()
                                    : item.TryGetProperty("column", out var c) ? c.GetString()
                                    : item.TryGetProperty("field", out var f) ? f.GetString()
                                    : null;
                                Add(name);
                            }
                        }
                        else
                        {
                            foreach (var p in first.EnumerateObject())
                                Add(p.Name);
                        }
                    }
                    break;
                case JsonValueKind.Object:
                    // JSON Schema style: properties: { sku: {}, price: {} }
                    if (el.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in props.EnumerateObject())
                            Add(p.Name);
                        break;
                    }
                    FromObject(el);
                    break;
                case JsonValueKind.String:
                    Add(el.GetString());
                    break;
            }
        }

        CollectFromValue(root);

        // OpenAPI: components.schemas.*.properties
        if (keys.Count == 0 && root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("components", out var components)
            && components.TryGetProperty("schemas", out var schemas)
            && schemas.ValueKind == JsonValueKind.Object)
        {
            foreach (var schema in schemas.EnumerateObject())
            {
                if (schema.Value.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in props.EnumerateObject())
                        Add(p.Name);
                    if (keys.Count > 0)
                        break;
                }
            }
        }

        return keys;
    }

    private static List<string> TryParseValidationFields(string body)
    {
        var keys = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            CollectValidationKeys(doc.RootElement, keys, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            // plain text: "required: sku, price"
            foreach (var token in body.Split(new[] { ' ', ',', '"', '\'', ':', '[', ']', '{', '}', '\n', '\r', '\t' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Equals("required", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("missing", StringComparison.OrdinalIgnoreCase)
                    || token.Length < 2)
                    continue;
            }
        }
        return keys;
    }

    private static void CollectValidationKeys(JsonElement el, List<string> keys, HashSet<string> seen)
    {
        void Add(string? k)
        {
            if (string.IsNullOrWhiteSpace(k) || k.Length > 80)
                return;
            if (seen.Add(k.Trim()))
                keys.Add(k.Trim());
        }

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    if (p.NameEquals("required") || p.NameEquals("missing") || p.NameEquals("fields")
                        || p.NameEquals("columns") || p.NameEquals("errors"))
                        CollectValidationKeys(p.Value, keys, seen);
                    else if (p.NameEquals("path") || p.NameEquals("field") || p.NameEquals("property")
                             || p.NameEquals("param") || p.NameEquals("key"))
                        Add(p.Value.GetString());
                    else
                        CollectValidationKeys(p.Value, keys, seen);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        Add(item.GetString());
                    else
                        CollectValidationKeys(item, keys, seen);
                }
                break;
        }
    }

    private static List<string> SplitCsvHeader(string headerLine)
    {
        var cur = new StringBuilder();
        var inQuotes = false;
        var keys = new List<string>();
        foreach (var ch in headerLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (ch == ',' && !inQuotes)
            {
                var k = cur.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(k))
                    keys.Add(k);
                cur.Clear();
                continue;
            }
            cur.Append(ch);
        }
        var last = cur.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last))
            keys.Add(last);
        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ShortUrl(string url)
    {
        try
        {
            var u = new Uri(url);
            return u.AbsolutePath.TrimEnd('/') + u.Query;
        }
        catch
        {
            return url.Length > 60 ? url[..60] + "…" : url;
        }
    }
}
