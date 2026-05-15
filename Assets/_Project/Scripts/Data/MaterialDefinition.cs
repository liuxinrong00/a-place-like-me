using UnityEngine;

namespace APlaceLikeMe.Data
{
    [CreateAssetMenu(menuName = "A Place Like Me/Data/Material", fileName = "MaterialDefinition")]
    public sealed class MaterialDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private int defaultPrice = 1;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public int DefaultPrice => defaultPrice;
    }
}
