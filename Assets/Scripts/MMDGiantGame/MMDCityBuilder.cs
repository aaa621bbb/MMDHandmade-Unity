using System.Collections.Generic;
using UnityEngine;

namespace MMDGiantGame
{
    /// <summary>
    /// Procedurally builds the "little city" of buildings and tiny people that the giant stomps.
    /// Deterministic via a seed so the same layout is reproducible (useful for tuning / replayability).
    /// Pure runtime generation: no prefabs or external assets required.
    /// </summary>
    public sealed class MMDCityBuilder : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Number of cells per side of the city grid.")]
        public int gridCells = 9;
        [Tooltip("Spacing (world units) between grid cells.")]
        public float cellSize = 16f;
        [Tooltip("Random seed for a reproducible city layout.")]
        public int seed = 424242;

        [Header("Density")]
        [Range(0f, 1f)] public float buildingChance = 0.6f;
        [Range(0f, 1f)] public float personChance = 0.45f;

        private readonly List<MMDBreakableProp> _props = new List<MMDBreakableProp>();

        /// <summary>All spawned props; read by the game manager to find what is stompable.</summary>
        public IReadOnlyList<MMDBreakableProp> Props => _props;

        /// <summary>Clears any existing props and regenerates the whole city.</summary>
        public void Build()
        {
            Clear();

            System.Random rng = new System.Random(seed);
            float half = gridCells * cellSize * 0.5f;
            int centerX = gridCells / 2;
            int centerZ = gridCells / 2;

            for (int x = 0; x < gridCells; ++x)
            {
                for (int z = 0; z < gridCells; ++z)
                {
                    // Leave the very centre cell free so the giant has somewhere to stand.
                    if (x == centerX && z == centerZ)
                    {
                        continue;
                    }

                    Vector3 pos = new Vector3(x * cellSize - half, 0f, z * cellSize - half);

                    if (rng.NextDouble() < personChance)
                    {
                        SpawnPerson(pos, rng);
                    }
                    else if (rng.NextDouble() < buildingChance)
                    {
                        SpawnBuilding(pos, rng);
                    }
                }
            }
        }

        /// <summary>Destroys all current props. Idempotent; safe to call on an empty city.</summary>
        public void Clear()
        {
            for (int i = _props.Count - 1; i >= 0; --i)
            {
                MMDBreakableProp prop = _props[i];
                if (prop != null)
                {
                    Destroy(prop.gameObject);
                }
            }
            _props.Clear();
        }

        /// <summary>
        /// Removes the given props from the active list so they are no longer stompable. The objects
        /// themselves are destroyed by <see cref="MMDBreakableProp"/> after its break animation.
        /// </summary>
        public void RemoveProps(IReadOnlyCollection<MMDBreakableProp> props)
        {
            if (props == null)
            {
                return;
            }

            foreach (MMDBreakableProp prop in props)
            {
                _props.Remove(prop);
            }
        }

        private void SpawnBuilding(Vector3 pos, System.Random rng)
        {
            float width = 2.5f + (float)rng.NextDouble() * 4f;
            float height = 7f + (float)rng.NextDouble() * 16f;
            float depth = 2.5f + (float)rng.NextDouble() * 4f;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Building";
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, height * 0.5f, pos.z);
            go.transform.localScale = new Vector3(width, height, depth);

            SetColor(go.GetComponent<Renderer>(), Color.HSVToRGB((float)rng.NextDouble(), 0.35f, 0.8f));

            MMDBreakableProp prop = go.AddComponent<MMDBreakableProp>();
            prop.isBuilding = true;
            prop.scoreValue = 5;
            prop.breakDuration = 1.1f;
            _props.Add(prop);
        }

        private void SpawnPerson(Vector3 pos, System.Random rng)
        {
            float height = 1.5f + (float)rng.NextDouble() * 0.9f;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Person";
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, height * 0.5f, pos.z);
            go.transform.localScale = new Vector3(0.6f, height, 0.6f);

            SetColor(go.GetComponent<Renderer>(), new Color(0.2f, 0.55f, 1f, 1f));

            MMDBreakableProp prop = go.AddComponent<MMDBreakableProp>();
            prop.isBuilding = false;
            prop.scoreValue = 1;
            prop.breakDuration = 0.5f;
            _props.Add(prop);
        }

        private static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            Shader standard = Shader.Find("Standard");
            if (standard == null)
            {
                // Built-in Standard unavailable; leave the primitive's default grey material.
                return;
            }

            Material material = new Material(standard);
            material.color = color;
            renderer.material = material;
        }
    }
}
