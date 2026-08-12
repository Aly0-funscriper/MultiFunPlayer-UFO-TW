using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace MultiFunPlayer.Common;
#pragma warning restore IDE0130 // Namespace does not match folder structure

public static class UIUtils
{
    public static UIElement CreateViewFromStream(Stream stream) => XamlReader.Load(stream) as UIElement;

    public static UIElement CreateViewFromFile(FileInfo file) => CreateViewFromFile(file.FullName);
    public static UIElement CreateViewFromFile(string path)
    {
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        return CreateViewFromStream(stream);
    }

    public static UIElement CreateViewFromString(string xamlContent)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xamlContent));
        return CreateViewFromStream(stream);
    }
}
