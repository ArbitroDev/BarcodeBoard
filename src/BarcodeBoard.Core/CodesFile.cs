using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BarcodeBoard.Core;

public sealed record CodeEntry(string Text, CodeType Type);

/// <summary>Contenu d'un fichier .codes : le nom donné à l'onglet (facultatif) et ses codes, dans l'ordre.</summary>
public sealed record CodesDocument(string? Name, IReadOnlyList<CodeEntry> Codes);

public sealed class InvalidCodesFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Lecture et écriture des fichiers d'onglet <c>.codes</c> (JSON UTF-8).</summary>
public static class CodesFile
{
    public const string Extension = ".codes";
    public const string FormatName = "barcode-board";
    /// <summary>Identifiant des premiers fichiers, toujours accepté à la lecture.</summary>
    private const string LegacyFormatName = "codes-ecran";
    public const int CurrentVersion = 1;
    private static string InvalidMessage => Lang.T("Ce fichier n'est pas un onglet de codes valide.", "This file is not a valid Barcode Board tab.");

    public static string Serialize(IEnumerable<CodeEntry> codes, string? name = null)
    {
        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = CurrentVersion,
        };
        if (!string.IsNullOrWhiteSpace(name)) root["name"] = name;
        root["codes"] = new JsonArray(codes.Select(c => (JsonNode)new JsonObject
        {
            ["text"] = c.Text,
            ["type"] = c.Type == CodeType.Code128 ? "code128" : "qr",
        }).ToArray());
        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    public static CodesDocument Deserialize(string json)
    {
        try { return Read(json); }
        // JSON mal formé, clé en double, valeur d'un type inattendu : toujours « fichier invalide », jamais un plantage.
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new InvalidCodesFileException(InvalidMessage, ex);
        }
    }

    private static CodesDocument Read(string json)
    {
        var root = JsonNode.Parse(json);
        if (root is not JsonObject obj
            || obj["format"]?.GetValueKind() != JsonValueKind.String
            || obj["format"]!.GetValue<string>() is not (FormatName or LegacyFormatName)
            || obj["version"]?.GetValueKind() != JsonValueKind.Number
            || obj["codes"] is not JsonArray items
            || !obj["version"]!.AsValue().TryGetValue<int>(out var version) || version < 1)
            throw new InvalidCodesFileException(InvalidMessage);

        if (version > CurrentVersion)
            throw new InvalidCodesFileException(InvalidMessage + Lang.T(" Il a été créé par une version plus récente de l'application.",
                " It was created by a newer version of the application."));

        var result = new List<CodeEntry>();
        foreach (var item in items)
        {
            if (item is not JsonObject code || code["text"]?.GetValueKind() != JsonValueKind.String)
                throw new InvalidCodesFileException(InvalidMessage);
            var type = code["type"]?.GetValueKind() == JsonValueKind.String && code["type"]!.GetValue<string>() == "code128"
                ? CodeType.Code128
                : CodeType.Qr; // type inconnu ou absent : QR code
            result.Add(new CodeEntry(code["text"]!.GetValue<string>(), type));
        }
        string? name = obj["name"]?.GetValueKind() == JsonValueKind.String ? obj["name"]!.GetValue<string>() : null;
        return new CodesDocument(string.IsNullOrWhiteSpace(name) ? null : name, result);
    }

    public static CodesDocument Load(string path)
    {
        string json;
        try { json = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidCodesFileException(Lang.T($"Impossible de lire le fichier : {ex.Message}", $"Unable to read the file: {ex.Message}"), ex);
        }
        return Deserialize(json);
    }

    /// <summary>
    /// Écrit dans un fichier temporaire voisin, vidé sur le disque, puis le met à la place de l'ancien :
    /// jamais de fichier à moitié écrit, même en cas de coupure. Un fichier existant garde ses droits et attributs.
    /// </summary>
    public static void Save(string path, IEnumerable<CodeEntry> codes, string? name = null)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(Serialize(codes, name));
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(full)) File.Replace(temp, full, destinationBackupFileName: null);
            else File.Move(temp, full);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
