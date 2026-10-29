using System;
using System.Globalization;

internal static class TimestampConverterTests
{
    private static int assertions;

    public static int Main()
    {
        try
        {
            Equal("美国", TimestampConverter.GetRegionName(ConversionRegion.UnitedStates), "US label");
            Equal("中国", TimestampConverter.GetRegionName(ConversionRegion.China), "China label");
            Equal("英国", TimestampConverter.GetRegionName(ConversionRegion.UnitedKingdom), "UK label");
            Equal("默认", TimestampConverter.GetRegionName(ConversionRegion.Default), "default label");
            Equal(TimeZoneInfo.Local.Id, TimestampConverter.GetTimeZone(ConversionRegion.Default).Id, "default uses local zone");

            Date("0", false, ConversionRegion.China, "1970-01-01 08:00:00.000 +08:00");
            Date("0", true, ConversionRegion.China, "1970-01-01 08:00:00.000 +08:00");
            Date("-1", true, ConversionRegion.China, "1970-01-01 07:59:59.999 +08:00");
            Date("-1", false, ConversionRegion.China, "1970-01-01 07:59:59.000 +08:00");
            Date("  +1001  ", true, ConversionRegion.China, "1970-01-01 08:00:01.001 +08:00");
            Equal(0L, TimestampConverter.ToTimestamp("1970-01-01 08:00:00", false, ConversionRegion.China), "epoch to seconds");
            Equal(1L, TimestampConverter.ToTimestamp("1970-01-01 08:00:00.001", true, ConversionRegion.China), "positive milliseconds");
            Equal(-1L, TimestampConverter.ToTimestamp("1970-01-01 07:59:59.999", true, ConversionRegion.China), "negative milliseconds");
            Equal(-1L, TimestampConverter.ToTimestamp("1970-01-01 07:59:59.999", false, ConversionRegion.China), "negative seconds floor");
            Equal(-2L, TimestampConverter.ToTimestamp("1970-01-01 07:59:58.999", false, ConversionRegion.China), "negative seconds floor before -1");
            Equal(1L, TimestampConverter.ToTimestamp("1970-01-01 08:00:01.999", false, ConversionRegion.China), "positive seconds floor");

            Date("1705320000", false, ConversionRegion.UnitedStates, "2024-01-15 07:00:00.000 -05:00");
            Date("1721044800", false, ConversionRegion.UnitedStates, "2024-07-15 08:00:00.000 -04:00");
            Date("1705320000", false, ConversionRegion.UnitedKingdom, "2024-01-15 12:00:00.000 +00:00");
            Date("1721044800", false, ConversionRegion.UnitedKingdom, "2024-07-15 13:00:00.000 +01:00");
            Equal(1705320000L, TimestampConverter.ToTimestamp("2024-01-15 07:00:00", false, ConversionRegion.UnitedStates), "US winter reverse");
            Equal(1721044800L, TimestampConverter.ToTimestamp("2024-07-15 08:00:00", false, ConversionRegion.UnitedStates), "US summer reverse");
            Equal(1705320000L, TimestampConverter.ToTimestamp("2024-01-15 12:00:00", false, ConversionRegion.UnitedKingdom), "UK winter reverse");
            Equal(1721044800L, TimestampConverter.ToTimestamp("2024-07-15 13:00:00", false, ConversionRegion.UnitedKingdom), "UK summer reverse");
            Reject<ArgumentException>(delegate { TimestampConverter.ToTimestamp("2024-03-10 02:30:00", false, ConversionRegion.UnitedStates); }, "US spring gap");
            Reject<ArgumentException>(delegate { TimestampConverter.ToTimestamp("2024-11-03 01:30:00", false, ConversionRegion.UnitedStates); }, "US autumn fold");
            Reject<ArgumentException>(delegate { TimestampConverter.ToTimestamp("2024-03-31 01:30:00", false, ConversionRegion.UnitedKingdom); }, "UK spring gap");
            Reject<ArgumentException>(delegate { TimestampConverter.ToTimestamp("2024-10-27 01:30:00", false, ConversionRegion.UnitedKingdom); }, "UK autumn fold");
            Date("1730611800", false, ConversionRegion.UnitedStates, "2024-11-03 01:30:00.000 -04:00");
            Date("1730615400", false, ConversionRegion.UnitedStates, "2024-11-03 01:30:00.000 -05:00");

            Reject<FormatException>(delegate { TimestampConverter.FromTimestamp("", false, ConversionRegion.China); }, "empty timestamp");
            Reject<FormatException>(delegate { TimestampConverter.FromTimestamp("1.5", false, ConversionRegion.China); }, "fractional timestamp");
            Reject<FormatException>(delegate { TimestampConverter.FromTimestamp("1,000", false, ConversionRegion.China); }, "grouped timestamp");
            Reject<FormatException>(delegate { TimestampConverter.ToTimestamp("2024-02-30 12:00:00", false, ConversionRegion.China); }, "invalid calendar date");
            Reject<FormatException>(delegate { TimestampConverter.ToTimestamp("2024-01-01T12:00:00Z", false, ConversionRegion.China); }, "wrong format");
            Reject<ArgumentOutOfRangeException>(delegate { TimestampConverter.FromTimestamp(long.MaxValue.ToString(), false, ConversionRegion.China); }, "large seconds overflow");
            Reject<ArgumentOutOfRangeException>(delegate { TimestampConverter.FromTimestamp(long.MinValue.ToString(), true, ConversionRegion.China); }, "negative milliseconds overflow");
            Reject<ArgumentOutOfRangeException>(delegate { TimestampConverter.FromTimestamp("253402300800", false, ConversionRegion.UnitedKingdom); }, "year 10000");
            Reject<ArgumentOutOfRangeException>(delegate { TimestampConverter.FromTimestamp("-62135596801", false, ConversionRegion.China); }, "year zero");
            Reject<ArgumentOutOfRangeException>(delegate { TimestampConverter.FromTimestamp("253402300799999", true, ConversionRegion.China); }, "regional upper overflow");
            Reject<ArgumentOutOfRangeException>(delegate { TimestampConverter.ToTimestamp("0001-01-01 00:00:00", true, ConversionRegion.China); }, "UTC lower overflow");
            Equal(-62135596800000L, TimestampConverter.ToTimestamp("0001-01-01 08:00:00", true, ConversionRegion.China), "minimum instant");
            Equal(253402271999999L, TimestampConverter.ToTimestamp("9999-12-31 23:59:59.999", true, ConversionRegion.China), "maximum regional date");

            long[] samples = { -2208988800000L, -1001L, -1L, 0L, 1L, 1001L, 1721044800123L };
            foreach (ConversionRegion region in Enum.GetValues(typeof(ConversionRegion)))
            {
                foreach (long value in samples)
                {
                    DateTimeOffset converted = TimestampConverter.FromTimestamp(value.ToString(CultureInfo.InvariantCulture), true, region);
                    Equal(value, TimestampConverter.ToTimestamp(converted.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), true, region), "round trip " + region + " " + value);
                }
            }

            Console.WriteLine("PASS: " + assertions + " timestamp conversion assertions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void Date(string value, bool milliseconds, ConversionRegion region, string expected)
    {
        Equal(expected, TimestampConverter.FromTimestamp(value, milliseconds, region).ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture), "timestamp " + value + " " + region);
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        assertions++;
        if (!object.Equals(expected, actual))
        {
            throw new Exception(description + ": expected " + expected + ", got " + actual);
        }
    }

    private static void Reject<T>(Action action, string description) where T : Exception
    {
        assertions++;
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception(description + ": expected " + typeof(T).Name);
    }
}
