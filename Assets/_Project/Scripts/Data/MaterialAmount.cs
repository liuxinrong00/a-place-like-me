using System;

namespace APlaceLikeMe.Data
{
    [Serializable]
    public sealed class MaterialAmount
    {
        public MaterialDefinition material;
        public int amount = 1;
    }
}
