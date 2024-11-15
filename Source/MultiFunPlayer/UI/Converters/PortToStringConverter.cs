using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public sealed partial class PortToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            int and 0 => "",
            int port => port.ToString(),
            _ => null
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s)
            return 0;

        var match = PortRegex.Match(s);
        if (!match.Success)
            return 0;

        if (int.TryParse(match.Groups["port"].Value, out var port))
            return Math.Clamp(port, 0, 65535);

        return 0;
    }

    [GeneratedRegex(@".*?(?<port>\d+).*")]
    private static partial Regex PortRegex { get; }
}
