using System.Globalization;
using FluentValidation;

namespace NeuralDamage.Application.Validators;

/// <summary>
/// Pins the API to one culture. FluentValidation otherwise answers in the
/// server's language - German on a German machine - and the app is English.
/// </summary>
public static class ValidationCulture
{
    public static void Pin()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.GetCultureInfo("en");
    }
}
