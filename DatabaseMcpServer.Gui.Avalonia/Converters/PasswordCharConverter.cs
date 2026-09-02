using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DatabaseMcpServer.Gui.Avalonia.Converters;

public sealed class PasswordCharConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? '●' : '\0';

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
