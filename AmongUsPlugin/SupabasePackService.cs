using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Il2CppInterop.Runtime.Attributes;

namespace AmongUsPlugin;

internal sealed class SupabasePackService
{
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [HideFromIl2Cpp]
    public async Task<ModActionResult> DownloadPackAsync(string roomCode, ModFileService modFileService, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modFileService);

        var normalizedCode = NormalizeCode(roomCode);
        StarterPlugin.Log.LogInfo($"Victor Launcher download requested for room code '{normalizedCode}'.");
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return ModActionResult.Fail("Enter a room code first.");
        }

        var projectUrl = ReadConfiguredValue(StarterPlugin.SupabaseUrl.Value, StarterPlugin.DefaultSupabaseUrl);
        var anonKey = ReadConfiguredValue(StarterPlugin.SupabaseAnonKey.Value, StarterPlugin.DefaultSupabaseAnonKey);
        var endpointName = ReadConfiguredValue(StarterPlugin.SupabaseManifestRpc.Value, "room-manifest");
        var publicKeyPem = ReadConfiguredValue(StarterPlugin.ManifestSigningPublicKeyPem.Value, StarterPlugin.DefaultManifestSigningPublicKeyPem);

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(anonKey))
        {
            return ModActionResult.Fail("Room downloads are not configured yet.");
        }

        var (responseBody, responseSource, lookupError) = await FetchManifestResponseAsync(projectUrl, anonKey, endpointName, normalizedCode, cancellationToken).ConfigureAwait(false);
        if (lookupError != null)
        {
            return lookupError;
        }
        StarterPlugin.Log.LogInfo($"Victor Launcher manifest source: {responseSource}.");

        var parseResult = TryParseManifestResponse(responseBody, normalizedCode, projectUrl, publicKeyPem);
        if (!parseResult.Succeeded)
        {
            return ModActionResult.Fail(parseResult.ErrorMessage);
        }

        var envelope = parseResult.Envelope!;
        if (!IsEnvelopeFresh(envelope))
        {
            return ModActionResult.Fail("The room manifest has expired. Try again in a moment.");
        }

        if (envelope.Manifest.Files.Count == 0)
        {
            return ModActionResult.Fail("That room pack does not contain any files yet.");
        }

        var downloadedCount = 0;
        foreach (var file in envelope.Manifest.Files)
        {
            var validationError = ValidateManifestFile(file, projectUrl);
            if (validationError != null)
            {
                return ModActionResult.Fail(validationError);
            }

            using var fileResponse = await HttpClient.GetAsync(file.Url, cancellationToken).ConfigureAwait(false);
            if (!fileResponse.IsSuccessStatusCode)
            {
                StarterPlugin.Log.LogError($"DLL download failed for {file.Url}: {(int)fileResponse.StatusCode} {fileResponse.ReasonPhrase}");
                return ModActionResult.Fail($"Failed to download {Path.GetFileName(file.Path)}.");
            }

            var bytes = await fileResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (file.SizeBytes > 0 && bytes.Length != file.SizeBytes)
            {
                return ModActionResult.Fail($"The downloaded size for {Path.GetFileName(file.Path)} did not match the manifest.");
            }

            if (!string.IsNullOrWhiteSpace(file.Sha256))
            {
                var actualHash = ComputeSha256(bytes);
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return ModActionResult.Fail($"Hash check failed for {Path.GetFileName(file.Path)}.");
                }
            }

            var stageResult = modFileService.StageDownloadedDll(normalizedCode, Path.GetFileName(file.Path), bytes);
            if (!stageResult.Succeeded)
            {
                return stageResult;
            }

            downloadedCount++;
        }

        var packName = string.IsNullOrWhiteSpace(envelope.RoomName) ? envelope.Manifest.Name : envelope.RoomName;
        return ModActionResult.Success($"{packName} queued {downloadedCount} DLL {(downloadedCount == 1 ? "download" : "downloads")}. Install them from the queue when you're ready.");
    }

    [HideFromIl2Cpp]
    private static async Task<(string ResponseBody, string ResponseSource, ModActionResult? Error)> FetchManifestResponseAsync(
        string projectUrl,
        string anonKey,
        string endpointName,
        string normalizedCode,
        CancellationToken cancellationToken)
    {
        foreach (var candidateEndpoint in GetManifestEndpoints(endpointName))
        {
            var functionUrl = $"{projectUrl.TrimEnd('/')}/functions/v1/{Uri.EscapeDataString(candidateEndpoint)}?roomCode={Uri.EscapeDataString(normalizedCode)}";
            using var functionRequest = new HttpRequestMessage(HttpMethod.Get, functionUrl);
            functionRequest.Headers.Add("apikey", anonKey);
            functionRequest.Headers.Add("Authorization", $"Bearer {anonKey}");

            using var functionResponse = await HttpClient.SendAsync(functionRequest, cancellationToken).ConfigureAwait(false);
            var functionBody = await functionResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            StarterPlugin.Log.LogInfo($"Room manifest lookup via '{candidateEndpoint}' returned {(int)functionResponse.StatusCode} {functionResponse.ReasonPhrase}.");

            if (functionResponse.IsSuccessStatusCode)
            {
                return (functionBody, $"edge-function:{candidateEndpoint}", null);
            }

            StarterPlugin.Log.LogError($"Room manifest lookup via '{candidateEndpoint}' failed: {(int)functionResponse.StatusCode} {functionResponse.ReasonPhrase}");
            StarterPlugin.Log.LogError(functionBody);

            if (!ShouldFallbackToRpc(functionResponse.StatusCode, functionBody))
            {
                return ("", "", ModActionResult.Fail("Could not find that room code."));
            }
        }

        StarterPlugin.Log.LogInfo("Victor Launcher is falling back to get_room_manifest RPC.");
        var rpcUrl = $"{projectUrl.TrimEnd('/')}/rest/v1/rpc/get_room_manifest";
        using var rpcRequest = new HttpRequestMessage(HttpMethod.Post, rpcUrl);
        rpcRequest.Headers.Add("apikey", anonKey);
        rpcRequest.Headers.Add("Authorization", $"Bearer {anonKey}");
        rpcRequest.Content = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["p_room_code"] = normalizedCode
            }),
            Encoding.UTF8,
            "application/json");

        using var rpcResponse = await HttpClient.SendAsync(rpcRequest, cancellationToken).ConfigureAwait(false);
        var rpcBody = await rpcResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        StarterPlugin.Log.LogInfo($"Legacy room RPC lookup returned {(int)rpcResponse.StatusCode} {rpcResponse.ReasonPhrase}.");

        if (!rpcResponse.IsSuccessStatusCode)
        {
            StarterPlugin.Log.LogError($"Legacy room RPC lookup failed: {(int)rpcResponse.StatusCode} {rpcResponse.ReasonPhrase}");
            StarterPlugin.Log.LogError(rpcBody);
            return ("", "", ModActionResult.Fail("Could not find that room code."));
        }

        return (rpcBody, "rpc-fallback", null);
    }

    [HideFromIl2Cpp]
    private static bool ShouldFallbackToRpc(System.Net.HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode != System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        return responseBody.Contains("\"Requested function was not found\"", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("\"code\":\"NOT_FOUND\"", StringComparison.OrdinalIgnoreCase);
    }

    [HideFromIl2Cpp]
    private static IReadOnlyList<string> GetManifestEndpoints(string configuredEndpoint)
    {
        var endpoints = new List<string>();
        AddEndpoint(endpoints, "room-manifest");
        AddEndpoint(endpoints, configuredEndpoint);
        return endpoints;
    }

    [HideFromIl2Cpp]
    private static void AddEndpoint(ICollection<string> endpoints, string? endpoint)
    {
        var trimmed = endpoint?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        if (endpoints.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        endpoints.Add(trimmed);
    }

    [HideFromIl2Cpp]
    private static ManifestParseResult TryParseManifestResponse(string responseBody, string normalizedCode, string projectUrl, string publicKeyPem)
    {
        try
        {
            var signedResponse = JsonSerializer.Deserialize<SignedManifestResponse>(responseBody, JsonOptions);
            if (signedResponse != null &&
                !string.IsNullOrWhiteSpace(signedResponse.CanonicalPayload) &&
                !string.IsNullOrWhiteSpace(signedResponse.Signature))
            {
                if (!string.IsNullOrWhiteSpace(publicKeyPem) &&
                    !VerifyManifestSignature(publicKeyPem, signedResponse.CanonicalPayload, signedResponse.Signature))
                {
                    return ManifestParseResult.Fail("The room manifest signature could not be verified.");
                }

                var signedEnvelope = JsonSerializer.Deserialize<SignedManifestEnvelope>(signedResponse.CanonicalPayload, JsonOptions);
                if (signedEnvelope?.Manifest == null)
                {
                    return ManifestParseResult.Fail("The signed manifest payload could not be read.");
                }

                if (!string.Equals(signedEnvelope.RoomCode, normalizedCode, StringComparison.OrdinalIgnoreCase))
                {
                    return ManifestParseResult.Fail("The signed manifest room code did not match the room you requested.");
                }

                return ManifestParseResult.Success(NormalizeEnvelopeUrls(signedEnvelope, projectUrl));
            }
        }
        catch (JsonException exception)
        {
            StarterPlugin.Log.LogWarning($"Signed manifest parse failed, trying legacy formats. {exception.Message}");
        }

        try
        {
            var directEnvelopeResponse = JsonSerializer.Deserialize<DirectEnvelopeResponse>(responseBody, JsonOptions);
            if (directEnvelopeResponse?.Envelope?.Manifest != null)
            {
                return ManifestParseResult.Success(NormalizeEnvelopeUrls(directEnvelopeResponse.Envelope, projectUrl));
            }
        }
        catch (JsonException exception)
        {
            StarterPlugin.Log.LogWarning($"Direct envelope parse failed, trying legacy room rows. {exception.Message}");
        }

        try
        {
            var legacyRows = JsonSerializer.Deserialize<LegacyRoomRow[]>(responseBody, JsonOptions);
            var legacyRow = legacyRows?.FirstOrDefault();
            if (legacyRow?.Manifest != null)
            {
                return ManifestParseResult.Success(BuildEnvelopeFromLegacyRow(legacyRow, normalizedCode, projectUrl));
            }
        }
        catch (JsonException exception)
        {
            StarterPlugin.Log.LogWarning($"Legacy row array parse failed. {exception.Message}");
        }

        try
        {
            var legacyRow = JsonSerializer.Deserialize<LegacyRoomRow>(responseBody, JsonOptions);
            if (legacyRow?.Manifest != null)
            {
                return ManifestParseResult.Success(BuildEnvelopeFromLegacyRow(legacyRow, normalizedCode, projectUrl));
            }
        }
        catch (JsonException exception)
        {
            StarterPlugin.Log.LogWarning($"Legacy row object parse failed. {exception.Message}");
        }

        return ManifestParseResult.Fail("The room service returned data Victor Launcher could not understand.");
    }

    [HideFromIl2Cpp]
    private static SignedManifestEnvelope NormalizeEnvelopeUrls(SignedManifestEnvelope envelope, string projectUrl)
    {
        var normalizedFiles = envelope.Manifest!.Files
            .Select(file => file with { Url = NormalizeDownloadUrl(file.Url, projectUrl) })
            .ToArray();

        return envelope with
        {
            Manifest = envelope.Manifest with
            {
                Files = normalizedFiles
            }
        };
    }

    [HideFromIl2Cpp]
    private static SignedManifestEnvelope BuildEnvelopeFromLegacyRow(LegacyRoomRow row, string normalizedCode, string projectUrl)
    {
        var files = row.Manifest!.Files
            .Select(file => new SignedManifestFile
            {
                Path = file.Path,
                Url = NormalizeDownloadUrl(file.Url, projectUrl),
                StoragePath = file.StoragePath,
                Sha256 = file.Sha256 ?? "",
                SizeBytes = file.SizeBytes
            })
            .ToArray();

        return new SignedManifestEnvelope
        {
            RoomCode = string.IsNullOrWhiteSpace(row.RoomCode) ? normalizedCode : row.RoomCode,
            RoomName = row.RoomName ?? row.Manifest.Name,
            ManifestVersion = row.ManifestVersion <= 0 ? 1 : row.ManifestVersion,
            IssuedAtUtc = row.UpdatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(12).ToString("O"),
            Manifest = new SignedManifest
            {
                Id = row.Manifest.Id,
                Name = row.Manifest.Name,
                Version = row.Manifest.Version,
                GameVersion = row.Manifest.GameVersion,
                Loader = row.Manifest.Loader,
                Dependencies = row.Manifest.Dependencies,
                Conflicts = row.Manifest.Conflicts,
                Files = files
            }
        };
    }

    [HideFromIl2Cpp]
    private static string NormalizeDownloadUrl(string url, string projectUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var trimmedUrl = url.Trim();

        if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out _))
        {
            return trimmedUrl;
        }

        if (trimmedUrl.StartsWith("object/sign/", StringComparison.OrdinalIgnoreCase))
        {
            return projectUrl.TrimEnd('/') + "/storage/v1/" + trimmedUrl;
        }

        if (trimmedUrl.StartsWith("/object/sign/", StringComparison.OrdinalIgnoreCase))
        {
            return projectUrl.TrimEnd('/') + "/storage/v1" + trimmedUrl;
        }

        if (trimmedUrl.StartsWith("/"))
        {
            return projectUrl.TrimEnd('/') + trimmedUrl;
        }

        return projectUrl.TrimEnd('/') + "/" + trimmedUrl.TrimStart('/');
    }

    [HideFromIl2Cpp]
    private static bool IsEnvelopeFresh(SignedManifestEnvelope envelope)
    {
        if (!DateTimeOffset.TryParse(envelope.IssuedAtUtc, out var issuedAt) ||
            !DateTimeOffset.TryParse(envelope.ExpiresAtUtc, out var expiresAt))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        return issuedAt <= now.AddMinutes(10) && expiresAt > now.AddMinutes(-1);
    }

    [HideFromIl2Cpp]
    private static bool VerifyManifestSignature(string publicKeyPem, string payload, string signatureBase64Url)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(payload),
                DecodeBase64Url(signatureBase64Url),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception exception)
        {
            StarterPlugin.Log.LogError("Manifest signature verification failed.");
            StarterPlugin.Log.LogError(exception);
            return false;
        }
    }

    [HideFromIl2Cpp]
    private static string ReadConfiguredValue(string? configured, string fallback)
    {
        var trimmed = configured?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    [HideFromIl2Cpp]
    private static string NormalizeCode(string roomCode)
    {
        return (roomCode ?? string.Empty).Trim().ToUpperInvariant();
    }

    [HideFromIl2Cpp]
    private static string? ValidateManifestFile(SignedManifestFile file, string projectUrl)
    {
        if (string.IsNullOrWhiteSpace(file.Path))
        {
            return "A pack file was missing its target path.";
        }

        if (!file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return $"Only DLL files are supported right now, but the pack included {file.Path}.";
        }

        if (string.IsNullOrWhiteSpace(file.Url) && !string.IsNullOrWhiteSpace(file.StoragePath))
        {
            StarterPlugin.Log.LogError($"Manifest file {file.Path} only contained storagePath '{file.StoragePath}'. Victor Launcher needs the signed room-manifest response for private bucket downloads.");
            return $"Victor Launcher could not get a signed download link for {file.Path}.";
        }

        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            StarterPlugin.Log.LogError($"Manifest file {file.Path} had an invalid URL value: '{file.Url}'.");
            return $"The room included an invalid download URL for {file.Path}.";
        }

        var projectHost = new Uri(projectUrl).Host;
        if (!string.Equals(uri.Host, projectHost, StringComparison.OrdinalIgnoreCase))
        {
            return $"The room included a download host Victor Launcher does not trust for {file.Path}.";
        }

        return null;
    }

    [HideFromIl2Cpp]
    private static byte[] DecodeBase64Url(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        var paddingLength = 4 - (normalized.Length % 4);
        if (paddingLength is > 0 and < 4)
        {
            normalized += new string('=', paddingLength);
        }

        return Convert.FromBase64String(normalized);
    }

    [HideFromIl2Cpp]
    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}

