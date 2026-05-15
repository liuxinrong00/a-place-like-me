using System;

namespace APlaceLikeMe.Data
{
    [Serializable]
    public sealed class InitialMaterialStock
    {
        public MaterialDefinition material;
        public int amount = 3;
    }
}
