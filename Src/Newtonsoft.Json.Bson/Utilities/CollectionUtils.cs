using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Newtonsoft.Json.Bson.Utilities
{
    internal static class CollectionUtils
    {
        // 4.6 has Array.Empty<T> to return a cached empty array. Lacking that in other
        // frameworks, Enumerable.Empty<T> happens to be implemented as a cached empty
        // array in all versions (in .NET Core the same instance as Array.Empty<T>).
        // This includes the internal Linq bridge for 2.0.
        // Since this method is simple and only 11 bytes long in a release build it's
        // pretty much guaranteed to be inlined, giving us fast access of that cached
        // array. With 4.5 and up we use AggressiveInlining just to be sure, so it's
        // effectively the same as calling Array.Empty<T> even when not available.
#if HAVE_METHOD_IMPL_ATTRIBUTE
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static T[] ArrayEmpty<T>()
        {
            T[] array = Enumerable.Empty<T>() as T[];
            Debug.Assert(array != null);
            // Defensively guard against a version of Linq where Enumerable.Empty<T> doesn't
            // return an array, but throw in debug versions so a better strategy can be
            // used if that ever happens.
            return array ?? new T[0];
        }
    }
}
