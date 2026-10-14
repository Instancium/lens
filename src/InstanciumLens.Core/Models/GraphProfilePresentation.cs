using System;
using System.Globalization;
using System.Linq;

namespace InstanciumLens.Core.Models
{
    /// <summary>
    /// Guards profile card fields against placeholder echoes and non-emoji icon text,
    /// both at profile creation time and when rendering legacy manifests.
    /// </summary>
    public static class GraphProfilePresentation
    {
        public const string PlaceholderIcon = "a single appropriate emoji";
        public const string PlaceholderDisplayName = "human readable title in the user's language";
        public const string PlaceholderDescription = "short description of what this graph extracts";
        public const string FallbackIcon = "📊";

        private const int MaxIconTextElements = 2;

        public static string ResolveIcon(string? icon) =>
            string.IsNullOrWhiteSpace(icon) ||
            IsPlaceholder(icon, PlaceholderIcon) ||
            !IsSingleGrapheme(icon)
                ? FallbackIcon
                : icon.Trim();

        public static string ResolveDisplayName(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) || IsPlaceholder(value, PlaceholderDisplayName)
                ? fallback
                : value.Trim();

        public static string ResolveDescription(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) || IsPlaceholder(value, PlaceholderDescription)
                ? fallback
                : value.Trim();

        public static string TitleFromId(string profileId) =>
            string.Join(
                ' ',
                (profileId ?? string.Empty)
                    .Split('_', '-')
                    .Select(word => word.Length == 0
                        ? word
                        : char.ToUpperInvariant(word[0]) + word[1..]));

        private static bool IsPlaceholder(string value, string placeholder) =>
            string.Equals(value.Trim(), placeholder, StringComparison.OrdinalIgnoreCase);

        private static bool IsSingleGrapheme(string value) =>
            !value.Contains(' ') && new StringInfo(value.Trim()).LengthInTextElements <= MaxIconTextElements;
    }
}