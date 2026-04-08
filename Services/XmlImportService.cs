using System.IO;
using System.Xml.Linq;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class XmlImportService
{
    private sealed record UserDefElementInfo(string Name, string TypeName);
    private sealed record ArrayTypeInfo(int MinRange, int MaxRange, string BaseTypeName);

    public async Task<List<TagItem>> ImportTagsAsync(string xmlFilePath)
    {
        await using var stream = File.OpenRead(xmlFilePath);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);

        var items = IsSymbolConfiguration(document)
            ? ParseSymbolConfiguration(document)
            : document
                .Descendants()
                .Where(IsTagLikeElement)
                .Select(MapTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.NodeId))
                .ToList();

        if (items.Count == 0)
        {
            throw new InvalidDataException("未在 XML 中识别到有效变量节点，请检查字段是否包含 Name 和 NodeId，或确认是否为 Symbolconfiguration 导出文件。");
        }

        return items;
    }

    private static bool IsSymbolConfiguration(XDocument document) =>
        document.Root?.Name.LocalName.Equals("Symbolconfiguration", StringComparison.OrdinalIgnoreCase) == true;

    private static List<TagItem> ParseSymbolConfiguration(XDocument document)
    {
        var userDefs = BuildUserDefMap(document);
        var arrayTypes = BuildArrayTypeMap(document);
        var rootNodes = document
            .Descendants()
            .Where(element => element.Name.LocalName.Equals("NodeList", StringComparison.OrdinalIgnoreCase))
            .Elements()
            .Where(element => element.Name.LocalName.Equals("Node", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return rootNodes
            .SelectMany(rootNode => ExpandSymbolNode(rootNode, userDefs, arrayTypes))
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.NodeId))
            .GroupBy(tag => tag.NodeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static Dictionary<string, List<UserDefElementInfo>> BuildUserDefMap(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName.Equals("TypeUserDef", StringComparison.OrdinalIgnoreCase))
            .Select(element => new
            {
                Name = ReadValue(element, "name", "Name"),
                Elements = element.Elements()
                    .Where(child => child.Name.LocalName.Equals("UserDefElement", StringComparison.OrdinalIgnoreCase))
                    .Select(child => new UserDefElementInfo(
                        ReadValue(child, "iecname", "name", "Name"),
                        ReadValue(child, "type", "Type")))
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.TypeName))
                    .ToList()
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Elements.Count > 0)
            .ToDictionary(item => item.Name, item => item.Elements, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, ArrayTypeInfo> BuildArrayTypeMap(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName.Equals("TypeArray", StringComparison.OrdinalIgnoreCase))
            .Select(element =>
            {
                var dimension = element.Elements()
                    .FirstOrDefault(child => child.Name.LocalName.Equals("ArrayDim", StringComparison.OrdinalIgnoreCase));
                var minRange = int.TryParse(ReadValue(dimension ?? element, "minrange", "MinRange"), out var min) ? min : 0;
                var maxRange = int.TryParse(ReadValue(dimension ?? element, "maxrange", "MaxRange"), out var max) ? max : -1;
                return new
                {
                    Name = ReadValue(element, "name", "Name"),
                    Info = new ArrayTypeInfo(minRange, maxRange, ReadValue(element, "basetype", "BaseType"))
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                && !string.IsNullOrWhiteSpace(item.Info.BaseTypeName)
                && item.Info.MaxRange >= item.Info.MinRange)
            .ToDictionary(item => item.Name, item => item.Info, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<TagItem> ExpandSymbolNode(
        XElement element,
        IReadOnlyDictionary<string, List<UserDefElementInfo>> userDefs,
        IReadOnlyDictionary<string, ArrayTypeInfo> arrayTypes)
    {
        var childNodes = element.Elements()
            .Where(child => child.Name.LocalName.Equals("Node", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (childNodes.Count > 0)
        {
            foreach (var childNode in childNodes)
            {
                foreach (var childTag in ExpandSymbolNode(childNode, userDefs, arrayTypes))
                {
                    yield return childTag;
                }
            }

            yield break;
        }

        var typeName = ReadValue(element, "type", "Type", "DataType");
        if (ShouldExpandType(typeName, userDefs, arrayTypes))
        {
            foreach (var expandedTag in ExpandStructuredType(element, typeName, userDefs, arrayTypes, 0))
            {
                yield return expandedTag;
            }

            yield break;
        }

        if (IsSymbolNodeCandidate(element))
        {
            yield return MapSymbolNode(element);
        }
    }

    private static bool ShouldExpandType(
        string typeName,
        IReadOnlyDictionary<string, List<UserDefElementInfo>> userDefs,
        IReadOnlyDictionary<string, ArrayTypeInfo> arrayTypes)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        if (typeName.Contains("Cylinder", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return arrayTypes.TryGetValue(typeName, out var arrayInfo)
            && arrayInfo.BaseTypeName.Contains("Cylinder", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<TagItem> ExpandStructuredType(
        XElement sourceElement,
        string typeName,
        IReadOnlyDictionary<string, List<UserDefElementInfo>> userDefs,
        IReadOnlyDictionary<string, ArrayTypeInfo> arrayTypes,
        int depth,
        string? explicitPath = null)
    {
        if (depth > 8)
        {
            yield break;
        }

        var currentPath = explicitPath ?? BuildSymbolPath(sourceElement);
        var access = ReadValue(sourceElement, "access", "Access", "Direction");
        var directAddress = ReadValue(sourceElement, "directaddress", "Address");
        var comment = ReadValue(sourceElement, "Comment", "Description", "Remark");

        if (arrayTypes.TryGetValue(typeName, out var arrayInfo))
        {
            var itemCount = arrayInfo.MaxRange - arrayInfo.MinRange + 1;
            if (itemCount <= 0 || itemCount > 128)
            {
                yield break;
            }

            for (var index = arrayInfo.MinRange; index <= arrayInfo.MaxRange; index++)
            {
                var indexedPath = $"{currentPath}[{index}]";
                foreach (var tag in ExpandStructuredType(sourceElement, arrayInfo.BaseTypeName, userDefs, arrayTypes, depth + 1, indexedPath))
                {
                    yield return tag;
                }
            }

            yield break;
        }

        if (userDefs.TryGetValue(typeName, out var elements))
        {
            foreach (var element in elements)
            {
                var childPath = $"{currentPath}.{element.Name}";
                if (arrayTypes.ContainsKey(element.TypeName) || userDefs.ContainsKey(element.TypeName))
                {
                    foreach (var childTag in ExpandStructuredType(sourceElement, element.TypeName, userDefs, arrayTypes, depth + 1, childPath))
                    {
                        yield return childTag;
                    }
                }
                else
                {
                    yield return CreateStructuredLeafTag(childPath, element.TypeName, access, directAddress, comment);
                }
            }

            yield break;
        }

        yield return CreateStructuredLeafTag(currentPath, typeName, access, directAddress, comment);
    }

    private static TagItem CreateStructuredLeafTag(string fullPath, string dataType, string access, string directAddress, string comment)
    {
        var normalizedPath = fullPath.Replace("Application.", string.Empty, StringComparison.OrdinalIgnoreCase);
        var category = normalizedPath.Contains(".CylCtrl[", StringComparison.OrdinalIgnoreCase) || normalizedPath.Contains(".VacCtrl[", StringComparison.OrdinalIgnoreCase)
            ? "Cylinder"
            : normalizedPath.Split('.').Skip(1).FirstOrDefault() ?? normalizedPath.Split('.').FirstOrDefault() ?? "General";

        var descriptionParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(comment))
        {
            descriptionParts.Add(comment);
        }

        if (!string.IsNullOrWhiteSpace(directAddress))
        {
            descriptionParts.Add($"地址: {directAddress}");
        }

        descriptionParts.Add($"符号路径: {fullPath}");

        return new TagItem
        {
            Name = normalizedPath,
            NodeId = fullPath,
            DataType = dataType,
            Category = category,
            Group = GetGroupFromPath(normalizedPath),
            Direction = NormalizeAccess(access),
            Description = string.Join(" | ", descriptionParts),
            IsAlarm = normalizedPath.Contains("Alarm", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.Contains("Fault", StringComparison.OrdinalIgnoreCase),
            IsWritable = access.Contains("Write", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string BuildSymbolPath(XElement element)
    {
        var pathSegments = element
            .AncestorsAndSelf()
            .Where(node => node.Name.LocalName.Equals("Node", StringComparison.OrdinalIgnoreCase))
            .Select(node => ReadValue(node, "name", "Name"))
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(".", pathSegments);
    }

    private static string GetGroupFromPath(string normalizedPath)
    {
        var segments = normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3)
        {
            return string.Join(".", segments.Take(segments.Length - 1));
        }

        return segments.FirstOrDefault() ?? "Default";
    }

    private static bool IsTagLikeElement(XElement element)
    {
        var elementName = element.Name.LocalName;
        if (elementName.Equals("Tag", StringComparison.OrdinalIgnoreCase)
            || elementName.Equals("Variable", StringComparison.OrdinalIgnoreCase)
            || elementName.Equals("Item", StringComparison.OrdinalIgnoreCase)
            || elementName.Equals("TagItem", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasAnyValue(element, "Name", "TagName", "VariableName")
            && HasAnyValue(element, "NodeId", "NodeID", "Address", "Id");
    }

    private static bool IsSymbolNodeCandidate(XElement element)
    {
        if (!element.Name.LocalName.Equals("Node", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(ReadValue(element, "type", "Type"))
            || !string.IsNullOrWhiteSpace(ReadValue(element, "directaddress", "Address"))
            || element.Elements().All(child => !child.Name.LocalName.Equals("Node", StringComparison.OrdinalIgnoreCase));
    }

    private static TagItem MapTag(XElement element) => new()
    {
        Name = ReadValue(element, "Name", "TagName", "VariableName"),
        NodeId = ReadValue(element, "NodeId", "NodeID", "Address", "Id"),
        DataType = ReadValue(element, new[] { "DataType", "Type", "ValueType", "VarType" }, "String"),
        Category = ReadValue(element, new[] { "Category", "Area" }, "General"),
        Group = ReadValue(element, new[] { "Group", "Section" }, "Default"),
        Direction = ReadValue(element, new[] { "Direction", "Access" }, "Input"),
        Description = ReadValue(element, "Description", "Comment", "Remark"),
        IsAlarm = ReadBoolValue(element, "IsAlarm", "Alarm", "EnableAlarm"),
        IsWritable = ReadBoolValue(element, "IsWritable", "Writable", "CanWrite", "Writeable")
    };

    private static TagItem MapSymbolNode(XElement element)
    {
        var pathSegments = BuildSymbolPath(element)
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var fullPath = string.Join(".", pathSegments);
        var parentPath = string.Join(".", pathSegments.Take(Math.Max(0, pathSegments.Count - 1)));
        var access = ReadValue(element, "access", "Access", "Direction");
        var comment = ReadValue(element, "Comment", "Description", "Remark");
        var directAddress = ReadValue(element, "directaddress", "Address");
        var dataType = ReadValue(element, new[] { "type", "Type", "DataType" }, "String");

        var descriptionParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(comment))
        {
            descriptionParts.Add(comment);
        }

        if (!string.IsNullOrWhiteSpace(directAddress))
        {
            descriptionParts.Add($"地址: {directAddress}");
        }

        descriptionParts.Add($"符号路径: {fullPath}");

        return new TagItem
        {
            Name = pathSegments.LastOrDefault() ?? string.Empty,
            NodeId = fullPath,
            DataType = dataType,
            Category = pathSegments.Skip(1).FirstOrDefault() ?? pathSegments.FirstOrDefault() ?? "General",
            Group = string.IsNullOrWhiteSpace(parentPath) ? "Default" : parentPath,
            Direction = NormalizeAccess(access),
            Description = string.Join(" | ", descriptionParts),
            IsAlarm = fullPath.Contains("Alarm", StringComparison.OrdinalIgnoreCase)
                || fullPath.Contains("Fault", StringComparison.OrdinalIgnoreCase)
                || comment.Contains("报警", StringComparison.OrdinalIgnoreCase)
                || comment.Contains("急停", StringComparison.OrdinalIgnoreCase),
            IsWritable = access.Contains("Write", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool HasAnyValue(XElement element, params string[] names) =>
        !string.IsNullOrWhiteSpace(ReadValue(element, names));

    private static string ReadValue(XElement element, params string[] names) =>
        ReadValue(element, names, string.Empty);

    private static string ReadValue(XElement element, string[] names, string defaultValue)
    {
        foreach (var name in names)
        {
            var attr = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (attr is not null && !string.IsNullOrWhiteSpace(attr.Value))
            {
                return attr.Value.Trim();
            }

            var child = element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (child is not null && !string.IsNullOrWhiteSpace(child.Value))
            {
                return child.Value.Trim();
            }
        }

        return defaultValue;
    }

    private static bool ReadBoolValue(XElement element, params string[] names)
    {
        var raw = ReadValue(element, names);
        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAccess(string access)
    {
        if (string.IsNullOrWhiteSpace(access))
        {
            return "Input";
        }

        return access.Equals("ReadWrite", StringComparison.OrdinalIgnoreCase)
            ? "ReadWrite"
            : access.Equals("Read", StringComparison.OrdinalIgnoreCase)
                ? "Input"
                : access.Trim();
    }
}