internal sealed record ManifestParseResult(bool Succeeded, SignedManifestEnvelope? Envelope, string ErrorMessage)
{
    public static ManifestParseResult Success(SignedManifestEnvelope envelope) => new(true, envelope, "");
    public static ManifestParseResult Fail(string errorMessage) => new(false, null, errorMessage);
}

internal sealed class SignedManifestResponse
{
    [JsonPropertyName("signingKeyId")]
    public string SigningKeyId { get; init; } = "";

    [JsonPropertyName("canonicalPayload")]
    public string CanonicalPayload { get; init; } = "";

    [JsonPropertyName("signature")]
    public string Signature { get; init; } = "";
}

internal sealed class DirectEnvelopeResponse
{
    [JsonPropertyName("envelope")]
    public SignedManifestEnvelope? Envelope { get; init; }
}

internal sealed record SignedManifestEnvelope
{
    [JsonPropertyName("roomCode")]
    public string RoomCode { get; init; } = "";

    [JsonPropertyName("roomName")]
    public string RoomName { get; init; } = "";

    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; init; }

    [JsonPropertyName("issuedAtUtc")]
    public string IssuedAtUtc { get; init; } = "";

    [JsonPropertyName("expiresAtUtc")]
    public string ExpiresAtUtc { get; init; } = "";

    [JsonPropertyName("manifest")]
    public SignedManifest? Manifest { get; init; }
}

