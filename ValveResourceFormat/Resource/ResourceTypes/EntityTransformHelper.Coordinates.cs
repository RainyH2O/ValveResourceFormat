using System.Globalization;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes
{
    public static partial class EntityTransformHelper
    {
        /// <summary>
        /// Parses three finite coordinates separated or surrounded by whitespace, punctuation, or symbols.
        /// Signs and decimal points remain part of each number.
        /// </summary>
        /// <param name="input">The coordinate text.</param>
        /// <param name="value">The parsed coordinates, or zero when parsing fails.</param>
        /// <returns>Whether the input contains exactly three finite numbers.</returns>
        public static bool TryParseCoordinates(string input, out Vector3 value)
        {
            value = default;

            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            var normalizedInput = string.Concat(input.Select(static character =>
                (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character))
                    && character is not '+' and not '-' and not '.'
                    ? ' '
                    : character));
            var split = normalizedInput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (split.Length != 3
                || !float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.TryParse(split[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                || !float.IsFinite(x)
                || !float.IsFinite(y)
                || !float.IsFinite(z))
            {
                return false;
            }

            value = new Vector3(x, y, z);
            return true;
        }
    }
}
