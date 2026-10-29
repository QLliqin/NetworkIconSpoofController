using System;
using System.Globalization;

internal enum ConversionRegion
{
    UnitedStates,
    China,
    UnitedKingdom,
    Default
}

internal static class TimestampConverter
{
    private const long UnixEpochTicks = 621355968000000000L;
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff"
    };

    public static string GetRegionName(ConversionRegion region)
    {
        switch (region)
        {
            case ConversionRegion.UnitedStates: return "美国";
            case ConversionRegion.China: return "中国";
            case ConversionRegion.UnitedKingdom: return "英国";
            case ConversionRegion.Default: return "默认";
            default: throw new ArgumentOutOfRangeException("region");
        }
    }

    public static TimeZoneInfo GetTimeZone(ConversionRegion region)
    {
        switch (region)
        {
            case ConversionRegion.UnitedStates:
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            case ConversionRegion.China:
                return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            case ConversionRegion.UnitedKingdom:
                return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            case ConversionRegion.Default:
                return TimeZoneInfo.Local;
            default:
                throw new ArgumentOutOfRangeException("region");
        }
    }

    public static string GetTimeZoneDescription(ConversionRegion region)
    {
        switch (region)
        {
            case ConversionRegion.UnitedStates: return "美国东部时间（纽约，自动夏令时）";
            case ConversionRegion.China: return "中国标准时间（北京，UTC+08:00）";
            case ConversionRegion.UnitedKingdom: return "英国时间（伦敦，自动夏令时）";
            case ConversionRegion.Default: return "本机时区：" + TimeZoneInfo.Local.DisplayName;
            default: throw new ArgumentOutOfRangeException("region");
        }
    }

    public static DateTimeOffset FromTimestamp(string input, bool milliseconds, ConversionRegion region)
    {
        long value;
        if (string.IsNullOrWhiteSpace(input)
            || !long.TryParse(input.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            throw new FormatException("请输入整数时间戳，并选择秒或毫秒。支持 1970 年之前的负数时间戳。");
        }

        long utcTicks;
        try
        {
            utcTicks = checked(UnixEpochTicks + checked(value * UnitTicks(milliseconds)));
        }
        catch (OverflowException)
        {
            throw TimestampOutOfRange();
        }

        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            throw TimestampOutOfRange();
        }

        var utc = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        TimeSpan offset = GetTimeZone(region).GetUtcOffset(utc);
        long localTicks = utcTicks + offset.Ticks;
        if (localTicks < DateTime.MinValue.Ticks || localTicks > DateTime.MaxValue.Ticks)
        {
            throw TimestampOutOfRange();
        }
        return new DateTimeOffset(localTicks, offset);
    }

    public static long ToTimestamp(string input, bool milliseconds, ConversionRegion region)
    {
        DateTime localDate;
        if (string.IsNullOrWhiteSpace(input)
            || !DateTime.TryParseExact(input.Trim(), DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out localDate))
        {
            throw new FormatException("请输入有效日期，格式为 yyyy-MM-dd HH:mm:ss 或 yyyy-MM-dd HH:mm:ss.fff。");
        }

        localDate = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        TimeZoneInfo zone = GetTimeZone(region);
        if (zone.IsInvalidTime(localDate))
        {
            throw new ArgumentException("该当地时间处于夏令时跳过的时段，不存在。请调整日期或时间。", "input");
        }
        if (zone.IsAmbiguousTime(localDate))
        {
            throw new ArgumentException("该当地时间在夏令时结束时出现两次，无法确定唯一时间戳。请调整日期或时间。", "input");
        }

        // Avoid ConvertTimeToUtc's boundary clamping: an unrepresentable instant is an error.
        long utcTicks = localDate.Ticks - zone.GetUtcOffset(localDate).Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            throw TimestampOutOfRange();
        }

        long elapsedTicks = utcTicks - UnixEpochTicks;
        long unitTicks = UnitTicks(milliseconds);
        long result = elapsedTicks / unitTicks;
        // Unix units count whole intervals since the epoch, including for negative instants.
        if (elapsedTicks < 0 && elapsedTicks % unitTicks != 0)
        {
            result--;
        }
        return result;
    }

    private static long UnitTicks(bool milliseconds)
    {
        return milliseconds ? TimeSpan.TicksPerMillisecond : TimeSpan.TicksPerSecond;
    }

    private static ArgumentOutOfRangeException TimestampOutOfRange()
    {
        return new ArgumentOutOfRangeException("input", "日期超出支持范围（公元 0001 至 9999 年），或所选时区超出此范围。请检查秒/毫秒单位。");
    }
}
