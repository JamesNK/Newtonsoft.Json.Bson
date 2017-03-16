#region License
// Copyright (c) 2017 James Newton-King
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.IO;
using System.Xml;
using System.Globalization;

namespace Newtonsoft.Json.Bson.Utilities
{
    internal static class DateTimeUtils
    {
        internal static readonly long InitialJavaScriptDateTicks = 621355968000000000;
        private const string IsoDateFormat = "yyyy-MM-ddTHH:mm:ss.FFFFFFFK";

        private const int DaysPer100Years = 36524;
        private const int DaysPer400Years = 146097;
        private const int DaysPer4Years = 1461;
        private const int DaysPerYear = 365;
        private const long TicksPerDay = 864000000000L;
        private static readonly int[] DaysToMonth365;
        private static readonly int[] DaysToMonth366;

        static DateTimeUtils()
        {
            DaysToMonth365 = new[] { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };
            DaysToMonth366 = new[] { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 };
        }

        public static TimeSpan GetUtcOffset(this DateTime d)
        {
            return TimeZoneInfo.Local.GetUtcOffset(d);
        }

#if !(PORTABLE)
        public static XmlDateTimeSerializationMode ToSerializationMode(DateTimeKind kind)
        {
            switch (kind)
            {
                case DateTimeKind.Local:
                    return XmlDateTimeSerializationMode.Local;
                case DateTimeKind.Unspecified:
                    return XmlDateTimeSerializationMode.Unspecified;
                case DateTimeKind.Utc:
                    return XmlDateTimeSerializationMode.Utc;
                default:
                    throw MiscellaneousUtils.CreateArgumentOutOfRangeException(nameof(kind), kind, "Unexpected DateTimeKind value.");
            }
        }
#else
        public static string ToDateTimeFormat(DateTimeKind kind)
        {
            switch (kind)
            {
                case DateTimeKind.Local:
                    return IsoDateFormat;
                case DateTimeKind.Unspecified:
                    return "yyyy-MM-ddTHH:mm:ss.FFFFFFF";
                case DateTimeKind.Utc:
                    return "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ";
                default:
                    throw MiscellaneousUtils.CreateArgumentOutOfRangeException(nameof(kind), kind, "Unexpected DateTimeKind value.");
            }
        }
#endif

        internal static DateTime EnsureDateTime(DateTime value, DateTimeZoneHandling timeZone)
        {
            switch (timeZone)
            {
                case DateTimeZoneHandling.Local:
                    value = SwitchToLocalTime(value);
                    break;
                case DateTimeZoneHandling.Utc:
                    value = SwitchToUtcTime(value);
                    break;
                case DateTimeZoneHandling.Unspecified:
                    value = new DateTime(value.Ticks, DateTimeKind.Unspecified);
                    break;
                case DateTimeZoneHandling.RoundtripKind:
                    break;
                default:
                    throw new ArgumentException("Invalid date time handling value.");
            }

            return value;
        }

        private static DateTime SwitchToLocalTime(DateTime value)
        {
            switch (value.Kind)
            {
                case DateTimeKind.Unspecified:
                    return new DateTime(value.Ticks, DateTimeKind.Local);

                case DateTimeKind.Utc:
                    return value.ToLocalTime();

                case DateTimeKind.Local:
                    return value;
            }
            return value;
        }

        private static DateTime SwitchToUtcTime(DateTime value)
        {
            switch (value.Kind)
            {
                case DateTimeKind.Unspecified:
                    return new DateTime(value.Ticks, DateTimeKind.Utc);

                case DateTimeKind.Utc:
                    return value;

                case DateTimeKind.Local:
                    return value.ToUniversalTime();
            }
            return value;
        }

        private static long ToUniversalTicks(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                return dateTime.Ticks;
            }

            return ToUniversalTicks(dateTime, dateTime.GetUtcOffset());
        }

