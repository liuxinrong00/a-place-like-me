using UnityEngine;

namespace APlaceLikeMe.UI
{
    public sealed class PrototypeScenePlayerController : MonoBehaviour
    {
        private const float MoveSpeed = 4.2f;

        private PrototypeGameController host;
        private Rect movementBounds;

        public void Configure(PrototypeGameController controller, Rect bounds)
        {
            host = controller;
            movementBounds = bounds;
        }

        private void Update()
        {
            if (host == null || host.AreWorldControlsLocked)
            {
                return;
            }

            var movement = Vector2.zero;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                movement.x -= 1f;
            }

            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                movement.x += 1f;
            }

            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                movement.y -= 1f;
            }

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                movement.y += 1f;
            }

            if (movement == Vector2.zero)
            {
                return;
            }

            movement = movement.normalized * MoveSpeed * Time.deltaTime;
            var nextPosition = (Vector2)transform.position + movement;
            nextPosition.x = Mathf.Clamp(nextPosition.x, movementBounds.xMin, movementBounds.xMax);
            nextPosition.y = Mathf.Clamp(nextPosition.y, movementBounds.yMin, movementBounds.yMax);
            transform.position = new Vector3(nextPosition.x, nextPosition.y, transform.position.z);
        }
    }
}
