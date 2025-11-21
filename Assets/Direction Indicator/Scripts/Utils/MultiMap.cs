using System.Collections.Generic;

namespace DIndicator.Utils
{
    public class MultiMap<TKey, TValue>
    {
        private Dictionary<TKey, List<TValue>> dict = new Dictionary<TKey, List<TValue>>();

        public void Add(TKey key, TValue value)
        {
            if (dict.ContainsKey(key))
            {
                dict[key].Add(value);
            }
            else
            {
                List<TValue> list = new List<TValue>();
                list.Add(value);
                dict.Add(key, list);
            }
        }

        public bool Remove(TKey key, TValue value)
        {
            if (!dict.ContainsKey(key))
            {
                return false;
            }

            List<TValue> list = dict[key];
            bool removed = list.Remove(value);

            if (list.Count == 0)
            {
                dict.Remove(key);
            }

            return removed;
        }

        public bool TryGetValue(TKey key, out List<TValue> values)
        {
            return dict.TryGetValue(key, out values);
        }

        public void Clear()
        {
            dict.Clear();
        }
    }
}