        private static long ToUniversalTicks(DateTime dateTime, TimeSpan offset)
        {
            // special case min and max value
            // they never have a timezone appended to avoid issues
            if (dateTime.Kind == DateTimeKind.Utc || dateTime == DateTime.MaxValue || dateTime == DateTime.MinValue)
            {
                return dateTime.Ticks;
            }

            long ticks = dateTime.Ticks - offset.Ticks;
            if (ticks > 3155378975999999999L)
            {
                return 3155378975999999999L;
            }

            if (ticks < 0L)
            {
                return 0L;
            }

            return ticks;
        }

        internal static long ConvertDateTimeToJavaScriptTicks(DateTime dateTime, TimeSpan offset)
        {
            long universialTicks = ToUniversalTicks(dateTime, offset);

            return UniversialTicksToJavaScriptTicks(universialTicks);
        }

        internal static long ConvertDateTimeToJavaScriptTicks(DateTime dateTime)
        {
            return ConvertDateTimeToJavaScriptTicks(dateTime, true);
        }

        internal static long ConvertDateTimeToJavaScriptTicks(DateTime dateTime, bool convertToUtc)
        {
            long ticks = (convertToUtc) ? ToUniversalTicks(dateTime) : dateTime.Ticks;

            return UniversialTicksToJavaScriptTicks(ticks);
        }

        private static long UniversialTicksToJavaScriptTicks(long universialTicks)
        {
            long javaScriptTicks = (universialTicks - InitialJavaScriptDateTicks) / 10000;

            return javaScriptTicks;
        }

        internal static DateTime ConvertJavaScriptTicksToDateTime(long javaScriptTicks)
        {
            DateTime dateTime = new DateTime((javaScriptTicks * 10000) + InitialJavaScriptDateTicks, DateTimeKind.Utc);

            return dateTime;
        }

        #region Parse
        internal static bool TryParseDateTimeIso(string text, DateTimeZoneHandling dateTimeZoneHandling, out DateTime dt)
        {
            DateTimeParser dateTimeParser = new DateTimeParser();
            if (!dateTimeParser.Parse(text, 0, text.Length))
            {
                dt = default(DateTime);
                return false;
            }

            DateTime d = CreateDateTime(dateTimeParser);

            long ticks;

            switch (dateTimeParser.Zone)
            {
                case ParserTimeZone.Utc:
                    d = new DateTime(d.Ticks, DateTimeKind.Utc);
                    break;

                case ParserTimeZone.LocalWestOfUtc:
                    {
                        TimeSpan offset = new TimeSpan(dateTimeParser.ZoneHour, dateTimeParser.ZoneMinute, 0);
                        ticks = d.Ticks + offset.Ticks;
                        if (ticks <= DateTime.MaxValue.Ticks)
                        {
                            d = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                        }
                        else
                        {
                            ticks += d.GetUtcOffset().Ticks;
                            if (ticks > DateTime.MaxValue.Ticks)
                            {
                                ticks = DateTime.MaxValue.Ticks;
                            }

                            d = new DateTime(ticks, DateTimeKind.Local);
                        }
                        break;
                    }
                case ParserTimeZone.LocalEastOfUtc:
                    {
                        TimeSpan offset = new TimeSpan(dateTimeParser.ZoneHour, dateTimeParser.ZoneMinute, 0);
                        ticks = d.Ticks - offset.Ticks;
                        if (ticks >= DateTime.MinValue.Ticks)
                        {
                            d = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                        }
                        else
                        {
                            ticks += d.GetUtcOffset().Ticks;
                            if (ticks < DateTime.MinValue.Ticks)
                            {
                                ticks = DateTime.MinValue.Ticks;
                            }

                            d = new DateTime(ticks, DateTimeKind.Local);
                        }
                        break;
                    }
            }

            dt = EnsureDateTime(d, dateTimeZoneHandling);
            return true;
        }

#if HAVE_DATE_TIME_OFFSET
        internal static bool TryParseDateTimeOffsetIso(string text, out DateTimeOffset dt)
        {
            DateTimeParser dateTimeParser = new DateTimeParser();
            if (!dateTimeParser.Parse(text, 0, text.Length))
            {
                dt = default(DateTimeOffset);
                return false;
            }

            DateTime d = CreateDateTime(dateTimeParser);

            TimeSpan offset;

            switch (dateTimeParser.Zone)
            {
                case ParserTimeZone.Utc:
                    offset = new TimeSpan(0L);
                    break;
                case ParserTimeZone.LocalWestOfUtc:
                    offset = new TimeSpan(-dateTimeParser.ZoneHour, -dateTimeParser.ZoneMinute, 0);
                    break;
                case ParserTimeZone.LocalEastOfUtc:
                    offset = new TimeSpan(dateTimeParser.ZoneHour, dateTimeParser.ZoneMinute, 0);
                    break;
                default:
                    offset = TimeZoneInfo.Local.GetUtcOffset(d);
                    break;
            }

            long ticks = d.Ticks - offset.Ticks;
            if (ticks < 0 || ticks > 3155378975999999999)
            {
                dt = default(DateTimeOffset);
                return false;
            }

            dt = new DateTimeOffset(d, offset);
            return true;
        }
#endif

