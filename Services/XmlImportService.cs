using System.IO;
using System.Xml.Linq;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Services;

public class XmlImportService
{
    public async Task<List<TagItem>> ImportTagsAsync(string xmlFilePath)
    {
        await using var stream = File.OpenRead(xmlFilePath);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);

        var items = document
            .Descendants()
            .Where(IsTagLikeElement)
            .Select(MapTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.NodeId))
            .ToList();

        if (items.Count == 0)
        {
            throw new InvalidDataException("未在 XML 中识别到有效变量节点，请检查字段是否包含 Name 和 NodeId。");
        }

        return items;
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
}
