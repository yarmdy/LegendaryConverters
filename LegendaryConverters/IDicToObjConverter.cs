using System.Collections.Generic;

namespace LegendaryConverters
{
    public interface IDicToObjConverter
    {
        T Convert<T>(IDictionary<string, object?> dic) where T : class, new();
    }
}