        private static DateTime CreateDateTime(DateTimeParser dateTimeParser)
        {
            bool is24Hour;
            if (dateTimeParser.Hour == 24)
            {
                is24Hour = true;
                dateTimeParser.Hour = 0;
            }
            else
            {
                is24Hour = false;
            }

            DateTime d = new DateTime(dateTimeParser.Year, dateTimeParser.Month, dateTimeParser.Day, dateTimeParser.Hour, dateTimeParser.Minute, dateTimeParser.Second);
            d = d.AddTicks(dateTimeParser.Fraction);

            if (is24Hour)
            {
                d = d.AddDays(1);
            }
            return d;
        }

        internal static bool TryParseDateTime(string s, DateTimeZoneHandling dateTimeZoneHandling, string dateFormatString, CultureInfo culture, out DateTime dt)
        {
            if (s.Length > 0)
            {
                if (s[0] == '/')
                {
                    if (s.Length >= 9 && s.StartsWith("/Date(", StringComparison.Ordinal) && s.EndsWith(")/", StringComparison.Ordinal))
                    {
                        if (TryParseDateTimeMicrosoft(s, dateTimeZoneHandling, out dt))
                        {
                            return true;
                        }
                    }
                }
                else if (s.Length >= 19 && s.Length <= 40 && char.IsDigit(s[0]) && s[10] == 'T')
                {
                    if (DateTime.TryParseExact(s, IsoDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                    {
                        dt = EnsureDateTime(dt, dateTimeZoneHandling);
                        return true;
                    }
                }

                if (!string.IsNullOrEmpty(dateFormatString))
                {
                    if (TryParseDateTimeExact(s, dateTimeZoneHandling, dateFormatString, culture, out dt))
                    {
                        return true;
                    }
                }
            }

            dt = default(DateTime);
            return false;
        }

#if HAVE_DATE_TIME_OFFSET
        internal static bool TryParseDateTimeOffset(string s, string dateFormatString, CultureInfo culture, out DateTimeOffset dt)
        {
            if (s.Length > 0)
            {
                if (s[0] == '/')
                {
                    if (s.Length >= 9 && s.StartsWith("/Date(", StringComparison.Ordinal) && s.EndsWith(")/", StringComparison.Ordinal))
                    {
                        if (TryParseDateTimeOffsetMicrosoft(s, out dt))
                        {
                            return true;
                        }
                    }
                }
                else if (s.Length >= 19 && s.Length <= 40 && char.IsDigit(s[0]) && s[10] == 'T')
                {
                    if (DateTimeOffset.TryParseExact(s, IsoDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                    {
                        if (TryParseDateTimeOffsetIso(s, out dt))
                        {
                            return true;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(dateFormatString))
                {
                    if (TryParseDateTimeOffsetExact(s, dateFormatString, culture, out dt))
                    {
                        return true;
                    }
                }
            }

            dt = default(DateTimeOffset);
            return false;
        }
#endif

        private static bool TryParseMicrosoftDate(string text, out long ticks, out TimeSpan offset, out DateTimeKind kind)
        {
            kind = DateTimeKind.Utc;

            int index = text.IndexOf('+', 7, text.Length - 8);

            if (index == -1)
            {
                index = text.IndexOf('-', 7, text.Length - 8);
            }

            if (index != -1)
            {
                kind = DateTimeKind.Local;

                if (!TryReadOffset(text, index, out offset))
                {
                    ticks = 0;
                    return false;
                }
            }
            else
            {
                offset = TimeSpan.Zero;
                index = text.Length - 2;
            }

            return long.TryParse(text.Substring(6, index - 6), out ticks);
        }

        private static bool TryParseDateTimeMicrosoft(string text, DateTimeZoneHandling dateTimeZoneHandling, out DateTime dt)
        {
            long ticks;
            TimeSpan offset;
            DateTimeKind kind;

            if (!TryParseMicrosoftDate(text, out ticks, out offset, out kind))
            {
                dt = default(DateTime);
                return false;
            }

            DateTime utcDateTime = ConvertJavaScriptTicksToDateTime(ticks);

            switch (kind)
            {
                case DateTimeKind.Unspecified:
                    dt = DateTime.SpecifyKind(utcDateTime.ToLocalTime(), DateTimeKind.Unspecified);
                    break;
                case DateTimeKind.Local:
                    dt = utcDateTime.ToLocalTime();
                    break;
                default:
                    dt = utcDateTime;
                    break;
            }

            dt = EnsureDateTime(dt, dateTimeZoneHandling);
            return true;
        }

        private static bool TryParseDateTimeExact(string text, DateTimeZoneHandling dateTimeZoneHandling, string dateFormatString, CultureInfo culture, out DateTime dt)
        {
            DateTime temp;
            if (DateTime.TryParseExact(text, dateFormatString, culture, DateTimeStyles.RoundtripKind, out temp))
            {
                temp = EnsureDateTime(temp, dateTimeZoneHandling);
                dt = temp;
                return true;
            }

            dt = default(DateTime);
            return false;
        }

#if HAVE_DATE_TIME_OFFSET
        private static bool TryParseDateTimeOffsetMicrosoft(string text, out DateTimeOffset dt)
        {
            long ticks;
            TimeSpan offset;
            DateTimeKind kind;

            if (!TryParseMicrosoftDate(text, out ticks, out offset, out kind))
            {
                dt = default(DateTime);
                return false;
            }

            DateTime utcDateTime = ConvertJavaScriptTicksToDateTime(ticks);

            dt = new DateTimeOffset(utcDateTime.Add(offset).Ticks, offset);
            return true;
        }

        private static bool TryParseDateTimeOffsetExact(string text, string dateFormatString, CultureInfo culture, out DateTimeOffset dt)
        {
            DateTimeOffset temp;
            if (DateTimeOffset.TryParseExact(text, dateFormatString, culture, DateTimeStyles.RoundtripKind, out temp))
            {
                dt = temp;
                return true;
            }

            dt = default(DateTimeOffset);
            return false;
        }
#endif

        private static bool TryReadOffset(string offsetText, int startIndex, out TimeSpan offset)
        {
            bool negative = (offsetText[startIndex] == '-');

            int hours;
            if (int.TryParse(offsetText.Substring(startIndex + 1, 2), out hours))
            {
                offset = default(TimeSpan);
                return false;
            }

            int minutes = 0;
            if (offsetText.Length - startIndex > 5)
            {
                if (int.TryParse(offsetText.Substring(startIndex + 3, 2), out minutes))
                {
                    offset = default(TimeSpan);
                    return false;
                }
            }

            offset = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
            if (negative)
            {
                offset = offset.Negate();
            }

            return true;
        }
        #endregion
    }
}