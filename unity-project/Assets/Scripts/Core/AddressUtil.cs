// AddressUtil.cs - Helpers for displaying darknet addresses safely
// Darknet addresses are 64 hex characters, but anything that arrives over the
// network (or from a corrupted save) may be shorter or null. Slicing with [..8]
// on such input throws, so every log line and channel id goes through here.

namespace DaemonVision.Core
{
    public static class AddressUtil
    {
        public const int DefaultPrefixLength = 8;

        /// <summary>
        /// First <paramref name="length"/> characters of an address, or the whole
        /// address when it is shorter. Never throws; null becomes an empty string.
        /// </summary>
        public static string Prefix(string address, int length = DefaultPrefixLength)
        {
            if (string.IsNullOrEmpty(address)) return string.Empty;
            if (length <= 0) return string.Empty;
            return address.Length <= length ? address : address.Substring(0, length);
        }

        /// <summary>
        /// Display form for logs: the prefix followed by "..." when truncated.
        /// </summary>
        public static string Short(string address, int length = DefaultPrefixLength)
        {
            if (string.IsNullOrEmpty(address)) return "(unknown)";
            return address.Length <= length ? address : address.Substring(0, length) + "...";
        }
    }
}
