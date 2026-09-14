using System.Globalization;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// Writes the numbers of the dump. The log is read by a spreadsheet or a script, so they are written
    /// with a dot whatever the machine's locale says.
    /// </summary>
    internal static class FormatUtils
    {
        public static string F(double value, int decimals)
        {
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        public static string I(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string L(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
