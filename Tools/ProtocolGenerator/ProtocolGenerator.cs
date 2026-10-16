using System.Text;
using System.Text.RegularExpressions;

namespace ProtocolGeneratorTool;

internal sealed class OpcodeInfo
{
    public required string Name { get; init; }
    public required int Opcode { get; init; }
}

internal static partial class ProtocolGenerator
{
    private static readonly char[] SplitChars = [' ', '\t'];

    public static void Generate(ProtocolGenerationOptions options)
    {
        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {options.SourceDirectory}");
        }

        Dictionary<string, string> clientFiles = new(StringComparer.Ordinal);
        Dictionary<string, string> serverFiles = new(StringComparer.Ordinal);
        HashSet<int> assignedOpcodes = [];

        string[] protoFiles = Directory.GetFiles(options.SourceDirectory, "*.proto", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        if (protoFiles.Length == 0)
        {
            throw new InvalidOperationException($"No protocol files found in source directory: {options.SourceDirectory}");
        }

        foreach (string protoPath in protoFiles)
        {
            ProtocolFileName fileName = ParseProtocolFileName(protoPath);
            string result = ConvertProtocolFile(protoPath, fileName.ProtocolName, fileName.StartOpcode, assignedOpcodes);
            string outputName = $"{fileName.FileName}.cs";

            if (fileName.Direction.Contains('C', StringComparison.Ordinal))
            {
                AddGeneratedFile(clientFiles, outputName, result, "Client");
                AddGeneratedFile(serverFiles, outputName, result, "Server");
            }

            if (fileName.Direction.Contains('S', StringComparison.Ordinal))
            {
                AddGeneratedFile(serverFiles, outputName, result, "Server");
            }
        }

        string? tempRoot = null;
        try
        {
            tempRoot = Path.Combine(Path.GetTempPath(), $"FirepowerLoop.ProtocolGenerator.{Guid.NewGuid():N}");
            string tempClient = Path.Combine(tempRoot, "Client");
            string tempServer = Path.Combine(tempRoot, "Server");

            WriteGeneratedSet(tempClient, clientFiles);
            WriteGeneratedSet(tempServer, serverFiles);

            List<string> touchedFiles = [];
            try
            {
                CommitGeneratedSet(tempClient, options.ClientOutputDirectory, clientFiles.Keys, touchedFiles);
                CommitGeneratedSet(tempServer, options.ServerOutputDirectory, serverFiles.Keys, touchedFiles);
            }
            catch (Exception e)
            {
                string touched = touchedFiles.Count == 0 ? "none" : string.Join(", ", touchedFiles);
                throw new IOException($"Commit failed after touching files: {touched}", e);
            }
        }
        finally
        {
            if (tempRoot is not null && Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static ProtocolFileName ParseProtocolFileName(string protoPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(protoPath);
        string[] parts = fileName.Split('_');
        if (parts.Length != 3)
        {
            throw new FormatException($"Protocol file name must be <Name>_<C|S|CS>_<StartOpcode>.proto: {Path.GetFileName(protoPath)}");
        }

        string direction = parts[1];
        if (direction is not ("C" or "S" or "CS"))
        {
            throw new FormatException($"Protocol file direction must be C, S, or CS: {Path.GetFileName(protoPath)}");
        }

        if (!int.TryParse(parts[2], out int startOpcode) || startOpcode < 0 || startOpcode > ushort.MaxValue)
        {
            throw new FormatException($"Protocol file start opcode is invalid: {Path.GetFileName(protoPath)}");
        }

        return new ProtocolFileName(fileName, parts[0], direction, startOpcode);
    }

    private static string ConvertProtocolFile(string protoPath, string protoName, int startOpcode, HashSet<int> assignedOpcodes)
    {
        List<OpcodeInfo> msgOpcode = [];
        string source = File.ReadAllText(protoPath);

        StringBuilder sb = new();
        sb.Append("using MemoryPack;\n");
        sb.Append("using System.Collections.Generic;\n\n");
        sb.Append($"namespace ET\n");
        sb.Append("{\n");

        bool isMsgStart = false;
        string msgName = "";
        string responseType = "";
        StringBuilder sbDispose = new();
        Regex responseTypeRegex = ResponseTypeRegex();
        // 显式编号保留已发布消息的身份；删掉中间消息不会重排其余协议。
        int? explicitOpcode = null;
        foreach (string line in source.Split('\n'))
        {
            string newline = line.Trim();
            if (string.IsNullOrEmpty(newline))
            {
                continue;
            }

            if (!isMsgStart && newline.StartsWith("// Opcode ", StringComparison.Ordinal))
            {
                if (explicitOpcode.HasValue || !int.TryParse(newline[10..].Trim(), out int parsed) || parsed <= 0 || parsed > ushort.MaxValue)
                    throw new FormatException($"Invalid or repeated Opcode declaration in {Path.GetFileName(protoPath)}: {newline}");
                explicitOpcode = parsed;
                continue;
            }

            if (responseTypeRegex.IsMatch(newline))
            {
                responseType = responseTypeRegex.Replace(newline, string.Empty);
                responseType = responseType.Trim().Split(' ')[0].TrimEnd('\r', '\n');
                continue;
            }

            if (!isMsgStart && newline.StartsWith("//", StringComparison.Ordinal))
            {
                if (newline.StartsWith("///", StringComparison.Ordinal))
                {
                    sb.Append("\t/// <summary>\n");
                    sb.Append($"\t/// {newline.TrimStart('/', ' ')}\n");
                    sb.Append("\t/// </summary>\n");
                }
                else
                {
                    sb.Append($"\t// {newline.TrimStart('/', ' ')}\n");
                }

                continue;
            }

            if (newline.StartsWith("message", StringComparison.Ordinal))
            {
                isMsgStart = true;

                string parentClass = "";
                msgName = newline.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries)[1];
                string[] ss = newline.Split(["//"], StringSplitOptions.RemoveEmptyEntries);
                if (ss.Length == 2)
                {
                    parentClass = ss[1].Trim();
                }

                int opcode = explicitOpcode ?? (startOpcode + 1);
                explicitOpcode = null;
                startOpcode = opcode;
                if (opcode > ushort.MaxValue)
                {
                    throw new FormatException($"Opcode exceeds ushort range in {Path.GetFileName(protoPath)}.");
                }

                if (!assignedOpcodes.Add(opcode))
                    throw new FormatException($"Duplicate Opcode {opcode} for {msgName} in {Path.GetFileName(protoPath)}.");

                msgOpcode.Add(new OpcodeInfo() { Name = msgName, Opcode = opcode });

                sb.Append($"\t[MemoryPackable]\n");
                sb.Append($"\t[Message({protoName}.{msgName})]\n");
                if (!string.IsNullOrEmpty(responseType))
                {
                    sb.Append($"\t[ResponseType(nameof({responseType}))]\n");
                }

                sb.Append($"\tpublic partial class {msgName} : MessageObject");

                if (parentClass is "IActorMessage" or "IActorRequest" or "IActorResponse")
                {
                    sb.Append($", {parentClass}\n");
                }
                else if (parentClass != "")
                {
                    sb.Append($", {parentClass}\n");
                }
                else
                {
                    sb.Append('\n');
                }

                continue;
            }

            if (isMsgStart)
            {
                if (newline.StartsWith('{'))
                {
                    sbDispose.Clear();
                    sb.Append("\t{\n");
                    sb.Append($"\t\tpublic static {msgName} Create(bool isFromPool = false)\n\t\t{{\n\t\t\treturn ObjectPool.Instance.Fetch(typeof({msgName}), isFromPool) as {msgName};\n\t\t}}\n\n");
                    continue;
                }

                if (newline.StartsWith('}'))
                {
                    isMsgStart = false;
                    responseType = "";

                    if (!newline.Contains("// no dispose", StringComparison.Ordinal))
                    {
                        sb.Append($"\t\tpublic override void Dispose()\n\t\t{{\n\t\t\tif (!this.IsFromPool)\n\t\t\t{{\n\t\t\t\treturn;\n\t\t\t}}\n\n\t\t\t{sbDispose.ToString().TrimEnd('\t')}\n\t\t\tObjectPool.Instance.Recycle(this);\n\t\t}}\n");
                    }

                    sb.Append("\t}\n\n");
                    continue;
                }

                if (newline.StartsWith("//", StringComparison.Ordinal))
                {
                    sb.Append("\t\t/// <summary>\n");
                    sb.Append($"\t\t/// {newline.TrimStart('/', ' ')}\n");
                    sb.Append("\t\t/// </summary>\n");
                    continue;
                }

                string memberStr;
                if (newline.Contains("//", StringComparison.Ordinal))
                {
                    string[] lineSplit = newline.Split("//");
                    memberStr = lineSplit[0].Trim();
                    sb.Append("\t\t/// <summary>\n");
                    sb.Append($"\t\t/// {lineSplit[1].Trim()}\n");
                    sb.Append("\t\t/// </summary>\n");
                }
                else
                {
                    memberStr = newline;
                }

                if (memberStr.StartsWith("map<", StringComparison.Ordinal))
                {
                    Map(sb, memberStr, sbDispose);
                }
                else if (memberStr.StartsWith("repeated", StringComparison.Ordinal))
                {
                    Repeated(sb, memberStr, sbDispose);
                }
                else
                {
                    Members(sb, memberStr, sbDispose);
                }
            }
        }

        if (explicitOpcode.HasValue)
            throw new FormatException($"Opcode declaration has no message in {Path.GetFileName(protoPath)}.");

        sb.Append("\tpublic static class " + protoName + "\n\t{\n");
        foreach (OpcodeInfo info in msgOpcode)
        {
            sb.Append($"\t\tpublic const ushort {info.Name} = {info.Opcode};\n");
        }

        sb.Append("\t}\n");

        sb.Append('}');

        sb.Replace("\t", "    ");
        return sb.ToString().ReplaceLineEndings("\r\n");
    }

    private static void AddGeneratedFile(Dictionary<string, string> files, string fileName, string content, string setName)
    {
        if (!files.TryAdd(fileName, content))
        {
            throw new InvalidOperationException($"Duplicate generated file in {setName}: {fileName}");
        }
    }

    private static void WriteGeneratedSet(string directory, Dictionary<string, string> files)
    {
        Directory.CreateDirectory(directory);
        foreach ((string fileName, string content) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string path = Path.Combine(directory, fileName);
            ValidateTopLevelFile(directory, path);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void CommitGeneratedSet(string tempDirectory, string outputDirectory, IEnumerable<string> generatedFiles, List<string> touchedFiles)
    {
        Directory.CreateDirectory(outputDirectory);

        HashSet<string> generated = new(generatedFiles, StringComparer.Ordinal);
        foreach (string fileName in generated.Order(StringComparer.Ordinal))
        {
            string source = Path.Combine(tempDirectory, fileName);
            string target = Path.Combine(outputDirectory, fileName);
            ValidateTopLevelFile(outputDirectory, target);
            File.Copy(source, target, overwrite: true);
            touchedFiles.Add(target);
        }

        foreach (string oldFile in Directory.GetFiles(outputDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(oldFile);
            if (!generated.Contains(fileName))
            {
                ValidateTopLevelFile(outputDirectory, oldFile);
                File.Delete(oldFile);
                touchedFiles.Add(oldFile);
            }
        }
    }

    private static void ValidateTopLevelFile(string directory, string filePath)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(fullPath) is not string parent ||
            !string.Equals(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Generated file must be top-level within output directory: {filePath}");
        }
    }

    private static void Map(StringBuilder sb, string newline, StringBuilder sbDispose)
    {
        int start = newline.IndexOf('<') + 1;
        int end = newline.IndexOf('>');
        string types = newline.Substring(start, end - start);
        string[] ss = types.Split(',');
        string keyType = ConvertType(ss[0].Trim());
        string valueType = ConvertType(ss[1].Trim());
        string tail = newline[(end + 1)..];
        ss = tail.Trim().Replace(";", "").Split(' ');
        string v = ss[0];
        int n = int.Parse(ss[2]);

        sb.Append("\t\t[MongoDB.Bson.Serialization.Attributes.BsonDictionaryOptions(MongoDB.Bson.Serialization.Options.DictionaryRepresentation.ArrayOfArrays)]\n");
        sb.Append($"\t\t[MemoryPackOrder({n - 1})]\n");
        sb.Append($"\t\tpublic Dictionary<{keyType}, {valueType}> {v} {{ get; set; }} = new();\n");

        sbDispose.Append($"this.{v}.Clear();\n\t\t\t");
    }

    private static void Repeated(StringBuilder sb, string newline, StringBuilder sbDispose)
    {
        int index = newline.IndexOf(';');
        if (index < 0)
        {
            throw new FormatException($"Invalid repeated field: {newline}");
        }

        newline = newline.Remove(index);
        string[] ss = newline.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries);
        string type = ss[1];
        type = ConvertType(type);
        string name = ss[2];
        int n = int.Parse(ss[4]);

        sb.Append($"\t\t[MemoryPackOrder({n - 1})]\n");
        sb.Append($"\t\tpublic List<{type}> {name} {{ get; set; }} = new();\n\n");

        sbDispose.Append($"this.{name}.Clear();\n\t\t\t");
    }

    private static string ConvertType(string type)
    {
        return type switch
        {
            "int16" => "short",
            "int32" => "int",
            "bytes" => "byte[]",
            "uint32" => "uint",
            "long" => "long",
            "int64" => "long",
            "uint64" => "ulong",
            "uint16" => "ushort",
            _ => type
        };
    }

    private static void Members(StringBuilder sb, string newline, StringBuilder sbDispose)
    {
        int index = newline.IndexOf(';');
        if (index < 0)
        {
            throw new FormatException($"Invalid field: {newline}");
        }

        newline = newline.Remove(index);
        string[] ss = newline.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries);
        string type = ss[0];
        string name = ss[1];
        int n = int.Parse(ss[3]);
        string typeCs = ConvertType(type);

        sb.Append($"\t\t[MemoryPackOrder({n - 1})]\n");
        sb.Append($"\t\tpublic {typeCs} {name} {{ get; set; }}\n\n");

        switch (typeCs)
        {
            case "bytes":
            {
                break;
            }
            default:
                sbDispose.Append($"this.{name} = default;\n\t\t\t");
                break;
        }
    }

    [GeneratedRegex(@"//\s*ResponseType")]
    private static partial Regex ResponseTypeRegex();

    private sealed record ProtocolFileName(string FileName, string ProtocolName, string Direction, int StartOpcode);
}
