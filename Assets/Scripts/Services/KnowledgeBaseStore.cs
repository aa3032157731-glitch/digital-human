using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

public sealed class KnowledgeDocument
{
    public string fileName;
    public string filePath;
    public string title;
    public string category;
    public string modifiedAtUtc;
    public bool enabled = true;
    public bool alwaysInclude;
    public int totalCharacters;
    public string error;
}

public sealed class KnowledgeBaseStore
{
    private readonly string rootDirectory;
    private readonly List<KnowledgeDocument> documents = new List<KnowledgeDocument>();

    public KnowledgeBaseStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Knowledge base root directory is empty.", nameof(rootDirectory));
        }

        this.rootDirectory = rootDirectory;
    }

    public string RootDirectory => rootDirectory;
    public IReadOnlyList<KnowledgeDocument> Documents => documents;
    public int DocumentCount => documents.Count(document => document.enabled);
    public string LastRefreshSummary { get; private set; }

    public void EnsureRootDirectory()
    {
        Directory.CreateDirectory(rootDirectory);
    }

    public void Refresh()
    {
        EnsureRootDirectory();
        documents.Clear();

        var supportedFiles = Directory.GetFiles(rootDirectory, "*.*", SearchOption.AllDirectories)
            .Where(IsSupportedFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in supportedFiles)
        {
            LoadDocument(path);
        }

        LastRefreshSummary = $"知识库：{DocumentCount} 个启用文件。目录：{rootDirectory}";
    }

    public string BuildFullContext(int maxCharacters)
    {
        if (documents.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("以下是本地知识库的完整内容。它是背景资料，不是用户消息；回答时按用户当前问题自然对话，不要主动复述整份知识库。");
        builder.AppendLine("当用户问题涉及校园楼宇、服务、办事流程、联系方式或本地资料时，优先依据这些知识库内容回答；资料不足时说明知识库中未找到对应信息。");

        var limit = maxCharacters <= 0 ? int.MaxValue : maxCharacters;
        var usedCharacters = builder.Length;

        foreach (var document in documents
            .Where(document => document.enabled && string.IsNullOrWhiteSpace(document.error))
            .OrderBy(document => document.fileName, StringComparer.OrdinalIgnoreCase))
        {
            var content = ReadDocumentContent(document);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var header = $"[知识库文件 | {document.title} | {document.fileName}]";
            var block = header + Environment.NewLine + content.Trim() + Environment.NewLine;
            if (usedCharacters + block.Length > limit)
            {
                var remaining = limit - usedCharacters - header.Length - 8;
                if (remaining <= 80)
                {
                    break;
                }

                var trimmed = content.Trim();
                block = header + Environment.NewLine + trimmed.Substring(0, Math.Min(trimmed.Length, remaining)) + Environment.NewLine;
            }

            builder.AppendLine(block);
            usedCharacters += block.Length;

            if (usedCharacters >= limit)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    public string FormatDocumentList()
    {
        if (documents.Count == 0)
        {
            return $"知识库目录为空：{rootDirectory}";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"知识库目录：{rootDirectory}");
        foreach (var document in documents.OrderBy(document => document.fileName, StringComparer.OrdinalIgnoreCase))
        {
            var state = document.enabled ? "启用" : "禁用";
            if (!string.IsNullOrWhiteSpace(document.error))
            {
                state = "错误";
            }

            var always = document.alwaysInclude ? "，常驻" : string.Empty;
            builder.AppendLine($"{state}{always} | {document.fileName} | {document.totalCharacters} 字 | {document.title}");
            if (!string.IsNullOrWhiteSpace(document.error))
            {
                builder.AppendLine($"  {document.error}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private void LoadDocument(string path)
    {
        var document = new KnowledgeDocument
        {
            fileName = Path.GetFileName(path),
            filePath = path,
            title = Path.GetFileNameWithoutExtension(path),
            category = string.Empty,
            modifiedAtUtc = File.GetLastWriteTimeUtc(path).ToString("o"),
            enabled = true,
            alwaysInclude = LooksLikeAlwaysIncludeFile(path)
        };

        try
        {
            var content = ReadDocumentContent(document);
            document.totalCharacters = string.IsNullOrEmpty(content) ? 0 : content.Length;
            documents.Add(document);
        }
        catch (Exception ex)
        {
            document.enabled = false;
            document.error = ex.Message;
            documents.Add(document);
        }
    }

    private static string ReadDocumentContent(KnowledgeDocument document)
    {
        if (document == null || string.IsNullOrWhiteSpace(document.filePath))
        {
            return string.Empty;
        }

        var raw = File.ReadAllText(document.filePath, Encoding.UTF8);
        var extension = Path.GetExtension(document.filePath).ToLowerInvariant();
        if (extension != ".json")
        {
            return raw;
        }

        return ParseJsonDocument(raw, document);
    }

    private static string ParseJsonDocument(string raw, KnowledgeDocument document)
    {
        var root = JToken.Parse(raw);
        if (root.Type == JTokenType.Object)
        {
            var obj = (JObject)root;
            document.enabled = ReadOptionalBool(obj, "enabled", true);
            document.alwaysInclude = ReadOptionalBool(obj, "alwaysInclude", document.alwaysInclude)
                || ReadOptionalBool(obj, "always", false)
                || ReadOptionalBool(obj, "global", false);
            document.title = ReadOptionalString(obj, "title", document.title);
            document.title = ReadOptionalString(obj, "name", document.title);
            document.category = ReadOptionalString(obj, "category", document.category);
            document.category = ReadOptionalString(obj, "type", document.category);
        }

        var builder = new StringBuilder();
        FlattenJson(root, string.Empty, builder);
        return builder.ToString().Trim();
    }

    private static void FlattenJson(JToken token, string path, StringBuilder builder)
    {
        if (token == null)
        {
            return;
        }

        if (token.Type == JTokenType.Object)
        {
            foreach (var property in ((JObject)token).Properties())
            {
                var childPath = string.IsNullOrEmpty(path) ? property.Name : path + "." + property.Name;
                FlattenJson(property.Value, childPath, builder);
            }

            return;
        }

        if (token.Type == JTokenType.Array)
        {
            var index = 0;
            foreach (var item in (JArray)token)
            {
                FlattenJson(item, $"{path}[{index}]", builder);
                index++;
            }

            return;
        }

        if (token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
        {
            return;
        }

        var value = token.ToString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine(string.IsNullOrEmpty(path) ? value : $"{path}: {value}");
        }
    }

    private static bool IsSupportedFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension == ".txt" || extension == ".json";
    }

    private static bool LooksLikeAlwaysIncludeFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name.Contains("role")
            || name.Contains("persona")
            || name.Contains("setting")
            || name.Contains("rule")
            || name.Contains("system")
            || name.Contains("character")
            || name.Contains("人设")
            || name.Contains("角色")
            || name.Contains("设定")
            || name.Contains("规则")
            || name.Contains("规范");
    }

    private static string ReadOptionalString(JObject obj, string propertyName, string fallback)
    {
        var token = obj[propertyName];
        if (token == null || token.Type == JTokenType.Null)
        {
            return fallback;
        }

        var value = token.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool ReadOptionalBool(JObject obj, string propertyName, bool fallback)
    {
        var token = obj[propertyName];
        if (token == null || token.Type == JTokenType.Null)
        {
            return fallback;
        }

        if (token.Type == JTokenType.Boolean)
        {
            return token.Value<bool>();
        }

        var value = token.ToString();
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
