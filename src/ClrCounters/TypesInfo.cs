using System.Collections.Generic;

namespace ClrCounters
{
    public class TypesInfo
    {
        private readonly Dictionary<ulong, string> _typesIdToName;

        public TypesInfo()
        {
            _typesIdToName = new Dictionary<ulong, string>();
        }

        public string this[ulong id]
        {
            get
            {
                if (!_typesIdToName.ContainsKey(id))
                    return null;

                return _typesIdToName[id];
            }
            set => _typesIdToName[id] = value;
        }
    }
}
