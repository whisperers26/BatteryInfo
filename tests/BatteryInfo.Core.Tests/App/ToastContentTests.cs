using System.Xml.Linq;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class ToastContentTests
{
    private static string[] Texts(string xml) =>
        [.. XDocument.Parse(xml).Descendants("text").Select(t => t.Value)];

    [Fact]
    public void Title_then_text_in_a_generic_binding()
    {
        var toast = XDocument.Parse(ToastContent.Build("Low battery", "Mouse is at 15 %")).Root!;

        Assert.Equal("toast", toast.Name.LocalName);
        Assert.Equal("ToastGeneric", toast.Element("visual")!.Element("binding")!.Attribute("template")!.Value);
        Assert.Equal(["Low battery", "Mouse is at 15 %"], Texts(toast.ToString()));
    }

    [Fact]
    public void Several_lines_stay_in_one_text_element()
    {
        var xml = ToastContent.Build("Low battery", "Left earbud is at 20 %\nRight earbud is at 18 %");

        Assert.Equal(["Low battery", "Left earbud is at 20 %\nRight earbud is at 18 %"], Texts(xml));
    }

    [Fact]
    public void Markup_in_names_is_escaped()
    {
        var xml = ToastContent.Build("Low <battery>", "Tom & Jerry's \"mouse\" is at 5 %");

        Assert.Equal(["Low <battery>", "Tom & Jerry's \"mouse\" is at 5 %"], Texts(xml));
    }
}
