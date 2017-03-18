using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Newtonsoft.Json.Bson.Utilities
{
    internal static class ConvertUtils
    {
        public static bool TryConvertGuid(string s, out Guid g)
        {
            // GUID has to have format 00000000-0000-0000-0000-000000000000
#if !HAVE_GUID_TRY_PARSE
            if (s == null)
            {
                throw new ArgumentNullException("s");
            }

            Regex format = new Regex("^[A-Fa-f0-9]{8}-([A-Fa-f0-9]{4}-){3}[A-Fa-f0-9]{12}$");
            Match match = format.Match(s);
            if (match.Success)
            {
                g = new Guid(s);
                return true;
            }

            g = Guid.Empty;
            return false;
#else
            return Guid.TryParseExact(s, "D", out g);
#endif
        }
    }
}
