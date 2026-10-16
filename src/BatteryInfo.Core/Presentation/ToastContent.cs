using System.Xml.Linq;

namespace BatteryInfo.Core.Presentation;

/// <summary>The XML of a Windows toast: a title line and a body, which may hold several lines.</summary>
public static class ToastContent
{
    public static string Build(string title, string text) =>
        new XElement("toast",
            new XElement("visual",
                new XElement("binding", new XAttribute("template", "ToastGeneric"),
                    new XElement("text", title),
                    new XElement("text", text)))).ToString(SaveOptions.DisableFormatting);
}
