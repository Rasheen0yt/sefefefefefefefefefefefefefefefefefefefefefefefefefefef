using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GreedyDownloader.Services;

public class PasteScraper
{
    private readonly HttpClient _httpClient;

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""'\)\]\}]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string Base58Chars =
        "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public PasteScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Extract download URLs from a paste link or web page.
    /// Handles PrivateBin encrypted pastes automatically.
    /// </summary>
    public async Task<List<string>> ExtractUrlsAsync(string inputUrl)
    {
        try
        {
            if (inputUrl.Contains("linkvault.cybar.to") || inputUrl.Contains("cybar.to"))
            {
                Debug.WriteLine($"Detected Cybar LinkVault URL: {inputUrl}");
                var urls = await ScrapeWithPlaywrightAsync(inputUrl);
                if (urls.Count > 0) return urls;
            }

            if (IsPrivateBinUrl(inputUrl))
            {
                Debug.WriteLine($"Detected PrivateBin URL: {inputUrl}");
                var urls = await DecryptPrivateBinAsync(inputUrl);
                if (urls.Count > 0) return urls;
            }

            // Fallback: simple HTTP page scrape
            return await ScrapePageAsync(inputUrl);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PasteScraper error: {ex.Message}");
            return new List<string>();
        }
    }

    private async Task<List<string>> ScrapeWithPlaywrightAsync(string url)
    {
        try
        {
            using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new Microsoft.Playwright.BrowserTypeLaunchOptions
            {
                Headless = true
            });
            var page = await browser.NewPageAsync();
            
            // Go to URL and wait for Cloudflare / JS to render
            await page.GotoAsync(url, new Microsoft.Playwright.PageGotoOptions 
            { 
                WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                Timeout = 30000
            });
            
            // Wait for links to appear (give CF some time if challenged)
            await Task.Delay(5000);
            
            // LinkVault sometimes has a "Reveal" button, but typically hash links automatically decrypt.
            // Wait an extra moment just in case
            await Task.Delay(2000);
            
            // Extract all text from the body to find plain text URLs
            var pageText = await page.EvaluateAsync<string>("() => document.body.innerText");
            var links = ExtractUrls(pageText);

            // Also extract any valid hrefs
            var aHrefs = await page.EvaluateAsync<string[]>(@"() => {
                return Array.from(document.querySelectorAll('a'))
                    .map(a => a.href)
                    .filter(href => href && href.startsWith('http'));
            }");

            links.AddRange(aHrefs.Where(u => !IsJunkUrl(u)));
            
            return links.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Playwright error: {ex.Message}");
            return new List<string>();
        }
    }

    private bool IsPrivateBinUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            if (string.IsNullOrEmpty(uri.Fragment) || uri.Fragment.Length < 5) return false;
            var query = uri.Query.TrimStart('?');
            if (string.IsNullOrEmpty(query)) return false;
            // PrivateBin paste IDs are hex strings
            var pasteId = query.Split('&')[0].Split('=')[0];
            return Regex.IsMatch(pasteId, "^[a-fA-F0-9]{8,}$");
        }
        catch { return false; }
    }

    private async Task<List<string>> DecryptPrivateBinAsync(string url)
    {
        var uri = new Uri(url);
        var pasteId = uri.Query.TrimStart('?').Split('&')[0].Split('=')[0];
        var fragmentKey = uri.Fragment.TrimStart('#');

        // Fetch paste JSON via API
        var apiUrl = $"{uri.Scheme}://{uri.Host}/?pasteid={pasteId}";
        var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Requested-With", "JSONHttpRequest");

        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        Debug.WriteLine($"PrivateBin API response length: {json.Length}");

        var doc = JsonNode.Parse(json);
        if (doc == null) return new List<string>();

        var status = doc["status"]?.GetValue<int>() ?? -1;
        if (status != 0) return new List<string>();

        var version = doc["v"]?.GetValue<int>() ?? 1;
        if (version < 2) return await ScrapePageAsync(url); // v1 not supported, fallback

        // Decrypt v2 paste
        var pasteText = DecryptV2(doc, fragmentKey);
        Debug.WriteLine($"Decrypted paste length: {pasteText.Length}");

        return ExtractUrls(pasteText);
    }

    private string DecryptV2(JsonNode doc, string keyString)
    {
        var ctB64 = doc["ct"]?.GetValue<string>()
            ?? throw new Exception("Missing ciphertext");
        var adata = doc["adata"] as JsonArray
            ?? throw new Exception("Missing adata");
        var spec = adata[0] as JsonArray
            ?? throw new Exception("Missing spec");

        // Parse encryption parameters
        var ivB64 = spec[0]?.GetValue<string>() ?? "";
        var saltB64 = spec[1]?.GetValue<string>() ?? "";
        var iterations = spec[2]?.GetValue<int>() ?? 100000;
        var keySize = spec[3]?.GetValue<int>() ?? 256;
        var tagSize = spec[4]?.GetValue<int>() ?? 128;
        var compression = (spec.Count > 7 ? spec[7]?.GetValue<string>() : null) ?? "none";

        var iv = Convert.FromBase64String(ivB64);
        var salt = Convert.FromBase64String(saltB64);
        var keyBytes = Base58Decode(keyString);

        // Derive key via PBKDF2-SHA256
        var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            keyBytes, salt, iterations, HashAlgorithmName.SHA256, keySize / 8);

        // Split ciphertext and GCM tag
        var ctBytes = Convert.FromBase64String(ctB64);
        int tagBytes = tagSize / 8;
        var ciphertext = ctBytes[..^tagBytes];
        var tag = ctBytes[^tagBytes..];
        var plaintext = new byte[ciphertext.Length];

        // AAD = JSON serialization of the adata array
        var aad = Encoding.UTF8.GetBytes(adata.ToJsonString());

        using var aes = new AesGcm(derivedKey, tagBytes);
        aes.Decrypt(iv, ciphertext, tag, plaintext, aad);

        // Decompress if zlib
        byte[] raw;
        if (compression == "zlib")
        {
            using var ms = new MemoryStream(plaintext);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            zlib.CopyTo(outMs);
            raw = outMs.ToArray();
        }
        else
        {
            raw = plaintext;
        }

        var resultJson = Encoding.UTF8.GetString(raw);

        // PrivateBin wraps paste in {"paste":"..."}
        try
        {
            var pasteDoc = JsonNode.Parse(resultJson);
            return pasteDoc?["paste"]?.GetValue<string>() ?? resultJson;
        }
        catch
        {
            return resultJson;
        }
    }

    private async Task<List<string>> ScrapePageAsync(string url)
    {
        try
        {
            var html = await _httpClient.GetStringAsync(url);
            return ExtractUrls(html);
        }
        catch { return new List<string>(); }
    }

    private List<string> ExtractUrls(string content)
    {
        return UrlRegex.Matches(content)
            .Select(m => m.Value.TrimEnd('.', ',', ';', ')', ']', '}', '\'', '"'))
            .Where(u => !IsJunkUrl(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsJunkUrl(string url)
    {
        var l = url.ToLowerInvariant();
        // Skip obvious non-download resources
        string[] junk = {
            "javascript:", "mailto:", "fonts.googleapis",
            "cdn.jsdelivr", ".css", ".js?", ".svg",
            "google.com/", "facebook.com", "twitter.com",
            "schema.org", "w3.org", "github.com/",
            "jquery", "bootstrap", ".ico"
        };
        return junk.Any(j => l.Contains(j));
    }

    // Base58 decoder (Bitcoin alphabet).
    private static byte[] Base58Decode(string input)
    {
        BigInteger value = BigInteger.Zero;
        foreach (char c in input)
        {
            int idx = Base58Chars.IndexOf(c);
            if (idx < 0) throw new FormatException($"Bad Base58 char: {c}");
            value = value * 58 + idx;
        }

        var bytes = value.ToByteArray(); // little-endian, may have sign byte

        // Strip trailing sign byte
        if (bytes.Length > 1 && bytes[^1] == 0)
            bytes = bytes[..^1];

        // Reverse to big-endian
        Array.Reverse(bytes);

        // Handle leading '1's as leading zero bytes.
        int leadingOnes = input.TakeWhile(c => c == '1').Count();
        if (leadingOnes > 0)
        {
            var full = new byte[leadingOnes + bytes.Length];
            Array.Copy(bytes, 0, full, leadingOnes, bytes.Length);
            return full;
        }
        return bytes;
    }
}