internal sealed record SignedManifest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; init; } = "";

    [JsonPropertyName("loader")]
    public string Loader { get; init; } = "BepInEx";

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<string> Conflicts { get; init; } = Array.Empty<string>();

    [JsonPropertyName("files")]
    public IReadOnlyList<SignedManifestFile> Files { get; init; } = Array.Empty<SignedManifestFile>();
}

internal sealed record SignedManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("storagePath")]
    public string StoragePath { get; init; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";

    [JsonPropertyName("sizeBytes")]
    public int SizeBytes { get; init; }
}

internal sealed class LegacyRoomRow
{
    [JsonPropertyName("room_code")]
    public string RoomCode { get; init; } = "";

    [JsonPropertyName("room_name")]
    public string RoomName { get; init; } = "";

    [JsonPropertyName("manifest_version")]
    public int ManifestVersion { get; init; }

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = "";

    [JsonPropertyName("manifest")]
    public LegacyManifest? Manifest { get; init; }
}

internal sealed class LegacyManifest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; init; } = "";

    [JsonPropertyName("loader")]
    public string Loader { get; init; } = "BepInEx";

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<string> Conflicts { get; init; } = Array.Empty<string>();

    [JsonPropertyName("files")]
    public IReadOnlyList<LegacyManifestFile> Files { get; init; } = Array.Empty<LegacyManifestFile>();
}

internal sealed class LegacyManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("storagePath")]
    public string StoragePath { get; init; } = "";

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; } = "";

    [JsonPropertyName("sizeBytes")]
    public int SizeBytes { get; init; }
}
