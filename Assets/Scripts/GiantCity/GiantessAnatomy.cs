using System.Collections.Generic;
using UnityEngine;
using UMT;

namespace GiantCity
{
    /// <summary>
    /// Locates the giantess's feet/ankles and head on the <see cref="MMDTransformManager"/> bones so the
    /// <see cref="StompDetector"/> can hang foot colliders and the <see cref="GiantessBrain"/> can lean the
    /// head down to look at the tiny player.
    ///
    /// PMX bone names are the model's original localised names (see <c>MMDBoneTransform.boneName</c>), which
    /// differ per model, so we match by a broad list of keywords plus L/R hints. The matched bones are cached
    /// on first scan; call <see cref="Rescan"/> if a new giantess is loaded.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiantessAnatomy : MonoBehaviour
    {
        /// <summary>Keywords that identify a foot/ankle bone (matched against the localised bone name).</summary>
        private static readonly string[] FootKeywords =
        {
            "足首", "足", "foot", "ankle", "toe", "指先", "足先", "mid", "ball",
        };

        /// <summary>Keywords that identify the head bone.</summary>
        private static readonly string[] HeadKeywords =
        {
            "頭", "头", "head", "neck", "首",
        };

        private static readonly string[] LeftHints = { "左", "Ｌ", "l_", "left", "Ｌ", "l" };
        private static readonly string[] RightHints = { "右", "Ｒ", "r_", "right", "ｒ", "r" };

        /// <summary>True when the left-foot bone was found.</summary>
        public bool HasLeftFoot => _leftFoot != null;
        /// <summary>True when the right-foot bone was found.</summary>
        public bool HasRightFoot => _rightFoot != null;

        private Transform _leftFoot;
        private Transform _rightFoot;
        private Transform _head;

        /// <summary>The left foot/ankle bone transform, or null when it was not found.</summary>
        public Transform LeftFoot => _leftFoot;
        /// <summary>The right foot/ankle bone transform, or null when it was not found.</summary>
        public Transform RightFoot => _rightFoot;
        /// <summary>The head bone transform, or null when it was not found.</summary>
        public Transform Head => _head;

        /// <summary>Scans the given transform manager's bones and caches the foot/head transforms.</summary>
        public void Rescan(MMDTransformManager tm)
        {
            _leftFoot = null;
            _rightFoot = null;
            _head = null;

            if (tm == null || tm.bones == null)
            {
                return;
            }

            // Walk in PMX order; later, more specific matches (左足首 before 足) win by being more specific,
            // so we keep both a generic candidate and update it when a left/right-specific one appears.
            foreach (MMDBoneTransform bone in tm.bones)
            {
                if (bone == null)
                {
                    continue;
                }

                string name = bone.boneName;
                if (string.IsNullOrEmpty(name))
                {
                    name = bone.name;
                }

                if (IsHead(name))
                {
                    if (_head == null)
                    {
                        _head = bone.transform;
                    }
                    continue;
                }

                if (IsFoot(name))
                {
                    bool isLeft = ContainsAny(name, LeftHints);
                    bool isRight = ContainsAny(name, RightHints);
                    if (isLeft && !isRight)
                    {
                        _leftFoot = bone.transform;
                    }
                    else if (isRight && !isLeft)
                    {
                        _rightFoot = bone.transform;
                    }
                    else if (_leftFoot == null)
                    {
                        // Ambiguous name: keep as the left foot unless a clearer one turns up.
                        _leftFoot = bone.transform;
                    }
                }
            }
        }

        private static bool IsFoot(string name)
        {
            return ContainsAny(name, FootKeywords);
        }

        private static bool IsHead(string name)
        {
            // "首" (neck/head) alone is too broad, so we require a head-specific match for the CJK term.
            bool cjkHead = name.Contains("頭") || name.Contains("头");
            if (cjkHead)
            {
                return true;
            }
            return name.Contains("head");
        }

        private static bool ContainsAny(string value, string[] candidates)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }
                if (value.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
