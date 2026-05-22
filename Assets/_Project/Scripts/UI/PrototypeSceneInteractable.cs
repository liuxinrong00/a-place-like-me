using UnityEngine;

namespace APlaceLikeMe.UI
{
    public sealed class PrototypeSceneInteractable : MonoBehaviour
    {
        [SerializeField] private PrototypeSceneInteractionKind kind;
        [SerializeField] private string prompt;

        public PrototypeSceneInteractionKind Kind => kind;
        public string Prompt => prompt;

        public void Configure(PrototypeSceneInteractionKind interactionKind, string interactionPrompt)
        {
            kind = interactionKind;
            prompt = interactionPrompt;
        }

        private void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.55f, 0.9f, 0.35f);
            Gizmos.DrawCube(transform.position + (Vector3)box.offset, box.size);
        }
    }
}
