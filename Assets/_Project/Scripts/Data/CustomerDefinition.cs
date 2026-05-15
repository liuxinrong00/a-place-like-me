using APlaceLikeMe.Core;
using UnityEngine;

namespace APlaceLikeMe.Data
{
    [CreateAssetMenu(menuName = "A Place Like Me/Data/Customer", fileName = "CustomerDefinition")]
    public sealed class CustomerDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private CustomerType customerType;
        [SerializeField, TextArea] private string note;

        public string Id => id;
        public string DisplayName => displayName;
        public CustomerType CustomerType => customerType;
        public string Note => note;
    }
}
