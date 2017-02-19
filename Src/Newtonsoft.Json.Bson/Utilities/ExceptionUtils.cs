using System;
using System.Globalization;

namespace Newtonsoft.Json.Bson.Utilities
{
    internal static class ExceptionUtils
    {
        internal static JsonReaderException CreateJsonReaderException(JsonReader reader, string message)
        {
            return CreateJsonReaderException(reader, message, null);
        }

        internal static JsonReaderException CreateJsonReaderException(JsonReader reader, string message, Exception ex)
        {
            return CreateJsonReaderException(reader as IJsonLineInfo, reader.Path, message, ex);
        }

        internal static JsonReaderException CreateJsonReaderException(IJsonLineInfo lineInfo, string path, string message, Exception ex)
        {
            message = FormatMessage(lineInfo, path, message);

            int lineNumber;
            int linePosition;
            if (lineInfo != null && lineInfo.HasLineInfo())
            {
                lineNumber = lineInfo.LineNumber;
                linePosition = lineInfo.LinePosition;
            }
            else
            {
                lineNumber = 0;
                linePosition = 0;
            }

            return new JsonReaderException(message, path, lineNumber, linePosition, ex);
        }

        internal static JsonWriterException CreateJsonWriterException(JsonWriter writer, string message, Exception ex)
        {
            return CreateJsonWriterException(writer.Path, message, ex);
        }

        internal static JsonWriterException CreateJsonWriterException(string path, string message, Exception ex)
        {
            message = FormatMessage(null, path, message);

            return new JsonWriterException(message, path, ex);
        }

        internal static JsonSerializationException CreateJsonSerializationException(IJsonLineInfo lineInfo, string path, string message, Exception ex)
        {
            message = FormatMessage(lineInfo, path, message);

            return new JsonSerializationException(message, ex);
        }

        private static string FormatMessage(IJsonLineInfo lineInfo, string path, string message)
        {
            // don't add a fullstop and space when message ends with a new line
            if (!message.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                message = message.Trim();

                if (!message.EndsWith('.'))
                {
                    message += ".";
                }

                message += " ";
            }

            message += "Path '{0}'".FormatWith(CultureInfo.InvariantCulture, path);

            if (lineInfo != null && lineInfo.HasLineInfo())
            {
                message += ", line {0}, position {1}".FormatWith(CultureInfo.InvariantCulture, lineInfo.LineNumber, lineInfo.LinePosition);
            }

            message += ".";

            return message;
        }
    }
}
