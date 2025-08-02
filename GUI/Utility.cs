#define DEBUG_MISSING_LOCALIZATIONS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows.Input;

using GARbro.GUI.Strings;

namespace GARbro
{
    #region  Native Methods
    internal class NativeMethods
    {
        public static bool IsWindowsVistaOrLater
        {
            get
            {
                return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version >= new Version (6, 0, 6000);
            }
        }

        [DllImport ("shlwapi.dll", CharSet = CharSet.Unicode)]
        internal static extern int StrCmpLogicalW (string psz1, string psz2);

        [DllImport ("gdi32.dll")]
        internal static extern int GetDeviceCaps (IntPtr hDc, int nIndex);

        [DllImport ("user32.dll")]
        internal static extern IntPtr GetDC (IntPtr hWnd);

        [DllImport ("user32.dll")]
        internal static extern int ReleaseDC (IntPtr hWnd, IntPtr hDc);

        [DllImport ("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr GetActiveWindow();

        [DllImport ("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow (IntPtr hWnd, int nCmdShow);

        [DllImport ("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnableWindow (IntPtr hWnd, bool bEnable);
    }

    public static class Desktop
    {
        public static int DpiX { get { return dpi_x; } }
        public static int DpiY { get { return dpi_y; } }
        
        public const int LOGPIXELSX = 88;
        public const int LOGPIXELSY = 90;

        private static int dpi_x = GetCaps (LOGPIXELSX);
        private static int dpi_y = GetCaps (LOGPIXELSY);

        public static int GetCaps (int cap)
        {
            IntPtr hdc = NativeMethods.GetDC (IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return 96;
            int dpi = NativeMethods.GetDeviceCaps (hdc, cap);
            NativeMethods.ReleaseDC (IntPtr.Zero, hdc);
            return dpi;
        }
    }

    public sealed class NumericStringComparer : IComparer<string>
    {
        public int Compare (string a, string b)
        {
            return NativeMethods.StrCmpLogicalW (a, b);
        }
    }

    public class WaitCursor : IDisposable
    {
        private Cursor m_previousCursor;

        public WaitCursor()
        {
            m_previousCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
        }

        #region IDisposable Members
        bool disposed = false;
        public void Dispose()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                Mouse.OverrideCursor = m_previousCursor;
                disposed = true;
            }
        }
        #endregion
    }
    #endregion

#pragma warning disable CS0162,CS0168
    public static class Localization
    {
        /// <summary>
        /// Gets or sets the ResourceManager used for string localization.<br/>
        /// Defaults to <b>guiStrings.ResourceManager</b> if not specified.
        /// </summary>
        public static ResourceManager ResourceManager { get; set; } = guiStrings.ResourceManager;

        private static readonly ConcurrentDictionary<string, bool> _hasCultureResources = new ConcurrentDictionary<string, bool>();

        /// <summary>
        /// Pluralization rules for different cultures.<br/>
        /// Each rule returns a suffix (1, 2, or 3) based on the count value.
        /// </summary>
        private static readonly Dictionary<string, Func<int, string>> PluralizationRules = new Dictionary<string, Func<int, string>> ()
        {
            ["be-BY"] = GetCyrillciPluralizationSuffix,
            ["cs-CZ"] = GetCzechPluralizationSuffix,
            ["de-DE"] = GetEnglishPluralizationSuffix,
            ["en-GB"] = GetEnglishPluralizationSuffix,
            ["en-US"] = GetEnglishPluralizationSuffix,
            ["es-ES"] = GetEnglishPluralizationSuffix,
            ["fr-FR"] = GetFrenchPluralizationSuffix,
            ["pl-PL"] = GetPolishPluralizationSuffix,
            ["ru-RU"] = GetCyrillciPluralizationSuffix,
            ["sk-SK"] = GetCzechPluralizationSuffix,
            ["uk-UA"] = GetCyrillciPluralizationSuffix
        };

        /// <summary>
        /// Returns the appropriate pluralized string for the given count and message identifier.<br/>
        /// Supports multiple languages with fallback strategies for missing resources.
        /// </summary>
        /// <param name="count">The count value that determines pluralization form.</param>
        /// <param name="messageId">The base message identifier to look up in resources.</param>
        /// <returns>
        /// The localized pluralized string, or the original messageId if no resource is found.
        /// </returns>
        /// <example>
        /// <code>
        /// // For Russian: item_count1 = "элемент", item_count2 = "элемента", item_count3 = "элементов"
        /// var result = Plural(5, "item_count"); // Returns "элементов"
        /// </code>
        /// </example>
        public static string Plural(int count, string messageId)
        {
            if (string.IsNullOrEmpty(messageId))
                return messageId ?? string.Empty;

            try
            {
                var suffix = GetPluralizationSuffix(count);
                var primaryKey = $"{messageId}{suffix}";

                return TryGetResource(primaryKey) ??
                       TryGetResource($"{messageId}1") ??  // Fallback to singular
                       TryGetResource(messageId) ??        // Fallback to base key
                       messageId;                          // Final fallback
            }
            catch (Exception ex)
            {
#if DEBUG_MISSING_LOCALIZATIONS
                Trace.WriteLine($"Error in pluralization for '{messageId}': {ex.Message}", "Localization.Plural");
#endif
            }
            return messageId;
        }

        private static string PluralRes(int count, string messageId)
        {
            if (string.IsNullOrEmpty(messageId))
                return messageId ?? string.Empty;

            var suffix = GetPluralizationSuffix(count);
            var primaryKey = $"{messageId}{suffix}";
            return primaryKey ?? $"{messageId}1" ?? messageId;
        }

        /// <inheritdoc cref = "Plural(int, string)" />
        public static string Pluralize(this int count, string messageId)
        {
            return Format(PluralRes(count, messageId), count);
        }

        private static string GetPluralizationSuffix(int count)
        {
            var culture = CultureInfo.CurrentUICulture.Name;

            if (PluralizationRules.TryGetValue(culture, out var rule))
            {
                if (!_hasCultureResources.TryGetValue(culture, out var hasResources))
                {
                    hasResources = CheckCultureHasSpecificResources();
                    _hasCultureResources[culture] = hasResources;
                }

                if (hasResources)
                    return rule(count);
            }

            return GetEnglishPluralizationSuffix(count);
        }

        private static bool CheckCultureHasSpecificResources()
        {
            try
            {
                var resourceSet = ResourceManager?.GetResourceSet(CultureInfo.CurrentUICulture, true, false);
                if (resourceSet != null)
                {
                    var invariantSet = ResourceManager?.GetResourceSet(CultureInfo.InvariantCulture, true, false);
                    if (resourceSet != invariantSet)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static string TryGetResource(string key)
        {
            try
            {
                var result = ResourceManager?.GetString(key);
#if DEBUG_MISSING_LOCALIZATIONS
                if (result == null)
                        Trace.WriteLine($"Missing string resource for '{key}' token", "Localization");
#endif
                return result;
            }
            catch (MissingManifestResourceException ex)
            {
#if DEBUG_MISSING_LOCALIZATIONS
                    Trace.WriteLine($"Resource manifest error for '{key}': {ex.Message}", "Localization");
#endif
                return null;
            }
        }

        #region Pluralization Rules

        /// <summary>
        /// English pluralization: 1 = singular, everything else = plural
        /// </summary>
        private static string GetEnglishPluralizationSuffix(int count)
        {
            return Math.Abs(count) == 1 ? "1" : "2";
        }

        /// <summary>
        /// French pluralization: 0 and 1 = singular, everything else = plural
        /// </summary>
        private static string GetFrenchPluralizationSuffix(int count)
        {
            var absCount = Math.Abs(count);
            return absCount == 0 || absCount == 1 ? "1" : "2";
        }

        /// <summary>
        /// Russian/Ukrainian/Belarusian pluralization rules:<br/>
        /// - 1, 21, 31, etc. (but not 11) = form 1<br/>
        /// - 2-4, 22-24, etc. (but not 12-14) = form 2<br/>
        /// - 0, 5-20, 25-30, etc. = form 3<br/>
        /// </summary>
        private static string GetCyrillciPluralizationSuffix(int count)
        {
            var absCount = Math.Abs(count);
            var lastDigit = absCount % 10;
            var lastTwoDigits = absCount % 100;

            if (lastDigit == 1 && lastTwoDigits != 11)
                return "1";

            if (lastDigit >= 2 && lastDigit <= 4 && (lastTwoDigits < 12 || lastTwoDigits > 14))
                return "2";

            return "3";
        }

        /// <summary>
        /// Polish pluralization rules (similar to other Cyrillic but count == 1 is special)
        /// </summary>
        private static string GetPolishPluralizationSuffix(int count)
        {
            if (count == 1)
                return "1";

            var absCount = Math.Abs(count);
            var lastDigit = absCount % 10;
            var lastTwoDigits = absCount % 100;

            if (lastDigit >= 2 && lastDigit <= 4 && (lastTwoDigits < 12 || lastTwoDigits > 14))
                return "2";

            return "3";
        }

        private static string GetCzechPluralizationSuffix(int count)
        {
            var absCount = Math.Abs(count);

            if (absCount == 1)
                return "1";

            if (absCount >= 2 && absCount <= 4)
                return "2";

            return "3";
        }

        #endregion

        /// <summary>
        /// Formats a string using named placeholders.
        /// </summary>
        /// <param name="msgText">The format string containing named placeholders like {name}, {date}, etc.</param>
        /// <param name="namedArgs">Named arguments as tuples of (name, value) pairs to replace placeholders.</param>
        /// <returns>
        /// The formatted string with placeholders replaced by corresponding values, 
        /// or unformatted message if formatting fails.
        /// </returns>
        /// <example>
        /// <code>
        /// var result = Format("Hello {name}, today is {date:dddd}", ("name", "Alice"), ("date", DateTime.Now));
        /// // Returns: "Hello Alice, today is Monday"
        /// </code>
        /// </example>
        public static string Format(string msgText, params (string name, object value)[] namedArgs)
        {
            try
            {
                // Replace named placeholders {name} with indexed {0}, {1}, ...
                var argsList = new List<object>();
                var localized_msg = TryGetResource(msgText);
                if (!string.IsNullOrEmpty(localized_msg))
                    msgText = localized_msg;

                // Process named arguments in order (to maintain {0}, {1}... order)
                for (int i = 0; i < namedArgs.Length; i++)
                {
                    var (name, value) = namedArgs[i];
                    argsList.Add(value);

                    // Replace {name} with {i} (e.g., {name} → {0})
                    msgText = msgText.Replace("{" + name + "}", "{" + i + "}");
                }

                return string.Format(msgText, argsList.ToArray());
            }
            catch (FormatException e)
            {
#if DEBUG_MISSING_LOCALIZATIONS
                    Trace.TraceError($"Localization format exception {msgText}", e.Message);
#endif
            }
            catch (Exception e)
            {
#if DEBUG_MISSING_LOCALIZATIONS
                    Trace.TraceError($"Localization exception {msgText}", e.Message);
#endif
            }
            return msgText;
        }

        /// <summary>
        /// Formats a string using indexed placeholders.
        /// </summary>
        /// <param name="msgText">The format string containing indexed placeholders like {0}, {1}, etc.</param>
        /// <param name="args">Positional arguments to replace indexed placeholders in order.</param>
        /// <returns>
        /// The formatted string with placeholders replaced by corresponding values, 
        /// or unformatted message if formatting fails.
        /// </returns>
        /// <example>
        /// <code>
        /// var result = Format("Hello {0}, today is {1:dddd}", "Bob", DateTime.Now);
        /// // Returns: "Hello Bob, today is Monday"
        /// </code>
        /// </example>
        public static string Format(string msgText, params object[] args)
        {
            try
            {
                var localized_msg = TryGetResource(msgText);
                if (!string.IsNullOrEmpty(localized_msg))
                    msgText = localized_msg;
                return string.Format(msgText, args);
            }
            catch (FormatException e)
            {
#if DEBUG_MISSING_LOCALIZATIONS
                    Trace.TraceError($"Localization format exception for {msgText}", e.Message);
#endif
            }
            catch (Exception e)
            {
#if DEBUG_MISSING_LOCALIZATIONS
                    Trace.TraceError($"Localization exception for {msgText}", e.Message);
#endif
            }
            return msgText;
        }


        /// <summary>
        /// Localizes a string or StringID.
        /// </summary>
        /// <param name="msgText">The original string or its ID to localize.</param>
        /// <returns>
        /// The localized string if found, original string if not.
        /// </returns>
        public static string Format(string msgText)
        {
            try
            {
                var localized_msg = TryGetResource(msgText);
                if (!string.IsNullOrEmpty(localized_msg))
                    msgText = localized_msg;
                return msgText;
            }
            catch (Exception e)
            {
#if DEBUG_MISSING_LOCALIZATIONS
                    Trace.TraceError($"Localization exception for {msgText}", e.Message);
#endif
            }
            return msgText;
        }

        /// <inheritdoc cref = "Format(string)" />
        public static string _T(string msgText)  // short alias
        {
            return Format(msgText);
        }
    }
#pragma warning restore CS0162,CS0168
}
