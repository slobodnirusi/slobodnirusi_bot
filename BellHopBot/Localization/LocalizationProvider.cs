using System.Globalization;
using BellHopBot.Resources;
using Microsoft.Extensions.Localization;

namespace BellHopBot.Localization;

internal class LocalizationProvider(IStringLocalizer<Messages> localizer)
{
    public string Value(string name, string culture)
    {
        var cultureInfo = new CultureInfo(culture);
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;
        
        return localizer[name];
    }
}