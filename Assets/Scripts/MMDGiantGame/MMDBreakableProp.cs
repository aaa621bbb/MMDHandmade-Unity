using System.Collections;
using UnityEngine;

namespace MMDGiantGame
{
    /// <summary>
    /// A destructible city prop (a building or a tiny person) the giant can stomp or walk over.
    /// Break feedback is transform-based (topple + sink for buildings, pop for people) so it needs no
    /// bespoke materials or particle assets; on break it reports a score value through a callback.
    /// </summary>
    public sealed class MMDBreakableProp : MonoBehaviour
    {
        [Tooltip("True = building (topples and sinks). False = person (pops down).")]
        public bool isBuilding;

        [Tooltip("Seconds the break animation runs before the object is destroyed.")]
        public float breakDuration = 1.1f;

        [Tooltip("Points awarded to the game manager when this prop is destroyed.")]
        public int scoreValue = 1;

        private bool _broken;

        /// <summary>
        /// Breaks the prop. Each prop can only break once; <paramref name="onScore"/> is invoked with the
        /// prop's <see cref="scoreValue"/> on the first break.
        /// </summary>
        public void Stomp(System.Action<int> onScore)
        {
            if (_broken)
            {
                return;
            }

            _broken = true;
            onScore?.Invoke(scoreValue);
            StartCoroutine(BreakRoutine());
        }

        private IEnumerator BreakRoutine()
        {
            if (isBuilding)
            {
                Vector3 startPos = transform.position;
                Vector3 targetPos = new Vector3(startPos.x, startPos.y - Max(4f, transform.localScale.y * 0.5f), startPos.z);
                float total = breakDuration;
                float t = 0f;

                // Topple over and sink into the ground.
                while (t < total)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / total);
                    transform.position = Vector3.Lerp(startPos, targetPos, k);
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.Euler(90f, transform.rotation.eulerAngles.y, 0f), k);
                    yield return null;
                }
            }
            else
            {
                // People pop: shrink and drop quickly.
                Vector3 start = transform.localPosition;
                float total = 0.5f;
                float t = 0f;
                while (t < total)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / total);
                    transform.localPosition = new Vector3(start.x, Mathf.Lerp(start.y, start.y - 2f, k), start.z);
                    transform.localScale = Vector3.Lerp(transform.localScale, Vector3.zero, k * 0.9f);
                    yield return null;
                }
            }

            // A short grace period so the destroyed object is visible for a moment, then remove it.
            yield return new WaitForSeconds(0.2f);
            Destroy(gameObject);
        }

        private static float Max(float a, float b)
        {
            return a > b ? a : b;
        }
    }
}
